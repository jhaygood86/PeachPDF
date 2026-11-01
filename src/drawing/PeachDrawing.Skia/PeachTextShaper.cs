using PeachDrawing.Text;
using PeachDrawing.Text.Shaping;
using SkiaSharp;
using System;
using System.Collections.Generic;

namespace PeachDrawing.Skia;

/// <summary>
/// An <see cref="ISkiaTextShaper"/> that shapes with the <c>PeachDrawing.Text</c> engine: OpenType substitution and positioning (ligatures,
/// kerning, mark attachment, contextual lookups), Arabic, Syriac, Indic and Khmer shaping. Pass it to
/// <see cref="SkiaRenderContext"/> to give <see cref="SkiaTextEngine.Native"/> text real shaping with no shaping library beyond this package.
/// </summary>
/// <remarks>
/// <para>
/// The engine reads the font file Skia has loaded (the same bytes, so glyph ids mean the same in both) and shapes with it; Skia draws the
/// glyphs. A typeface whose file cannot be read back (a platform font Skia will not hand out a stream for) is shaped by the basic shaper
/// instead, so text is never lost. Weight and slant are taken from the typeface itself.
/// </para>
/// <para>
/// A shaper holds the fonts it has read for as long as it lives, and is used from one thread at a time by the context. It owns no native
/// resource, so there is nothing to dispose.
/// </para>
/// </remarks>
public sealed class PeachTextShaper : ISkiaTextShaper
{
    private readonly FontSet _fonts = new();
    private readonly Dictionary<SKTypeface, Typeface?> _typefaces = [];

    /// <inheritdoc />
    public SkiaShapedRun Shape(string text, SKFont font)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(font);

        if (TypefaceOf(font.Typeface) is not { } typeface || typeface.Metrics.UnitsPerEm == 0)
            return BasicSkiaShaper.Instance.Shape(text, font);

        var glyphs = Shaper.Shape(typeface, text, ShapeSettings.Default).Glyphs;
        var scale = font.Size / typeface.Metrics.UnitsPerEm;
        var ids = new ushort[glyphs.Count];
        var xs = new float[glyphs.Count];
        var ys = new float[glyphs.Count];
        var clusters = new uint[glyphs.Count];
        var pen = 0.0;
        for (var i = 0; i < ids.Length; i++)
        {
            var glyph = glyphs[i];
            ids[i] = (ushort)glyph.GlyphIndex;
            xs[i] = (float)(pen + glyph.XOffset * scale);
            ys[i] = (float)(-glyph.YOffset * scale);
            clusters[i] = (uint)glyph.ClusterStart;
            pen += (typeface.GetAdvance((ushort)glyph.GlyphIndex) + glyph.XAdvanceDelta) * scale;
        }

        return new SkiaShapedRun(ids, xs, ys, clusters, (float)pen);
    }

    private Typeface? TypefaceOf(SKTypeface skia)
    {
        if (_typefaces.TryGetValue(skia, out var known))
            return known;

        Typeface? typeface = null;
        try
        {
            using var stream = skia.OpenStream();
            if (stream is not null)
            {
                var bytes = new byte[stream.Length];
                stream.Read(bytes, bytes.Length);
                var family = _fonts.AddData(bytes);
                if (family.TryMatch(TypefaceQuery.From(skia.FontWeight, 100, skia.FontSlant != SKFontStyleSlant.Upright, null, null, null), out var match))
                    typeface = match.Typeface;
            }
        }
        catch (Exception)
        {
            // A file the engine cannot read: the basic shaper takes the run.
        }

        return _typefaces[skia] = typeface;
    }
}
