using PeachDrawing.Text.Internal.Hinting.FreeType;
using System;

namespace PeachDrawing.Text.Internal.Hinting;

/// <summary>
/// The objects of Adobe's CFF engine that are big and that every glyph load needs, kept by the thread that loads glyphs so that the next glyph
/// finds them: a hint map has an array of 192 edges (about 4 KB), and a glyph makes three of them, a fourth for a counter mask, and a
/// temporary mask for each map it builds. This is PeachDrawing.Text's own code; it contains nothing of FreeType's.
/// </summary>
/// <remarks>
/// A thread that loads glyphs is the only one to touch its pool, so there is no locking. An object that is rented and not given back (a load
/// that throws) is simply left to the garbage collector. What is given back is released first, so that the pool does not keep a font, a size
/// or a decoder alive from a thread-static.
/// </remarks>
internal static class Cf2Pool
{
    /// <summary>How many of each kind a thread keeps: a load has at most four maps and a few masks at a time.</summary>
    private const int MaxKept = 8;

    [ThreadStatic]
    private static Cf2HintMap?[]? s_maps;

    [ThreadStatic]
    private static int s_mapCount;

    [ThreadStatic]
    private static Cf2HintMask?[]? s_masks;

    [ThreadStatic]
    private static int s_maskCount;

    /// <summary>A hint map: one of the thread's, or a new one. Its state is what the last glyph left; <c>Cf2HintMap.Init</c> sets what a map reads.</summary>
    public static Cf2HintMap RentHintMap()
    {
        if (s_mapCount > 0 && s_maps is { } maps)
        {
            Cf2HintMap map = maps[--s_mapCount]!;
            maps[s_mapCount] = null;
            return map;
        }

        return new Cf2HintMap();
    }

    public static void ReturnHintMap(Cf2HintMap map)
    {
        map.Release();

        Cf2HintMap?[] maps = s_maps ??= new Cf2HintMap?[MaxKept];
        if (s_mapCount < maps.Length)
            maps[s_mapCount++] = map;
    }

    /// <summary>A hint mask: one of the thread's, or a new one. The caller initializes it before it reads it.</summary>
    public static Cf2HintMask RentHintMask()
    {
        if (s_maskCount > 0 && s_masks is { } masks)
        {
            Cf2HintMask mask = masks[--s_maskCount]!;
            masks[s_maskCount] = null;
            return mask;
        }

        return new Cf2HintMask();
    }

    public static void ReturnHintMask(Cf2HintMask mask)
    {
        mask.Release();

        Cf2HintMask?[] masks = s_masks ??= new Cf2HintMask?[MaxKept];
        if (s_maskCount < masks.Length)
            masks[s_maskCount++] = mask;
    }

    /// <summary>The most points (and contours) of an outline that a thread keeps room for.</summary>
    private const int MaxRetainedOutlinePoints = 4096;

    [ThreadStatic]
    private static Cf2Outline? s_outline;

    /// <summary>How many array elements this thread's pools hold on to: what the tests look at to see that a font that asked for a lot is not kept.</summary>
    internal static long RetainedElements
    {
        get
        {
            long total = s_outline is { } outline ? (long)outline.X.Length + outline.Contours.Length : 0;

            for (int i = 0; i < s_interpCount; i++)
            {
                Cf2InterpBuffers buffers = s_interps![i]!;
                total += buffers.HStemHints.Capacity + buffers.VStemHints.Capacity + buffers.HintMoves.Capacity + buffers.OpStack.Capacity + buffers.SubrStack.Capacity;
            }

            return total;
        }
    }

    /// <summary>The thread's outline, made ready for a glyph, or a new one. It is given back with <see cref="ReturnOutline"/> when the glyph has been read.</summary>
    public static Cf2Outline RentOutline()
    {
        Cf2Outline outline = s_outline ?? new Cf2Outline();
        s_outline = null; // taken: a load inside a load (there is none) would get one of its own
        outline.Prepare();
        return outline;
    }

    public static void ReturnOutline(Cf2Outline outline)
    {
        outline.Release(MaxRetainedOutlinePoints);
        s_outline = outline;
    }

    [ThreadStatic]
    private static Cf2InterpBuffers?[]? s_interps;

    [ThreadStatic]
    private static int s_interpCount;

    /// <summary>
    /// The buffers a run of the charstring interpreter works in (<see cref="Cf2InterpBuffers"/>), made as they would be for a new run: one of the thread's, or
    /// new ones. A run that meets a <c>seac</c> accent rents another for each component while it holds its own.
    /// </summary>
    public static Cf2InterpBuffers RentInterp(Cf2Error error, int stackSize)
    {
        Cf2InterpBuffers buffers;
        if (s_interpCount > 0 && s_interps is { } interps)
        {
            buffers = interps[--s_interpCount]!;
            interps[s_interpCount] = null;
            buffers.Reset(error, stackSize);
        }
        else
        {
            buffers = new Cf2InterpBuffers(error, stackSize);
        }

        return buffers;
    }

    public static void ReturnInterp(Cf2InterpBuffers buffers)
    {
        buffers.Release();

        Cf2InterpBuffers?[] interps = s_interps ??= new Cf2InterpBuffers?[4];
        if (s_interpCount < interps.Length)
            interps[s_interpCount++] = buffers;
    }
}

/// <summary>
/// What a run of <c>Cf2Interpreter.Interpret</c> allocated for itself: the storage of <c>put</c> and <c>get</c>, the stack of subroutine
/// buffers, the stem hints, the moves of the second pass of the hint adjustment and the operand stack. A thread keeps them (see <see cref="Cf2Pool"/>).
/// </summary>
internal sealed class Cf2InterpBuffers
{
    /// <summary>The number of registers of <c>put</c> and <c>get</c>.</summary>
    public const int StorageSize = 32;

    /// <summary>The most elements of any of the stacks that a thread keeps room for: real charstrings have a few dozen stem hints and a stack of 48, a CFF2 font's a few hundred.</summary>
    private const int MaxRetainedElements = 4096;

    public readonly int[] Storage = new int[StorageSize];
    public readonly Cf2ArrStack<Cf2Buffer> SubrStack;
    public readonly Cf2ArrStack<Cf2StemHint> HStemHints;
    public readonly Cf2ArrStack<Cf2StemHint> VStemHints;
    public readonly Cf2ArrStack<Cf2HintMove> HintMoves;
    public readonly Cf2Stack OpStack;

    public Cf2InterpBuffers(Cf2Error error, int stackSize)
    {
        SubrStack = new Cf2ArrStack<Cf2Buffer>(error);
        HStemHints = new Cf2ArrStack<Cf2StemHint>(error);
        VStemHints = new Cf2ArrStack<Cf2StemHint>(error);
        HintMoves = new Cf2ArrStack<Cf2HintMove>(error);
        OpStack = new Cf2Stack(error, stackSize);
    }

    /// <summary>Makes them what <see cref="Cf2InterpBuffers(Cf2Error, int)"/> makes: nothing stored, nothing pushed.</summary>
    public void Reset(Cf2Error error, int stackSize)
    {
        Array.Clear(Storage);
        SubrStack.Reset(error);
        HStemHints.Reset(error);
        VStemHints.Reset(error);
        HintMoves.Reset(error);
        OpStack.Reset(error, stackSize);
    }

    /// <summary>
    /// Lets go of what a thread must not keep: the font data the subroutine buffers point into, and the room that a hostile font's stem hints or
    /// operand stack made huge.
    /// </summary>
    public void Release()
    {
        for (int i = 0; i < SubrStack.Count; i++)
            SubrStack.GetRef(i).Set([], 0, 0);

        HStemHints.TrimTo(MaxRetainedElements);
        VStemHints.TrimTo(MaxRetainedElements);
        HintMoves.TrimTo(MaxRetainedElements);
        OpStack.TrimTo(MaxRetainedElements);
    }
}