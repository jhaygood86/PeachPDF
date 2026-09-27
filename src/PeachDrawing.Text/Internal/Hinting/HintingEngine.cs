using PeachDrawing.Text.Internal.Fonts.OpenType;
using PeachDrawing.Text.Internal.Fonts.OpenType.Variations;
using PeachDrawing.Text.Internal.Hinting.FreeType;
using PeachDrawing.Text.Outlines;
using System;
using System.Collections.Generic;
using System.Threading;

namespace PeachDrawing.Text.Internal.Hinting;

/// <summary>What hinting one glyph at one size gave: its grid-fitted outline and advance, or that it could not be hinted.</summary>
internal sealed class HintedGlyphResult
{
    /// <summary>The answer for a glyph that could not be hinted; the caller falls back to the unhinted outline.</summary>
    public static readonly HintedGlyphResult Failed = new(null, 0, false);

    public HintedGlyphResult(GlyphOutline? outline, double advance, bool isHinted)
    {
        Outline = outline;
        Advance = advance;
        IsHinted = isHinted;
    }

    /// <summary>Whether the glyph was loaded at all.</summary>
    public bool Succeeded => Outline is not null;

    /// <summary>
    /// Whether the outline was grid-fitted. It is not when the font's own programs turned hinting off at this size (the CVT program can), in
    /// which case the outline is only scaled, as FreeType does.
    /// </summary>
    public bool IsHinted { get; }

    /// <summary>The outline in pixels; empty for a glyph with no ink.</summary>
    public GlyphOutline? Outline { get; }

    /// <summary>The grid-fitted advance in pixels.</summary>
    public double Advance { get; }
}

/// <summary>
/// Grid-fits the glyphs of one face: owns what it takes to run the font's TrueType instructions, or apply the hints of its CFF charstrings, at a size (the tables
/// read once, the state each size's programs leave behind, both cached) and hands out the hinted outlines, also cached.
/// </summary>
/// <remarks>
/// Everything cached is immutable, so one engine serves any number of threads. Fonts are untrusted input: a font or a
/// glyph that cannot be hinted for any reason answers <see cref="HintedGlyphResult.Failed"/> and never throws.
/// </remarks>
internal sealed class HintingEngine
{
    // The number of sizes (each holds the state of a CVT program run, which a font may declare to be megabytes) and of hinted glyphs kept
    // per face; the glyphs are also limited by their total weight (a glyph weighs what its outline has of contours and segments), so a
    // face of a few huge glyphs cannot fill the memory with them.
    private const int MaxSizes = 16;
    private const int MaxGlyphs = 4096;
    private const long MaxGlyphWeight = 250_000;

    private readonly OpenTypeFontface _font;
    private readonly string? _familyName;
    private readonly VariationCoordinates? _variation;
    private readonly Func<int, int> _instanceAdvance;

    // What tells the location this engine fits at from another in the caches (null at the defaults). An engine serves one location (a typeface at
    // another has an engine of its own), so the caches could do without it; it is in their keys so that what one location gave is never taken
    // for another's if an engine is ever shared.
    private readonly string? _variationKey;

    private readonly object _faceLock = new();
    private TtFace? _face;
    private bool _faceRead; // written last, with release semantics, so a reader that sees it true sees _face

    private CffFace? _cffFace;
    private bool _cffFaceRead;

    private TtGasp? _gasp;
    private bool _gaspRead;

    private TtBlend? _blend;
    private bool _blendRead;

    private readonly LruCache<SizeKey, TtSize?> _sizes = new(MaxSizes);
    private readonly LruCache<SizeKey, CffSize?> _cffSizes = new(MaxSizes);
    private readonly LruCache<GlyphKey, HintedGlyphResult> _glyphs;
    private readonly Func<SizeKey, TtSize?> _createSize;
    private readonly Func<SizeKey, CffSize?> _createCffSize;

    // Glyphs that were asked for lately, where any number of threads can find them without taking a lock. Every glyph cache hit takes the one lock of the
    // cache and moves the entry to the front of its list, which is a write to memory that every thread shares: with several threads asking for the same few
    // glyphs (text is mostly a few hundred of them) the threads spend their time waiting for each other and for the cache lines, tens of times what a hit costs
    // alone. An entry is immutable and so is what it holds; the array is direct-mapped, an entry that goes out of _glyphs is taken out of it too, and a thread
    // that finds nothing here asks _glyphs and puts what it finds here. The price is that a hit here does not move the entry to the front of _glyphs, so an
    // entry that is asked for over and over may reach the end of it and be hinted again after all: that costs one more hinting of the glyph, never a wrong answer.
    private const int FrontBits = 11;
    private FrontEntry?[]? _front; // made by the first glyph that is found in _glyphs, so an engine that only ever hints each glyph once does not have it

    public HintingEngine(OpenTypeFontface font, string? familyName, VariationCoordinates? variation, Func<int, int> instanceAdvance)
    {
        _font = font;
        _familyName = familyName;
        _variation = variation;
        _instanceAdvance = instanceAdvance;
        _variationKey = variation?.Key;
        _glyphs = new(MaxGlyphs, WeightOf, MaxGlyphWeight, (key, _) => Forget(key));
        _createSize = CreateSize;
        _createCffSize = CreateCffSize;
    }

    /// <summary>
    /// The normalized coordinates, in 16.16, FreeType has for a variable font at the location of this engine: one for each axis of the font (zero at the defaults, which is how it keeps
    /// a font that has an <c>fvar</c> table even when nothing was set), or null for a font that has none. They are made from the design coordinates as FreeType makes them
    /// (<c>ft_var_to_normalized</c>, with the <c>avar</c> table in 16.16), not by rounding the package's own 2.14 coordinates.
    /// </summary>
    private int[]? NormalizedCoordinates() => TtVarTables.NormalizedCoordinates(_font, _variation);

    private static int FrontSlot(in GlyphKey key) =>
        (int)(((uint)key.Glyph * 0x9E3779B1u + (uint)key.Size.Ppem26Dot6 * 0x85EBCA6Bu + (uint)key.Size.Mode * 0xC2B2AE35u + (key.Size.StemDarkening ? 0x27D4EB2Fu : 0u)) >> (32 - FrontBits));

    private void Forget(in GlyphKey key)
    {
        if (Volatile.Read(ref _front) is not { } front)
            return;

        ref FrontEntry? slot = ref front[FrontSlot(key)];
        FrontEntry? entry = Volatile.Read(ref slot);
        if (entry is not null && entry.Key.Equals(key))
            Interlocked.CompareExchange(ref slot, null, entry);
    }

    /// <summary>
    /// Whether the face has TrueType outlines and TrueType instructions, or CFF outlines (which carry their own hints), which is what
    /// this engine can hint.
    /// </summary>
    public bool CanHint => GetFace() is { HasInstructions: true } || GetCffFace() is not null;

    private TtFace? GetFace()
    {
        if (Volatile.Read(ref _faceRead))
            return _face;

        lock (_faceLock)
        {
            if (!_faceRead)
            {
                try
                {
                    _face = TtFace.TryCreate(_font, _familyName, _variation is null ? null : NormalizedCoordinates());
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    NoteFailure(ex);
                    _face = null;
                }

                Volatile.Write(ref _faceRead, true);
            }

            return _face;
        }
    }

    private CffFace? GetCffFace()
    {
        // a font with TrueType outlines is not also a CFF font
        if (GetFace() is not null)
            return null;

        if (Volatile.Read(ref _cffFaceRead))
            return _cffFace;

        lock (_faceLock)
        {
            if (!_cffFaceRead)
            {
                try
                {
                    _cffFace = CffFace.TryCreate(_font, CffAdvance(), NormalizedCoordinates());
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    NoteFailure(ex);
                    _cffFace = null;
                }

                Volatile.Write(ref _cffFaceRead, true);
            }

            return _cffFace;
        }
    }

    /// <summary>The location of a variable font as FreeType keeps it (which is what <c>MVAR</c> moves the <c>gasp</c> ranges of), or null for a font that is not variable.</summary>
    private TtBlend? GetBlend()
    {
        if (Volatile.Read(ref _blendRead))
            return _blend;

        lock (_faceLock)
        {
            if (!_blendRead)
            {
                try
                {
                    if (GetFace() is { } face)
                        _blend = face.Blend; // a font with TrueType outlines has it in its face
                    else if (_variation is not null && TtVarTables.For(_font) is { } tables)
                        _blend = TtBlend.TryCreate(tables, NormalizedCoordinates() ?? [], isCff2: true); // (a face nothing was set on has none)
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    NoteFailure(ex);
                    _blend = null;
                }

                Volatile.Write(ref _blendRead, true);
            }

            return _blend;
        }
    }

    /// <summary>
    /// The advance of a glyph of a font with CFF outlines, in font units: its <c>hmtx</c> entry, and at a location of a variable font what <c>HVAR</c> makes of it, in the 16.16
    /// arithmetic FreeType uses (the package's own is in floating point), as <c>cff_slot_load</c> reads it.
    /// </summary>
    private Func<int, int> CffAdvance()
    {
        if (GetBlend() is not { DoBlend: true } blend || _font.hhea is null || _font.hmtx is null)
            return _instanceAdvance;

        return glyph =>
        {
            // a glyph past the long metrics shares the last one's advance; a font with none has an advance of 0
            int index = Math.Min(glyph, _font.hhea.numberOfHMetrics - 1);
            return blend.AdjustAdvance(false, glyph, index < 0 ? 0 : _font.hmtx.Metrics[index].advanceWidth);
        };
    }

    private TtGasp? GetGasp()
    {
        if (Volatile.Read(ref _gaspRead))
            return _gasp;

        lock (_faceLock)
        {
            if (!_gaspRead)
            {
                // reading the table checks every offset and length against the table, and the location's own tables are read inside GetBlend, which catches what a hostile font makes them throw
                _gasp = TtGasp.TryRead(_font)?.AtLocation(GetBlend());
                Volatile.Write(ref _gaspRead, true);
            }

            return _gasp;
        }
    }

    /// <summary>
    /// Hints a glyph at a size, from the cache when it has been asked for before. A size at which the font's <c>gasp</c> table does not ask
    /// for grid-fitting is not hinted: the answer is <see cref="HintedGlyphResult.Failed"/>, and the caller keeps the scaled design.
    /// </summary>
    /// <param name="glyph">The glyph.</param>
    /// <param name="ppem26Dot6">The size in pixels per em, in 1/64.</param>
    /// <param name="mode">The kind of grid-fitting; not <see cref="GridFitting.None"/>.</param>
    /// <param name="stemDarkening">Whether the stem darkening of Adobe's CFF engine is on; it means nothing for a TrueType font.</param>
    public HintedGlyphResult Get(int glyph, int ppem26Dot6, GridFitting mode, bool stemDarkening = false)
    {
        ppem26Dot6 = EffectivePpem(ppem26Dot6);

        // the font's own word on which sizes want fitting (FT_GASP_DO_GRIDFIT: "if this bit is not set, no hinting gets applied")
        if (GetGasp() is { } gasp && !gasp.AllowsGridFit((int)(((long)ppem26Dot6 + 32) >> 6)))
            return HintedGlyphResult.Failed;

        if (GetCffFace() is not null)
        {
            // Adobe's CFF engine has no modes: a CFF font is fitted the same way whatever is asked for
            mode = GridFitting.Standard;
        }
        else
        {
            // and only it darkens: a TrueType font's entries are shared between the two answers
            stemDarkening = false;
        }

        var sizeKey = new SizeKey(ppem26Dot6, mode, stemDarkening, _variationKey);
        var key = new GlyphKey(sizeKey, glyph);

        int slot = FrontSlot(key);
        FrontEntry?[]? front = Volatile.Read(ref _front);
        if (front is not null && Volatile.Read(ref front[slot]) is { } entry && entry.Key.Equals(key))
            return entry.Result;

        if (_glyphs.TryGet(key, out HintedGlyphResult? cached))
        {
            if (front is null)
            {
                // two threads that make it at the same moment share whichever was stored first
                front = new FrontEntry?[1 << FrontBits];
                front = Interlocked.CompareExchange(ref _front, front, null) ?? front;
            }

            Volatile.Write(ref front[slot], new FrontEntry(key, cached!));
            return cached!;
        }

        HintedGlyphResult result = Compute(glyph, sizeKey);
        _glyphs.Set(key, result);
        return result;
    }

    /// <summary>
    /// The size a face is actually scaled to. A TrueType font whose <c>head</c> flags ask for integer ppems (nearly all do) is scaled to the
    /// nearest whole number of pixels per em, as FreeType does: 11.4 ppem is 11. Keying the caches by the size that counts means that every
    /// fractional size of such a font shares one entry, instead of each running <c>prep</c> again and pushing another out of the cache.
    /// </summary>
    private int EffectivePpem(int ppem26Dot6)
    {
        TtFace? face = GetFace();
        if (face is null || (face.HeadFlags & 8) == 0)
            return ppem26Dot6;

        int rounded = (int)(((long)ppem26Dot6 + 32) >> 6) << 6;
        return rounded > 0 ? rounded : ppem26Dot6;
    }

    private HintedGlyphResult Compute(int glyph, SizeKey sizeKey)
    {
        if (GetCffFace() is not null)
            return ComputeCff(glyph, sizeKey);

        TtSize? size = GetSize(sizeKey);
        if (size is null)
            return HintedGlyphResult.Failed;

        try
        {
            // the outline is made from the loader's own arrays, without a copy of them first
            return TtGlyphLoader.Load(size, glyph, sizeKey.Ppem26Dot6, s_readTrueTypeGlyph);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // HintingException for what is wrong with the glyph, and anything else the interpreter is unhappy about with hostile data
            NoteFailure(ex);
            return HintedGlyphResult.Failed;
        }
    }

    private HintedGlyphResult ComputeCff(int glyph, SizeKey sizeKey)
    {
        CffSize? size = GetCffSize(sizeKey);
        if (size is null)
            return HintedGlyphResult.Failed;

        try
        {
            // the outline is made from the thread's own arrays, without a copy of them first
            return CffGlyphLoader.Load(size, glyph, sizeKey.Ppem26Dot6, s_readCffGlyph);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // HintingException for what is wrong with the glyph, and anything else the engine is unhappy about with hostile data
            NoteFailure(ex);
            return HintedGlyphResult.Failed;
        }
    }

    // A size is made once however many threads want it at the same moment (the cache's GetOrAdd): making one runs the font's fpgm and prep programs.
    private CffSize? GetCffSize(SizeKey key) => _cffSizes.GetOrAdd(key, _createCffSize);

    private CffSize? CreateCffSize(SizeKey key)
    {
        CffFace? face = GetCffFace();
        if (face is null)
            return null;

        try
        {
            return new CffSize(face, key.Ppem26Dot6, key.StemDarkening);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            NoteFailure(ex);
            return null;
        }
    }

    private TtSize? GetSize(SizeKey key) => _sizes.GetOrAdd(key, _createSize);

    // a size that fails is remembered as well: its programs would fail again, and slowly
    private TtSize? CreateSize(SizeKey key)
    {
        TtFace? face = GetFace();
        if (face is null)
            return null;

        try
        {
            var (version, renderMode) = key.Mode == GridFitting.Monochrome
                ? (TtInterpreterVersion.V35, TtRenderMode.Mono)
                : (TtInterpreterVersion.V40, TtRenderMode.Normal);

            return TtSize.Create(face, key.Ppem26Dot6, version, renderMode);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            NoteFailure(ex);
            return null;
        }
    }

    private static int WeightOf(HintedGlyphResult result)
    {
        int weight = 1;
        if (result.Outline is { } outline)
        {
            foreach (OutlineContour contour in outline.ContourList)
                weight += 2 + contour.SegmentList.Count;
        }

        return weight;
    }

    // The outline of a CFF glyph: each contour starts at an on-curve point and goes on by lines (an on-curve point) and cubic curves (two
    // control points and an on-curve point).
    private static readonly CffGlyphReader<int, HintedGlyphResult> s_readCffGlyph =
        static (in CffGlyphView hinted, int ppem26Dot6) => new HintedGlyphResult(ToOutline(hinted, ppem26Dot6), hinted.Advance / 64.0, true);

    private static GlyphOutline ToOutline(in CffGlyphView hinted, int ppem26Dot6)
    {
        var outline = new GlyphOutline
        {
            IsGridFitted = true,
            PixelsPerEm = ppem26Dot6 / 64.0,
            GridFittedAdvance = hinted.Advance / 64.0,
        };

        outline.ContourList.Capacity = hinted.ContourEnds.Length;

        ReadOnlySpan<int> xs = hinted.X;
        ReadOnlySpan<int> ys = hinted.Y;
        ReadOnlySpan<byte> tags = hinted.Tags;

        int start = 0;
        foreach (int end in hinted.ContourEnds)
        {
            if ((tags[start] & Cf2Outline.TagOn) == 0)
                throw new HintingException("A contour does not start on the curve.");

            // one segment for each line and each curve: counted first, so that the list is made as big as it is going to be, not grown
            var contour = new OutlineContour(At(xs, ys, start), CountSegments(tags, start, end));
            int i = start + 1;
            while (i <= end)
            {
                if ((tags[i] & Cf2Outline.TagOn) != 0)
                {
                    contour.SegmentList.Add(OutlineSegment.Line(At(xs, ys, i)));
                    i++;
                }
                else if (i + 1 == end && (tags[i + 1] & Cf2Outline.TagOn) == 0)
                {
                    // the last curve of a contour ends where it began: its end point was dropped, as FreeType drops a last point that lies
                    // on the first, and it ends at the start of the contour
                    contour.SegmentList.Add(OutlineSegment.Cubic(At(xs, ys, i), At(xs, ys, i + 1), At(xs, ys, start)));
                    i += 2;
                }
                else
                {
                    if (i + 2 > end || (tags[i + 1] & Cf2Outline.TagOn) != 0 || (tags[i + 2] & Cf2Outline.TagOn) == 0)
                        throw new HintingException("A cubic curve is not made of two control points and an end point.");

                    contour.SegmentList.Add(OutlineSegment.Cubic(At(xs, ys, i), At(xs, ys, i + 1), At(xs, ys, i + 2)));
                    i += 3;
                }
            }

            outline.ContourList.Add(contour);
            start = end + 1;
        }

        return outline;
    }

    private static OutlinePoint At(ReadOnlySpan<int> xs, ReadOnlySpan<int> ys, int i) => new(xs[i] / 64.0, ys[i] / 64.0);

    // How many segments the loop of ToOutline makes of a contour: it walks the tags as that loop does (a malformed contour is not this method's to
    // refuse; the loop throws for it).
    private static int CountSegments(ReadOnlySpan<byte> tags, int start, int end)
    {
        int segments = 0;
        int i = start + 1;

        while (i <= end)
        {
            segments++;

            if ((tags[i] & Cf2Outline.TagOn) != 0)
                i++;
            else if (i + 1 == end && (tags[i + 1] & Cf2Outline.TagOn) == 0)
                i += 2;
            else
                i += 3;
        }

        return segments;
    }

    private static readonly TtGlyphReader<int, HintedGlyphResult> s_readTrueTypeGlyph =
        static (in TtGlyphView hinted, int ppem26Dot6) => new HintedGlyphResult(ToOutline(hinted, ppem26Dot6), hinted.Advance / 64.0, hinted.IsHinted);

    // The points of the contour being made; a thread keeps one list from glyph to glyph. Nothing of it goes into the outline (BuildContour copies).
    [ThreadStatic]
    private static List<GlyphOutlineDecoder.RawPoint>? s_contourPoints;

    // The most points the list is kept at: a contour of a real glyph has a few hundred.
    private const int MaxRetainedContourPoints = 4096;

    private static GlyphOutline ToOutline(in TtGlyphView hinted, int ppem26Dot6)
    {
        var outline = new GlyphOutline
        {
            IsGridFitted = hinted.IsHinted,
            PixelsPerEm = ppem26Dot6 / 64.0,
            GridFittedAdvance = hinted.IsHinted ? hinted.Advance / 64.0 : null,
        };

        outline.ContourList.Capacity = hinted.ContourEnds.Length;

        List<GlyphOutlineDecoder.RawPoint> points = s_contourPoints ?? new List<GlyphOutlineDecoder.RawPoint>(64);
        s_contourPoints = null; // taken: a contour that throws leaves nothing half-used behind

        ReadOnlySpan<int> xs = hinted.X;
        ReadOnlySpan<int> ys = hinted.Y;
        ReadOnlySpan<byte> tags = hinted.Tags;
        int start = 0;

        foreach (int end in hinted.ContourEnds)
        {
            points.Clear();
            for (int i = start; i <= end && i < xs.Length; i++)
                points.Add(new GlyphOutlineDecoder.RawPoint(xs[i] / 64.0, ys[i] / 64.0, (tags[i] & FtTag.On) != 0));

            OutlineContour? contour = GlyphOutlineDecoder.BuildContour(points);
            if (contour is not null)
                outline.ContourList.Add(contour);

            start = end + 1;
        }

        if (points.Capacity <= MaxRetainedContourPoints)
            s_contourPoints = points;

        return outline;
    }

    private static long s_unexpectedFailures;

    /// <summary>
    /// How many times hinting failed with anything but a <see cref="HintingException"/> (a font's failure to be hinted, which is expected of
    /// hostile data): an index out of range or the like in the interpreter, which the engine also answers by not hinting but which is a bug.
    /// The tests watch it.
    /// </summary>
    internal static long UnexpectedFailures => Interlocked.Read(ref s_unexpectedFailures);

    private static void NoteFailure(Exception ex)
    {
        if (ex is not HintingException)
            Interlocked.Increment(ref s_unexpectedFailures);
    }

    private readonly record struct SizeKey(int Ppem26Dot6, GridFitting Mode, bool StemDarkening, string? Variation);

    private readonly record struct GlyphKey(SizeKey Size, int Glyph);

    /// <summary>A glyph and what hinting it gave, in <see cref="_front"/>.</summary>
    private sealed class FrontEntry(GlyphKey key, HintedGlyphResult result)
    {
        public readonly GlyphKey Key = key;
        public readonly HintedGlyphResult Result = result;
    }
}
