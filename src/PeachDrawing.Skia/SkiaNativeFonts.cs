using PeachDrawing.Core;
using PeachDrawing.Text;
using SkiaSharp;
using System;
using System.Collections.Generic;

namespace PeachDrawing.Skia;

/// <summary>
/// A Skia typeface the native text engine resolves a family to, and the shaper that shapes text in it. The shaper is shared by every face of a
/// context and is called from one thread at a time, so shaping takes the shaper's lock.
/// </summary>
internal sealed class NativeFace(SKTypeface typeface, bool ownsTypeface, ISkiaTextShaper shaper) : IDisposable
{
    public SKTypeface Typeface { get; } = typeface;

    public SkiaShapedRun Shape(string text, SKFont font)
    {
        lock (shaper)
            return shaper.Shape(text, font);
    }

    public void Dispose()
    {
        if (ownsTypeface)
            Typeface.Dispose();
    }
}

/// <summary>
/// The faces the native text engine resolves families to: the fonts a document added with <c>@font-face</c>, then the platform's font
/// manager. Everything it hands out is released when the context is disposed.
/// </summary>
internal sealed class NativeFonts(ISkiaTextShaper shaper) : IDisposable
{
    private readonly record struct Registered(NativeFace Face, double Weight, bool Italic);

    private readonly object _gate = new();
    private readonly Dictionary<string, List<Registered>> _registered = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string, int, bool, int), NativeFace> _matched = [];
    private readonly Dictionary<SKTypeface, NativeFace> _byTypeface = [];
    private readonly List<NativeFace> _owned = [];
    private NativeFace? _default;

    public static IEnumerable<string> InstalledFamilyNames => SKFontManager.Default.FontFamilies;

    /// <summary>Registers a font file under <paramref name="family"/>. Returns false for a format Skia cannot load (WOFF, for one).</summary>
    public bool Add(byte[] data, string family, FontFaceDescriptors descriptors)
    {
        var typeface = SKTypeface.FromData(SKData.CreateCopy(data));
        if (typeface is null)
            return false;

        var face = new NativeFace(typeface, true, shaper);
        var weight = descriptors.Weight is { } range ? (range.Minimum + range.Maximum) / 2 : typeface.FontWeight;
        var italic = descriptors.IsItalic ?? typeface.FontSlant != SKFontStyleSlant.Upright;
        lock (_gate)
        {
            _owned.Add(face);
            if (!_registered.TryGetValue(family, out var list))
                _registered[family] = list = [];

            list.Add(new Registered(face, weight, italic));

            // A family looked up before this face arrived must see it now.
            _matched.Clear();
        }

        return true;
    }

    /// <summary>The face for <paramref name="family"/> at the weight, width (percent) and slant asked for; the platform default when nothing matches.</summary>
    public NativeFace Resolve(string family, double weight, bool italic, double stretch) => Find(family, weight, italic, stretch) ?? Default();

    /// <summary>Like <see cref="Resolve"/>, but <see langword="null"/> when the family is unknown.</summary>
    public NativeFace? Find(string family, double weight, bool italic, double stretch)
    {
        var key = (family, (int)Math.Round(weight), italic, (int)Math.Round(stretch));
        lock (_gate)
        {
            if (_matched.TryGetValue(key, out var cached))
                return cached;

            NativeFace? best = null;
            if (_registered.TryGetValue(family, out var list))
            {
                var bestDistance = double.MaxValue;
                foreach (var candidate in list)
                {
                    var distance = Math.Abs(candidate.Weight - weight) + (candidate.Italic == italic ? 0 : 1000);
                    if (distance < bestDistance)
                    {
                        best = candidate.Face;
                        bestDistance = distance;
                    }
                }
            }

            if (best is null)
            {
                var style = new SKFontStyle((SKFontStyleWeight)(int)Math.Clamp(weight, 1, 1000), WidthClass(stretch), italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
                var typeface = SKFontManager.Default.MatchFamily(family, style);
                if (typeface is not null)
                    best = Wrap(typeface);
            }

            if (best is not null)
                _matched[key] = best;

            return best;
        }
    }

    /// <summary>A face the platform picks for <paramref name="codepoint"/>, or <see langword="null"/> when none covers it.</summary>
    public NativeFace? Fallback(int codepoint, double weight, bool italic, double stretch)
    {
        var style = new SKFontStyle((SKFontStyleWeight)(int)Math.Clamp(weight, 1, 1000), WidthClass(stretch), italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
        var typeface = SKFontManager.Default.MatchCharacter(null, style, null, codepoint);
        if (typeface is null)
            return null;

        lock (_gate)
            return Wrap(typeface);
    }

    private NativeFace Default()
    {
        lock (_gate)
            return _default ??= Track(new NativeFace(SKTypeface.Default, false, shaper));
    }

    // One face per typeface, so a family matched twice shares a shaper; the typeface the manager returned is ours to dispose.
    private NativeFace Wrap(SKTypeface typeface)
    {
        if (_byTypeface.TryGetValue(typeface, out var existing))
        {
            // SkiaSharp hands back the one managed wrapper for a native typeface, which must not be disposed out from under the face.
            if (!ReferenceEquals(existing.Typeface, typeface))
                typeface.Dispose();

            return existing;
        }

        var face = Track(new NativeFace(typeface, true, shaper));
        _byTypeface[typeface] = face;
        return face;
    }

    private NativeFace Track(NativeFace face)
    {
        _owned.Add(face);
        return face;
    }

    private static SKFontStyleWidth WidthClass(double percent) => percent switch
    {
        < 56.25 => SKFontStyleWidth.UltraCondensed,
        < 68.75 => SKFontStyleWidth.ExtraCondensed,
        < 81.25 => SKFontStyleWidth.Condensed,
        < 93.75 => SKFontStyleWidth.SemiCondensed,
        < 106.25 => SKFontStyleWidth.Normal,
        < 118.75 => SKFontStyleWidth.SemiExpanded,
        < 137.5 => SKFontStyleWidth.Expanded,
        < 175 => SKFontStyleWidth.ExtraExpanded,
        _ => SKFontStyleWidth.UltraExpanded,
    };

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var face in _owned)
                face.Dispose();

            _owned.Clear();
            _registered.Clear();
            _matched.Clear();
            _byTypeface.Clear();
            _default = null;
        }
    }
}
