using PeachDrawing.Text.Internal.Fonts.OpenType;
using PeachDrawing.Text.Internal.Fonts.OpenType.Variations;
using PeachDrawing.Text.Internal.Hinting.FreeType;
using PeachDrawing.Text.Outlines;
using System;
using System.Collections.Generic;

namespace PeachDrawing.Text.Internal.Hinting;

/// <summary>What hinting one glyph at one size gave: its grid-fitted outline and advance, or that it could not be hinted.</summary>
internal sealed class HintedGlyphResult
{
    /// <summary>The answer for a glyph that could not be hinted; the caller falls back to the unhinted outline.</summary>
    public static readonly HintedGlyphResult Failed = new(null, 0);

    public HintedGlyphResult(GlyphOutline? outline, double advance)
    {
        Outline = outline;
        Advance = advance;
    }

    public bool Succeeded => Outline is not null;

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
    // The number of sizes (each holds the state of a CVT program run) and of hinted glyphs kept per face.
    private const int MaxSizes = 32;
    private const int MaxGlyphs = 4096;

    private readonly OpenTypeFontface _font;
    private readonly string? _familyName;
    private readonly VariationCoordinates? _variation;
    private readonly Func<int, int> _instanceAdvance;

    private readonly object _faceLock = new();
    private TtFace? _face;
    private bool _faceRead;

    private readonly LruCache<SizeKey, TtSize?> _sizes = new(MaxSizes);
    private readonly LruCache<GlyphKey, HintedGlyphResult> _glyphs = new(MaxGlyphs);

    public HintingEngine(OpenTypeFontface font, string? familyName, VariationCoordinates? variation, Func<int, int> instanceAdvance)
    {
        _font = font;
        _familyName = familyName;
        _variation = variation;
        _instanceAdvance = instanceAdvance;
    }

    /// <summary>Whether the face has TrueType outlines this engine can hint at all.</summary>
    public bool CanHint => GetFace() is not null;

    private TtFace? GetFace()
    {
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
                    _face = null;
                }

                _faceRead = true;
            }

            return _face;
        }
    }

    /// <summary>Hints a glyph at a size, from the cache when it has been asked for before.</summary>
    /// <param name="glyph">The glyph.</param>
    /// <param name="ppem26Dot6">The size in pixels per em, in 1/64.</param>
    /// <param name="mode">The kind of grid-fitting; not <see cref="GridFitting.None"/>.</param>
    public HintedGlyphResult Get(int glyph, int ppem26Dot6, GridFitting mode)
    {
        var sizeKey = new SizeKey(ppem26Dot6, mode);
        var key = new GlyphKey(sizeKey, glyph);

        if (_glyphs.TryGet(key, out HintedGlyphResult? cached))
            return cached!;

        HintedGlyphResult result = Compute(glyph, sizeKey);
        _glyphs.Set(key, result);
        return result;
    }

    private HintedGlyphResult Compute(int glyph, SizeKey sizeKey)
    {
        TtSize? size = GetSize(sizeKey);
        if (size is null)
            return HintedGlyphResult.Failed;

        try
        {
            TtHintedGlyph hinted = TtGlyphLoader.Load(size, glyph);
            return new HintedGlyphResult(ToOutline(hinted, sizeKey.Ppem26Dot6), hinted.Advance / 64.0);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // HintingException for what is wrong with the glyph, and anything else the interpreter is unhappy about with hostile data
            return HintedGlyphResult.Failed;
        }
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
                size = null;
            }
        }

        // a size that fails is remembered as well: its programs would fail again, and slowly
        _sizes.Set(key, size);
        return size;
    }

    private static GlyphOutline ToOutline(TtHintedGlyph hinted, int ppem26Dot6)
    {
        var outline = new GlyphOutline
        {
            IsGridFitted = true,
            PixelsPerEm = ppem26Dot6 / 64.0,
            GridFittedAdvance = hinted.Advance / 64.0,
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

    private readonly record struct SizeKey(int Ppem26Dot6, GridFitting Mode);

    private readonly record struct GlyphKey(SizeKey Size, int Glyph);
}

/// <summary>A thread-safe cache that keeps the most recently used entries, up to a limit.</summary>
internal sealed class LruCache<TKey, TValue>
    where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> _map = [];
    private readonly LinkedList<KeyValuePair<TKey, TValue>> _order = new();
    private readonly object _lock = new();

    public LruCache(int capacity)
    {
        _capacity = capacity;
    }

    public bool TryGet(TKey key, out TValue? value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    public void Set(TKey key, TValue value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _order.Remove(existing);
                _map.Remove(key);
            }

            var node = new LinkedListNode<KeyValuePair<TKey, TValue>>(new KeyValuePair<TKey, TValue>(key, value));
            _order.AddFirst(node);
            _map[key] = node;

            while (_map.Count > _capacity)
            {
                LinkedListNode<KeyValuePair<TKey, TValue>> last = _order.Last!;
                _order.RemoveLast();
                _map.Remove(last.Value.Key);
            }
        }
    }
}
