using PeachDrawing.Core;
using PeachDrawing.Core.ColorGlyphs;
using PeachDrawing.Text;
using PeachDrawing.Text.Export;
using PeachDrawing.Text.Outlines;
using PeachDrawing.Text.Shaping;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace PeachDrawing.Svg;

public sealed partial class SvgCanvas
{
    // ---- measuring ----------------------------------------------------------------------------------------

    // Measurement is the layout engine's own routine, so what is drawn can never disagree with layout.
    /// <inheritdoc/>
    public override Size MeasureString(string str, Font font, ShapeSettings? features = null)
    {
        var size = TextMeasurement.Measure(str, font.Typeface!, font.Size, font.SyntheticStyle, features ?? ShapeSettings.Default);
        return new Size(size.Width * _ppp, size.Height * _ppp);
    }

    /// <inheritdoc/>
    public override int CountShapedGlyphs(string str, Font font, ShapeSettings? features = null) =>
        Shaper.Shape(font.Typeface!, str, features ?? ShapeSettings.Default).Glyphs.Count;

    /// <inheritdoc/>
    public override void MeasureString(string str, Font font, double maxWidth, out int charFit, out double charFitWidth)
    {
        // The longest prefix, cut between whole characters, that fits.
        charFit = 0;
        charFitWidth = 0;
        for (var length = 1; length <= str.Length; length++)
        {
            if (char.IsLowSurrogate(str[length - 1]) && length > 1 && char.IsHighSurrogate(str[length - 2]))
                continue;
            if (char.IsHighSurrogate(str[length - 1]) && length < str.Length)
                continue;

            var width = MeasureString(str[..length], font).Width;
            if (width > maxWidth)
                break;

            charFit = length;
            charFitWidth = width;
        }
    }

    /// <inheritdoc/>
    public override GraphicsPath? GetTextOutline(string str, Font font, PaintPoint baselineOrigin, double letterSpacing = 0, ShapeSettings? features = null) =>
        TextOutlineBuilder.Build(GetGraphicsPath(), font, _ppp, str, baselineOrigin, letterSpacing, features ?? ShapeSettings.Default);

    /// <inheritdoc/>
    public override IReadOnlyList<InkSpan>? GetInkCrossings(string str, Font font, PaintPoint origin, double bandTop, double bandBottom,
        double letterSpacing = 0, ShapeSettings? features = null)
    {
        var typeface = font.Typeface!;
        var unitsPerEm = typeface.Metrics.UnitsPerEm;
        if (unitsPerEm == 0 || bandBottom <= bandTop)
            return null;

        // The baseline DrawString paints this run on, and the size of a design unit, both in the canvas's own units.
        var baselineY = origin.Y + font.Size * typeface.Metrics.CellAscent / unitsPerEm * _ppp;
        var scale = font.Size * _ppp / unitsPerEm;

        if (InkCrossings.Measure(typeface, str, scale, bandTop - baselineY, bandBottom - baselineY, letterSpacing, features ?? ShapeSettings.Default) is not { } relative)
            return null;

        var spans = new InkSpan[relative.Count];
        for (var i = 0; i < spans.Length; i++)
            spans[i] = new InkSpan(relative[i].Start + origin.X, relative[i].End + origin.X);

        return spans;
    }

    // ---- drawing ------------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public override void DrawString(string str, Font font, PaintColor color, PaintPoint point, Size size, double letterSpacing = 0,
        FontPalette? fontPalette = null, ShapeSettings? features = null)
    {
        var typeface = font.Typeface!;
        var unitsPerEm = typeface.Metrics.UnitsPerEm;
        if (unitsPerEm == 0 || str.Length == 0)
            return;

        // Where the PDF renderer puts the baseline: the run's top-left plus the cell ascent (here in layout units).
        var lineSpace = typeface.Metrics.LineSpacing * font.Size / unitsPerEm * _ppp;
        var baselineY = point.Y + lineSpace * typeface.Metrics.CellAscent / typeface.Metrics.LineSpacing;
        DrawRun(str, font, color, point.X, baselineY, letterSpacing, fontPalette, features ?? ShapeSettings.Default);
    }

    /// <inheritdoc/>
    public override void DrawGlyphs(IReadOnlyList<GlyphPlacement> glyphs, Font font, PaintColor color)
    {
        var typeface = font.Typeface!;
        var unitsPerEm = typeface.Metrics.UnitsPerEm;
        if (InvisibleText || unitsPerEm == 0)
            return;

        var scale = font.Size / unitsPerEm * _ppp;
        var uses = new List<GlyphUse>(glyphs.Count);
        foreach (var placement in glyphs)
        {
            if (!TryDrawSpecialGlyph(typeface, placement.GlyphIndex, font, color, null, placement.X, placement.Y)
                && typeface.TryGetOutline((ushort)placement.GlyphIndex, out var outline))
            {
                uses.Add(new GlyphUse(GlyphId(typeface, placement.GlyphIndex, outline, scale, 0), placement.X, placement.Y));
            }
        }

        EmitGlyphs(uses, font, color);
    }

    private void DrawRun(string str, Font font, PaintColor color, double originX, double baselineY, double letterSpacing,
        FontPalette? palette, ShapeSettings features)
    {
        var typeface = font.Typeface!;
        var unitsPerEm = typeface.Metrics.UnitsPerEm;
        var glyphs = Shaper.Shape(typeface, str, features).Glyphs;
        var scale = font.Size / unitsPerEm * _ppp;

        // Text the viewer can select: only for a run it can reproduce from the characters and the positions alone.
        if (_options.TextMode == SvgTextMode.Text && TryWriteText(str, font, color, originX, baselineY, letterSpacing, glyphs, scale))
            return;

        // The outline of an invisible run would paint; invisible text only exists to be selected, which outlines cannot be.
        if (InvisibleText)
            return;

        var skew = ItalicSkew(font);
        var uses = new List<GlyphUse>(glyphs.Count);
        var penX = originX;
        foreach (var glyph in glyphs)
        {
            var gx = penX + glyph.XOffset * scale;
            var gy = baselineY - glyph.YOffset * scale;
            if (!TryDrawSpecialGlyph(typeface, glyph.GlyphIndex, font, color, palette, gx, gy)
                && typeface.TryGetOutline((ushort)glyph.GlyphIndex, out var outline))
            {
                uses.Add(new GlyphUse(GlyphId(typeface, glyph.GlyphIndex, outline, scale, skew), gx, gy));
            }

            penX += (typeface.GetAdvance((ushort)glyph.GlyphIndex) + glyph.XAdvanceDelta) * scale + letterSpacing;
        }

        EmitGlyphs(uses, font, color);
    }

    /// <summary>
    /// Draws a glyph that is not a plain outline - a bitmap (CBDT/sbix), an OpenType SVG document or COLR/CPAL colour artwork - at a glyph
    /// origin in layout units, and says whether it did.
    /// </summary>
    private bool TryDrawSpecialGlyph(Typeface typeface, int glyphIndex, Font font, PaintColor color, FontPalette? palette, double originX, double baselineY)
    {
        if (typeface.HasBitmapGlyphs && typeface.TryGetBitmap((ushort)glyphIndex, font.Size, out var bitmap))
            return DrawBitmapGlyph(typeface, glyphIndex, bitmap, font.Size, originX, baselineY);

        var paletteIndex = palette?.BasePaletteIndex ?? 0;
        if (typeface.HasSvgGlyphs && TryDrawSvgGlyph(typeface, glyphIndex, font.Size, color, paletteIndex, ToGlyphOverrides(palette), originX, baselineY))
            return true;

        if (typeface.HasColorGlyphs && TryDrawColorGlyph(typeface, glyphIndex, font.Size, color, paletteIndex, ToGlyphOverrides(palette), originX, baselineY))
            return true;

        return false;
    }

    /// <summary>Draws a bitmap colour glyph at a glyph origin (layout units), the way the PDF backend places it.</summary>
    private bool DrawBitmapGlyph(Typeface typeface, int glyphId, EmbeddedBitmap bitmap, double fontSize, double originX, double baselineY)
    {
        if (BitmapImages.GetValue(typeface, _ => new Dictionary<(int, int), SvgImage?>()) is not { } cache)
            return false;

        SvgImage? image;
        lock (cache)
        {
            if (!cache.TryGetValue((glyphId, bitmap.Ppem), out image))
            {
                image = SvgImage.TryDecode(new System.IO.MemoryStream(bitmap.Data));
                cache[(glyphId, bitmap.Ppem)] = image;
            }
        }

        if (image is null)
            return false;

        var scale = fontSize / bitmap.Ppem * _ppp;
        DrawImage(image, new Rect(originX + bitmap.BearingX * scale, baselineY - bitmap.BearingTop * scale, bitmap.Width * scale, bitmap.Height * scale));
        return true;
    }

    private static readonly ConditionalWeakTable<Typeface, Dictionary<(int, int), SvgImage?>> BitmapImages = new();

    /// <summary>Draws a glyph's COLR/CPAL colour artwork through the shared painter, which targets this canvas as vector paths and gradients.</summary>
    private bool TryDrawColorGlyph(Typeface typeface, int glyphId, double fontSize, PaintColor color, int paletteIndex,
        IReadOnlyDictionary<int, PaintColor>? overrides, double originX, double baselineY)
    {
        if (typeface.GetColorPaint((ushort)glyphId) is null && !typeface.TryGetColorLayers((ushort)glyphId, out _))
            return false;

        var painter = new ColorGlyphPainter(typeface, fontSize * _ppp, color, yDown: true, paletteIndex, overrides);
        painter.Paint((ushort)glyphId, painter.Placement(originX, baselineY),
            new CanvasColorGlyphTarget(this) { SupportsAdditiveComposite = true, SupportsPeriodicConeGradients = true });
        return true;
    }

    /// <summary>
    /// Draws a glyph from its OpenType SVG document through the render context's SVG glyph painter, if it has one. The painter takes a
    /// position and size in points.
    /// </summary>
    private bool TryDrawSvgGlyph(Typeface typeface, int glyphId, double fontSize, PaintColor color, int paletteIndex,
        IReadOnlyDictionary<int, PaintColor>? overrides, double originX, double baselineY)
    {
        if (typeface.GetColorPaint((ushort)glyphId) is not null || typeface.TryGetColorLayers((ushort)glyphId, out _) ||
            !typeface.TryGetSvgGlyph((ushort)glyphId, out var svg))
            return false;

        _svgGlyphs ??= _renderContext.CreateSvgGlyphPainter(this);
        return _svgGlyphs?.TryPaint(typeface, (ushort)glyphId, svg, fontSize, originX / _ppp, baselineY / _ppp, color, paletteIndex, overrides) ?? false;
    }

    private ISvgGlyphPainter? _svgGlyphs;

    private static Dictionary<int, PaintColor>? ToGlyphOverrides(FontPalette? palette)
    {
        if (palette is not { Overrides.Count: > 0 })
            return null;

        var overrides = new Dictionary<int, PaintColor>(palette.Overrides.Count);
        foreach (var (entry, colour) in palette.Overrides)
            overrides[entry] = colour;

        return overrides;
    }

    private static double ItalicSkew(Font font) =>
        (font.SyntheticStyle & SyntheticStyle.Italic) != 0 ? font.ObliqueSkewSinus ?? TextMeasurement.ItalicSkewAngleSinus : 0;

    /// <summary>Appends a glyph outline (design units, y up) as path data, origin on the baseline at (x, y), sheared by <paramref name="skew"/> for a synthetic italic.</summary>
    private void AppendGlyph(StringBuilder d, GlyphOutline outline, double x, double y, double scale, double skew)
    {
        // Faux italic shears about the baseline: points above it move right.
        string X(OutlinePoint p) => N(x + p.X * scale + skew * p.Y * scale);
        string Y(OutlinePoint p) => N(y - p.Y * scale);

        for (var ci = 0; ci < outline.Contours.Count; ci++)
        {
            var contour = outline.Contours[ci];
            d.Append('M').Append(X(contour.Start)).Append(' ').Append(Y(contour.Start));
            for (var si = 0; si < contour.Segments.Count; si++)
            {
                var segment = contour.Segments[si];
                if (segment.IsCubic)
                {
                    d.Append('C').Append(X(segment.Control1)).Append(' ').Append(Y(segment.Control1)).Append(' ')
                        .Append(X(segment.Control2)).Append(' ').Append(Y(segment.Control2)).Append(' ')
                        .Append(X(segment.End)).Append(' ').Append(Y(segment.End));
                }
                else
                {
                    d.Append('L').Append(X(segment.End)).Append(' ').Append(Y(segment.End));
                }
            }

            d.Append('Z');
        }
    }

    /// <summary>A glyph of a run: the definition it is an instance of, and where its origin is.</summary>
    private readonly record struct GlyphUse(string Id, double X, double Y);

    /// <summary>
    /// The id of the definition of one glyph's outline at one size and slant, relative to the glyph's own origin: a glyph is written once
    /// and every place it is drawn is a <c>&lt;use&gt;</c> of it, which is what keeps a screenful of text a fraction of the size of the same
    /// outlines written out again at every position.
    /// </summary>
    private string GlyphId(Typeface typeface, int glyphIndex, GlyphOutline outline, double scale, double skew) =>
        _defs.GlyphId(typeface, glyphIndex, scale, skew, id =>
        {
            var d = new StringBuilder();
            AppendGlyph(d, outline, 0, 0, scale, skew);
            return $"<path id=\"{id}\" d=\"{d}\"/>";
        });

    /// <summary>
    /// Writes a run's glyphs painted with <paramref name="color"/>, as instances of their definitions in one group that carries the paint;
    /// a synthetic bold strokes the outlines at 2% of the em, as the PDF renderer does.
    /// </summary>
    private void EmitGlyphs(List<GlyphUse> uses, Font font, PaintColor color)
    {
        if (uses.Count == 0 || color.A == 0)
            return;

        var paint = new StringBuilder(" fill=\"").Append(SvgDefs.Hex(color)).Append('"');
        if (color.A < 255)
            paint.Append(" fill-opacity=\"").Append(N(SvgDefs.Alpha(color))).Append('"');

        if ((font.SyntheticStyle & SyntheticStyle.Bold) != 0)
        {
            paint.Append(" stroke=\"").Append(SvgDefs.Hex(color)).Append("\" stroke-width=\"").Append(N(font.Size * _ppp * TextMeasurement.BoldEmphasis))
                .Append("\" stroke-miterlimit=\"10\"");
            if (color.A < 255)
                paint.Append(" stroke-opacity=\"").Append(N(SvgDefs.Alpha(color))).Append('"');
        }

        if (uses.Count == 1)
        {
            Line($"<use href=\"#{uses[0].Id}\" x=\"{N(uses[0].X)}\" y=\"{N(uses[0].Y)}\"{paint}{Style()}/>");
            return;
        }

        Line($"<g{paint}{Style()}>");
        _depth++;
        foreach (var use in uses)
            Line($"<use href=\"#{use.Id}\" x=\"{N(use.X)}\" y=\"{N(use.Y)}\"/>");

        _depth--;
        Line("</g>");
    }

    // ---- selectable text ----------------------------------------------------------------------------------

    /// <summary>
    /// Writes the run as <c>&lt;text&gt;</c> when a viewer can reproduce it from the characters and per-character positions alone: every
    /// character is one glyph in reading order, the font's character map gives that glyph, and the font exports as an embeddable OpenType
    /// subset. False (the caller writes outlines) for anything else, such as ligatures, reordering, colour or bitmap glyphs.
    /// </summary>
    private bool TryWriteText(string str, Font font, PaintColor color, double originX, double baselineY, double letterSpacing,
        IReadOnlyList<PlacedGlyph> glyphs, double scale)
    {
        var typeface = font.Typeface!;
        if (typeface.HasColorGlyphs || typeface.HasBitmapGlyphs || typeface.HasSvgGlyphs || glyphs.Count != str.Length)
            return false;

        for (var i = 0; i < str.Length; i++)
        {
            var c = str[i];
            var glyph = glyphs[i];
            if (c < 0x20 || char.IsSurrogate(c) || glyph.ClusterStart != i || glyph.ClusterLength != 1 || glyph.IsHiddenIgnorable ||
                !typeface.TryMapRune(new System.Text.Rune(c), out var mapped) || mapped != glyph.GlyphIndex)
            {
                return false;
            }
        }

        var use = _defs.UseFont(typeface, () => CanEmbed(typeface));
        if (!use.Eligible)
            return false;

        var xs = new StringBuilder();
        var ys = new StringBuilder();
        var anyOffsetY = false;
        var penX = originX;
        for (var i = 0; i < glyphs.Count; i++)
        {
            var glyph = glyphs[i];
            use.Glyphs.Add(glyph.GlyphIndex);
            if (i > 0)
            {
                xs.Append(' ');
                ys.Append(' ');
            }

            xs.Append(N(penX + glyph.XOffset * scale));
            ys.Append(N(baselineY - glyph.YOffset * scale));
            anyOffsetY |= glyph.YOffset != 0;
            penX += (typeface.GetAdvance((ushort)glyph.GlyphIndex) + glyph.XAdvanceDelta) * scale + letterSpacing;
        }

        var sb = new StringBuilder("<text x=\"").Append(xs).Append("\" y=\"").Append(anyOffsetY ? ys.ToString() : N(baselineY))
            .Append("\" font-family=\"").Append(use.Name).Append("\" font-size=\"").Append(N(font.Size * _ppp)).Append('"');

        var skew = ItalicSkew(font);
        if (skew != 0)
            sb.Append(" transform=\"matrix(1 0 ").Append(SvgDefs.Factor(-skew)).Append(" 1 ").Append(N(skew * baselineY)).Append(" 0)\"");

        if (InvisibleText)
        {
            sb.Append(" fill-opacity=\"0\"");
        }
        else
        {
            sb.Append(" fill=\"").Append(SvgDefs.Hex(color)).Append('"');
            if (color.A < 255)
                sb.Append(" fill-opacity=\"").Append(N(SvgDefs.Alpha(color))).Append('"');
            if ((font.SyntheticStyle & SyntheticStyle.Bold) != 0)
                sb.Append(" stroke=\"").Append(SvgDefs.Hex(color)).Append("\" stroke-width=\"").Append(N(font.Size * _ppp * TextMeasurement.BoldEmphasis)).Append('"');
        }

        // The positions are explicit, so the viewer must not substitute or move anything on its own.
        const string verbatim = "font-kerning:none;font-variant-ligatures:none;font-synthesis:none;font-weight:normal;font-style:normal;white-space:pre";
        sb.Append(" xml:space=\"preserve\"");

        // A sheared run is placed by a transform on its own element, which a clip must not share: the group around it carries the clip.
        if (skew != 0 && FlatClipAttribute().Length > 0)
        {
            Line($"<g{Style()}>");
            _depth++;
            Line(sb.Append(" style=\"" + verbatim + "\">").Append(SvgDefs.Escape(str)).Append("</text>").ToString());
            _depth--;
            Line("</g>");
            return true;
        }

        Line(sb.Append(Style(verbatim)).Append('>').Append(SvgDefs.Escape(str)).Append("</text>").ToString());
        return true;
    }

    /// <summary>Whether the typeface exports as a font a browser can load that holds only the glyphs asked for.</summary>
    private static bool CanEmbed(Typeface typeface)
    {
        try
        {
            var probe = TypefaceExporter.ExportSubset(typeface, [0], keepCharacterMap: true);
            return probe.Format == ExportedFontFormat.OpenType && probe.IsSubset;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
