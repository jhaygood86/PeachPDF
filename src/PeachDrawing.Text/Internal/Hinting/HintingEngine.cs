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
/// Grid-fits the glyphs of one face: owns what it takes to run the font's TrueType instructions at a size (the tables read
/// once, the state each size's programs leave behind, both cached) and hands out the hinted outlines, also cached.
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

    private readonly object _faceLock = new();
    private TtFace? _face;
    private bool _faceRead; // written last, with release semantics, so a reader that sees it true sees _face

    private CffFace? _cffFace;
    private bool _cffFaceRead;

    private readonly LruCache<SizeKey, TtSize?> _sizes = new(MaxSizes);
    private readonly LruCache<SizeKey, CffSize?> _cffSizes = new(MaxSizes);
    private readonly LruCache<GlyphKey, HintedGlyphResult> _glyphs = new(MaxGlyphs, WeightOf, MaxGlyphWeight);

    public HintingEngine(OpenTypeFontface font, string? familyName, VariationCoordinates? variation, Func<int, int> instanceAdvance)
    {
        _font = font;
        _familyName = familyName;
        _variation = variation;
        _instanceAdvance = instanceAdvance;
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
                    _face = TtFace.TryCreate(_font, _familyName, _variation, _instanceAdvance);
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
                    _cffFace = CffFace.TryCreate(_font, _instanceAdvance);
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

    /// <summary>Hints a glyph at a size, from the cache when it has been asked for before.</summary>
    /// <param name="glyph">The glyph.</param>
    /// <param name="ppem26Dot6">The size in pixels per em, in 1/64.</param>
    /// <param name="mode">The kind of grid-fitting; not <see cref="GridFitting.None"/>.</param>
    public HintedGlyphResult Get(int glyph, int ppem26Dot6, GridFitting mode)
    {
        ppem26Dot6 = EffectivePpem(ppem26Dot6);

        // Adobe's CFF engine has no modes: a CFF font is fitted the same way whatever is asked for
        if (GetCffFace() is not null)
            mode = GridFitting.Standard;

        var sizeKey = new SizeKey(ppem26Dot6, mode);
        var key = new GlyphKey(sizeKey, glyph);

        if (_glyphs.TryGet(key, out HintedGlyphResult? cached))
            return cached!;

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
            TtHintedGlyph hinted = TtGlyphLoader.Load(size, glyph);
            return new HintedGlyphResult(ToOutline(hinted, sizeKey.Ppem26Dot6), hinted.Advance / 64.0, hinted.IsHinted);
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
            CffHintedGlyph hinted = CffGlyphLoader.Load(size, glyph);
            return new HintedGlyphResult(ToOutline(hinted, sizeKey.Ppem26Dot6), hinted.Advance / 64.0, true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // HintingException for what is wrong with the glyph, and anything else the engine is unhappy about with hostile data
            NoteFailure(ex);
            return HintedGlyphResult.Failed;
        }
    }

    private CffSize? GetCffSize(SizeKey key)
    {
        if (_cffSizes.TryGet(key, out CffSize? cached))
            return cached;

        CffSize? size = null;
        CffFace? face = GetCffFace();
        if (face is not null)
        {
            try
            {
                size = new CffSize(face, key.Ppem26Dot6);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                NoteFailure(ex);
                size = null;
            }
        }

        _cffSizes.Set(key, size);
        return size;
    }

    private TtSize? GetSize(SizeKey key)
    {
        if (_sizes.TryGet(key, out TtSize? cached))
            return cached;

        TtSize? size = null;
        TtFace? face = GetFace();
        if (face is not null)
        {
            try
            {
                var (version, renderMode) = key.Mode == GridFitting.Monochrome
                    ? (TtInterpreterVersion.V35, TtRenderMode.Mono)
                    : (TtInterpreterVersion.V40, TtRenderMode.Normal);

                size = TtSize.Create(face, key.Ppem26Dot6, version, renderMode);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                NoteFailure(ex);
                size = null;
            }
        }

        // a size that fails is remembered as well: its programs would fail again, and slowly
        _sizes.Set(key, size);
        return size;
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
    private static GlyphOutline ToOutline(CffHintedGlyph hinted, int ppem26Dot6)
    {
        var outline = new GlyphOutline
        {
            IsGridFitted = true,
            PixelsPerEm = ppem26Dot6 / 64.0,
            GridFittedAdvance = hinted.Advance / 64.0,
        };

        int start = 0;
        foreach (int end in hinted.ContourEnds)
        {
            OutlinePoint At(int i) => new(hinted.X[i] / 64.0, hinted.Y[i] / 64.0);

            if ((hinted.Tags[start] & Cf2Outline.TagOn) == 0)
                throw new HintingException("A contour does not start on the curve.");

            var contour = new OutlineContour(At(start));
            int i = start + 1;
            while (i <= end)
            {
                if ((hinted.Tags[i] & Cf2Outline.TagOn) != 0)
                {
                    contour.SegmentList.Add(OutlineSegment.Line(At(i)));
                    i++;
                }
                else if (i + 1 == end && (hinted.Tags[i + 1] & Cf2Outline.TagOn) == 0)
                {
                    // the last curve of a contour ends where it began: its end point was dropped, as FreeType drops a last point that lies
                    // on the first, and it ends at the start of the contour
                    contour.SegmentList.Add(OutlineSegment.Cubic(At(i), At(i + 1), At(start)));
                    i += 2;
                }
                else
                {
                    if (i + 2 > end || (hinted.Tags[i + 1] & Cf2Outline.TagOn) != 0 || (hinted.Tags[i + 2] & Cf2Outline.TagOn) == 0)
                        throw new HintingException("A cubic curve is not made of two control points and an end point.");

                    contour.SegmentList.Add(OutlineSegment.Cubic(At(i), At(i + 1), At(i + 2)));
                    i += 3;
                }
            }

            outline.ContourList.Add(contour);
            start = end + 1;
        }

        return outline;
    }

    private static GlyphOutline ToOutline(TtHintedGlyph hinted, int ppem26Dot6)
    {
        var outline = new GlyphOutline
        {
            IsGridFitted = hinted.IsHinted,
            PixelsPerEm = ppem26Dot6 / 64.0,
            GridFittedAdvance = hinted.IsHinted ? hinted.Advance / 64.0 : null,
        };

        var points = new List<GlyphOutlineDecoder.RawPoint>();
        int start = 0;

        foreach (int end in hinted.ContourEnds)
        {
            points.Clear();
            for (int i = start; i <= end && i < hinted.NPoints; i++)
                points.Add(new GlyphOutlineDecoder.RawPoint(hinted.X[i] / 64.0, hinted.Y[i] / 64.0, (hinted.Tags[i] & FtTag.On) != 0));

            OutlineContour? contour = GlyphOutlineDecoder.BuildContour(points);
            if (contour is not null)
                outline.ContourList.Add(contour);

            start = end + 1;
        }

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

    private readonly record struct SizeKey(int Ppem26Dot6, GridFitting Mode);

    private readonly record struct GlyphKey(SizeKey Size, int Glyph);
}
