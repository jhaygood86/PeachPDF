#region PeachPDF - A .NET library for rendering HTML to PDF
//
// Paints color-font (COLR/CPAL) glyphs as vector fills into the PDF content
// stream. A small embedded subset is used only for invisible selectable text;
// it never supplies the visible glyph artwork. Driven from
// XGraphicsPdfRenderer.DrawString for fonts that report IsColorFont.
//
//   - COLR v0: each base glyph is a stack of (layer glyph, palette color)
//     outlines painted bottom-to-top (this file).
//   - COLR v1: a recursive paint graph (ColorGlyphPainter.ColrV1.cs).
//
// Glyph outlines are decoded to design-unit contours and mapped to world space
// (the same space DrawString's baseline is in) through a ColrAffine that starts
// as the per-glyph placement and, for v1, composes the paint graph's own
// transforms. Fills/clips go through the shared XGraphics, so page scaling and
// the WorldToView mapping apply exactly as for ordinary vector content.
//
#endregion

using System;
using System.Collections.Generic;
using PeachPDF.Fonts.OpenType;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Text;

namespace PeachPDF.PdfSharpCore.Drawing.Pdf
{
    internal sealed partial class ColorGlyphPainter
    {
        private const int UseForegroundColor = 0xFFFF;

        private readonly XGraphicsPdfRenderer _renderer;
        private readonly XGraphics _gfx;
        private readonly OpenTypeDescriptor _descriptor;
        private readonly XFont _font;
        private readonly XBrush _textBrush;
        private readonly double _scale;         // design units -> world units
        private readonly double _letterSpacing; // world units
        private readonly bool _pageDownwards;
        private readonly XColor _foreground;
        private readonly int _paletteIndex;     // selected CPAL palette (CSS font-palette), default 0
        private readonly IReadOnlyDictionary<int, XColor>? _overrides; // CPAL entry index -> override color

        private readonly double _baselineX;
        private readonly double _baselineY;

        public ColorGlyphPainter(XGraphicsPdfRenderer renderer, OpenTypeDescriptor descriptor, XFont font,
            XBrush brush, double baselineX, double baselineY, double letterSpacing, XPageDirection pageDirection,
            int paletteIndex = 0, IReadOnlyDictionary<int, XColor>? overrides = null)
        {
            _renderer = renderer;
            _gfx = renderer.Gfx;
            _descriptor = descriptor;
            _font = font;
            _textBrush = brush;
            _scale = font.Size / descriptor.UnitsPerEm;
            _letterSpacing = letterSpacing;
            _pageDownwards = pageDirection == XPageDirection.Downwards;
            _foreground = brush is XSolidBrush solid ? solid.Color : XColors.Black;
            _paletteIndex = paletteIndex;
            _overrides = overrides is { Count: > 0 } ? overrides : null;
            _baselineX = baselineX;
            _baselineY = baselineY;
        }

        /// <summary>
        /// Paints every shaped glyph of the run as vectors, then emits one shared rendering-mode-3 text
        /// object whose source-bearing glyphs are individually wrapped in Unicode <c>/ActualText</c>.
        /// The vector paths remain the only visible ink; the text show adds selection geometry and
        /// <c>/ActualText</c> preserves the exact per-occurrence Unicode sequence.
        /// </summary>
        public void Paint(string text, IReadOnlyList<ShapedGlyph> glyphs, string? logicalText = null)
        {
            string?[] actualTextByGlyph = BuildActualTextByGlyph(text, logicalText, glyphs);
            double penX = 0;
            for (int i = 0; i < glyphs.Count; i++)
            {
                ShapedGlyph glyph = glyphs[i];
                double glyphX = _baselineX + penX + glyph.XOffset * _scale;

                // GPOS positioning (kerning's XOffset, mark attachment's XOffset/YOffset) shifts where
                // this glyph paints without changing its own outline shape - see GposPositioner.
                // The artwork itself is identical wherever the glyph lands, so it is drawn once into a
                // Form XObject and referenced here; only when that cannot apply is it inlined.
                if (!TryPaintGlyphFromForm(glyph.GlyphIndex, glyphX, glyph.YOffset * _scale))
                    PaintGlyph(glyph.GlyphIndex, glyphX, glyph.YOffset * _scale);

                penX += (_descriptor.GlyphIndexToWidth(glyph.GlyphIndex) + glyph.XAdvanceDelta) * _scale + _letterSpacing;
            }

            bool hasSelectableGlyph = false;
            for (int i = 0; i < actualTextByGlyph.Length; i++)
                hasSelectableGlyph |= actualTextByGlyph[i] is { Length: > 0 };
            if (!hasSelectableGlyph)
                return;

            // Paths force graphic mode, so paint all visible artwork first and then share one BT/ET pair
            // across the run. /ActualText remains per glyph because the same CID can represent distinct
            // source sequences at different occurrences (for example heart with and without VS16).
            _renderer.BeginInvisibleTextRun(_font, _textBrush);
            try
            {
                penX = 0;
                for (int i = 0; i < glyphs.Count; i++)
                {
                    ShapedGlyph glyph = glyphs[i];
                    if (actualTextByGlyph[i] is { Length: > 0 } actualText)
                    {
                        double glyphX = _baselineX + penX + glyph.XOffset * _scale;
                        double glyphY = _pageDownwards
                            ? _baselineY - glyph.YOffset * _scale
                            : _baselineY + glyph.YOffset * _scale;

                        _renderer.BeginActualText(actualText);
                        try
                        {
                            _renderer.DrawInvisibleGlyph(_font, glyph, actualText, glyphX, glyphY);
                        }
                        finally
                        {
                            _renderer.EndMarkedContent();
                        }
                    }

                    // A GSUB Multiple Substitution's second and later output glyphs deliberately own no
                    // source characters: the first output carries the original cluster once, rather than
                    // every painted expansion glyph making extraction repeat it.
                    penX += (_descriptor.GlyphIndexToWidth(glyph.GlyphIndex) + glyph.XAdvanceDelta) * _scale + _letterSpacing;
                }
            }
            finally
            {
                _renderer.EndInvisibleTextRun();
            }
        }

        /// <summary>
        /// Assigns every UTF-16 code unit in the original run to exactly one surviving shaped glyph.
        /// Besides the ordinary one-character and ligature cases, this keeps default-ignorables that
        /// shaping consumed or deleted (VS16, ZWJ, emoji tag characters, bidi controls) in the copied
        /// text even though they correctly have no painted glyph of their own.
        /// </summary>
        internal static string?[] BuildActualTextByGlyph(string text, string? logicalText, IReadOnlyList<ShapedGlyph> glyphs)
        {
            var result = new string?[glyphs.Count];
            if (text.Length == 0 || glyphs.Count == 0)
                return result;

            // Matches CMapInfo.AddShapedText's contract: logicalText differs only when it is a
            // positionally-aligned source for an already bidi-transformed display string.
            string source = logicalText != null && logicalText.Length == text.Length && logicalText != text
                ? logicalText
                : text;

            var ownerByCodeUnit = new int[text.Length];
            Array.Fill(ownerByCodeUnit, -1);

            // A ligature span can overlap a separately-painted skipped mark. Let the widest span own
            // those characters first; the mark then contributes its vector ink without duplicating text.
            // Shaped runs almost always have disjoint clusters, so claim those directly and only allocate
            // and sort an index list when an overlap proves that precedence is actually needed.
            bool hasOverlappingClusters = false;
            for (int i = 0; i < glyphs.Count; i++)
            {
                ShapedGlyph glyph = glyphs[i];
                if (glyph.ClusterLength <= 0)
                    continue;

                int start = Math.Clamp(glyph.ClusterStart, 0, text.Length);
                int end = Math.Clamp(glyph.ClusterStart + glyph.ClusterLength, start, text.Length);
                for (int codeUnit = start; codeUnit < end; codeUnit++)
                {
                    if (ownerByCodeUnit[codeUnit] >= 0)
                    {
                        hasOverlappingClusters = true;
                    }
                    else
                    {
                        ownerByCodeUnit[codeUnit] = i;
                    }
                }
            }

            if (hasOverlappingClusters)
            {
                Array.Fill(ownerByCodeUnit, -1);
                var sourceBearingGlyphs = new List<int>(glyphs.Count);
                for (int i = 0; i < glyphs.Count; i++)
                {
                    if (glyphs[i].ClusterLength > 0)
                        sourceBearingGlyphs.Add(i);
                }

                sourceBearingGlyphs.Sort((left, right) =>
                {
                    int byLength = glyphs[right].ClusterLength.CompareTo(glyphs[left].ClusterLength);
                    if (byLength != 0)
                        return byLength;

                    int byStart = glyphs[left].ClusterStart.CompareTo(glyphs[right].ClusterStart);
                    return byStart != 0 ? byStart : left.CompareTo(right);
                });

                foreach (int glyphIndex in sourceBearingGlyphs)
                {
                    ShapedGlyph glyph = glyphs[glyphIndex];
                    int start = Math.Clamp(glyph.ClusterStart, 0, text.Length);
                    int end = Math.Clamp(glyph.ClusterStart + glyph.ClusterLength, start, text.Length);
                    for (int codeUnit = start; codeUnit < end; codeUnit++)
                    {
                        if (ownerByCodeUnit[codeUnit] < 0)
                            ownerByCodeUnit[codeUnit] = glyphIndex;
                    }
                }
            }

            // Any unowned interval came from a source character which left no surviving glyph. Attach
            // an interior/trailing interval to its preceding cluster (heart + VS16 is the canonical
            // case), or a leading interval to the following cluster.
            int gapStart = 0;
            while (gapStart < ownerByCodeUnit.Length)
            {
                if (ownerByCodeUnit[gapStart] >= 0)
                {
                    gapStart++;
                    continue;
                }

                int gapEnd = gapStart + 1;
                while (gapEnd < ownerByCodeUnit.Length && ownerByCodeUnit[gapEnd] < 0)
                    gapEnd++;

                int owner = gapStart > 0 ? ownerByCodeUnit[gapStart - 1] : -1;
                if (owner < 0 && gapEnd < ownerByCodeUnit.Length)
                    owner = ownerByCodeUnit[gapEnd];

                if (owner >= 0)
                {
                    for (int codeUnit = gapStart; codeUnit < gapEnd; codeUnit++)
                        ownerByCodeUnit[codeUnit] = owner;
                }

                gapStart = gapEnd;
            }

            // Ownership normally consists of one contiguous source range per glyph, so take that range
            // directly instead of allocating a StringBuilder for every glyph.
            int rangeStart = 0;
            while (rangeStart < source.Length)
            {
                int owner = ownerByCodeUnit[rangeStart];
                if (owner < 0)
                {
                    rangeStart++;
                    continue;
                }

                int rangeEnd = rangeStart + 1;
                while (rangeEnd < source.Length && ownerByCodeUnit[rangeEnd] == owner)
                    rangeEnd++;

                string ownedText = source.Substring(rangeStart, rangeEnd - rangeStart);
                result[owner] = result[owner] is null
                    ? ownedText
                    : string.Concat(result[owner], ownedText);
                rangeStart = rangeEnd;
            }

            return result;
        }

        private void PaintGlyph(int glyphId, double originX, double originYOffset = 0)
        {
            ColrAffine placement = Placement(originX, originYOffset);
            ColrTable colr = _descriptor.ColorTable;

            // Per the COLR processing model a v1-aware renderer resolves the v1 BaseGlyphList first,
            // falling back to the v0 layer records only when the glyph has no v1 paint.
            if (colr.Version >= 1 && colr.GetV1BaseGlyphPaint(glyphId) is { } paint)
            {
                PaintV1(paint, placement, hasClip: false, clip: default, depth: 0);
                return;
            }

            if (colr.TryGetV0Layers(glyphId, out var layers))
            {
                foreach ((int layerGlyphId, int paletteIndex) in layers)
                    FillGlyphOutline(layerGlyphId, placement, ResolveColor(paletteIndex));
                return;
            }

            // A glyph with no color record inside a color font (e.g. space, digits): draw its plain
            // outline in the text color.
            FillGlyphOutline(glyphId, placement, _foreground);
        }

        /// <summary>Fills a single glyph's outline (mapped by <paramref name="transform"/>) with a solid color.</summary>
        private void FillGlyphOutline(int glyphId, ColrAffine transform, XColor color)
        {
            if (!_descriptor.TryGetGlyphOutline(glyphId, out GlyphOutline outline) || outline.IsEmpty)
                return;

            if (_measuring)
            {
                IncludeInMeasuredBounds(WorldBounds(outline, transform));
                return;
            }

            _gfx.DrawPath(new XSolidBrush(color), BuildPath(outline, transform));
        }

        private static XGraphicsPath BuildPath(GlyphOutline outline, ColrAffine transform)
        {
            int pointCount = outline.Contours.Count;
            foreach (GlyphContour contour in outline.Contours)
            {
                foreach (GlyphSegment segment in contour.Segments)
                    pointCount += segment.IsCubic ? 3 : 1;
            }

            var path = new XGraphicsPath(pointCount) { FillMode = XFillMode.Winding };

            foreach (GlyphContour contour in outline.Contours)
            {
                XPoint current = Map(transform, contour.Start.X, contour.Start.Y);
                foreach (GlyphSegment segment in contour.Segments)
                {
                    XPoint end = Map(transform, segment.End.X, segment.End.Y);
                    if (segment.IsCubic)
                    {
                        XPoint c1 = Map(transform, segment.Control1.X, segment.Control1.Y);
                        XPoint c2 = Map(transform, segment.Control2.X, segment.Control2.Y);
                        path.AddBezier(current.X, current.Y, c1.X, c1.Y, c2.X, c2.Y, end.X, end.Y);
                    }
                    else
                    {
                        path.AddLine(current.X, current.Y, end.X, end.Y);
                    }
                    current = end;
                }
                path.CloseFigure();
            }

            return path;
        }

        private static XPoint Map(ColrAffine t, double x, double y)
            => new(t.XX * x + t.XY * y + t.DX, t.YX * x + t.YY * y + t.DY);

        /// <summary>The design-units-&gt;world placement affine for a glyph at the given pen origin -
        /// <paramref name="originYOffset"/> is a GPOS mark-positioning Y delta (world units, already
        /// scaled and sign-adjusted for page direction the same way X is by the caller).</summary>
        private ColrAffine Placement(double originX, double originYOffset = 0)
        {
            // Font em-square is y-up; the page (when downwards) is y-down, so flip Y - the offset
            // flips the same way, since it moves the glyph up in font space regardless of page direction.
            double yy = _pageDownwards ? -_scale : _scale;
            double baselineY = _pageDownwards ? _baselineY - originYOffset : _baselineY + originYOffset;
            return new ColrAffine(_scale, 0, 0, yy, originX, baselineY);
        }

        private XColor ResolveColor(int paletteIndex) => ResolveColor(paletteIndex, 1.0);

        private XColor ResolveColor(int paletteIndex, double alpha)
        {
            XColor color;
            if (paletteIndex == UseForegroundColor)
            {
                // The COLR "use text color" sentinel is a paint reference, not a real CPAL entry index, so it
                // resolves to the text color regardless of any font-palette override-colors.
                color = _foreground;
            }
            else if (_overrides is not null && _overrides.TryGetValue(paletteIndex, out var over))
            {
                color = over;
            }
            else if (_descriptor.ColorPalette.TryGetColor(_paletteIndex, paletteIndex, out var c))
            {
                color = XColor.FromArgb(c.A, c.R, c.G, c.B);
            }
            else
            {
                color = _foreground;
            }

            if (alpha < 1.0)
            {
                // XColor.A is a 0..1 double, but FromArgb's alpha argument is a 0..255 byte - scale, or
                // every COLR paint alpha under 0.5 rounds to a fully transparent 0 and everything above
                // it to 1/255, which is why alpha'd COLR content used to be invisible.
                int scaledAlpha = (int)System.Math.Round(color.A * alpha * 255.0, MidpointRounding.AwayFromZero);
                color = XColor.FromArgb(System.Math.Clamp(scaledAlpha, 0, 255), color.R, color.G, color.B);
            }
            return color;
        }
    }
}
