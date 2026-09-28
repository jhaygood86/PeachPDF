using PeachDrawing.Text;
using PeachDrawing.Text.Layout;
using PeachDrawing.Text.Shaping;
using System;
using System.Collections.Generic;

namespace PeachDrawing.Core
{
    /// <summary>
    /// Draws text that <c>PeachDrawing.Text</c> has already shaped and laid out onto any <see cref="Canvas"/>, so the positions a
    /// <see cref="ParagraphLayout"/> reports are the positions the text is painted at. Everything is in the canvas's user units,
    /// the units <see cref="Canvas.DrawRectangle(Brush, double, double, double, double)"/> takes; layout sizes are read in the same
    /// units, so a paragraph laid out at 16 draws 16 user units tall on a canvas whose <see cref="Canvas.PixelsPerPoint"/> is 1.
    /// </summary>
    public static class CanvasParagraphExtensions
    {
        /// <summary>Draws every run of <paramref name="layout"/> in one color.</summary>
        /// <param name="canvas">the canvas to draw onto</param>
        /// <param name="layout">the laid-out paragraph</param>
        /// <param name="origin">where the top-left corner of the layout goes</param>
        /// <param name="color">the color of the text</param>
        public static void DrawParagraph(this Canvas canvas, ParagraphLayout layout, PaintPoint origin, PaintColor color) =>
            canvas.DrawParagraph(layout, origin, _ => new ParagraphPaint(color));

        /// <summary>Draws every run of <paramref name="layout"/>, painted as <paramref name="paintForRun"/> says.</summary>
        /// <param name="canvas">the canvas to draw onto</param>
        /// <param name="layout">the laid-out paragraph</param>
        /// <param name="origin">where the top-left corner of the layout goes</param>
        /// <param name="paintForRun">the color and decorations of a run; called once for each run of text, including a generated hyphen or ellipsis</param>
        /// <param name="drawInlineBox">
        /// draws an inline box (see <see cref="ParagraphBuilder.AddInlineBox"/>): given the canvas, the run that stands for the box and the
        /// box's bounds already moved by <paramref name="origin"/>. Boxes are skipped when this is <see langword="null"/>.
        /// </param>
        public static void DrawParagraph(this Canvas canvas, ParagraphLayout layout, PaintPoint origin, Func<PlacedRun, ParagraphPaint> paintForRun,
            Action<Canvas, PlacedRun, Rect>? drawInlineBox = null)
        {
            ArgumentNullException.ThrowIfNull(canvas);
            ArgumentNullException.ThrowIfNull(layout);
            ArgumentNullException.ThrowIfNull(paintForRun);

            foreach (LineBox line in layout.Lines)
            {
                foreach (PlacedRun run in line.Runs)
                {
                    if (run.InlineBox is not null)
                    {
                        var bounds = run.InlineBoxBounds;
                        drawInlineBox?.Invoke(canvas, run, new Rect(origin.X + bounds.X, origin.Y + bounds.Y, bounds.Width, bounds.Height));
                        continue;
                    }

                    if (run.Glyphs.Glyphs.Count == 0)
                        continue;

                    ParagraphPaint paint = paintForRun(run);
                    var baselineOrigin = new PaintPoint(origin.X + run.X, origin.Y + run.Baseline);
                    DrawRun(canvas, run, baselineOrigin, paint.Color);
                    DrawDecorations(canvas, run, baselineOrigin, paint);
                }
            }
        }

        /// <summary>
        /// Draws a run of already-shaped glyphs, each starting where the previous one's advance ends, without shaping anything: the glyphs and
        /// positions are the shaper's, so a color font, kerning and ligatures come out as they were shaped.
        /// </summary>
        /// <param name="canvas">the canvas to draw onto</param>
        /// <param name="run">the shaped glyphs</param>
        /// <param name="size">the font size the run was shaped for, in the canvas's user units</param>
        /// <param name="baselineOrigin">where the first glyph's origin goes: the start of the run, on its baseline</param>
        /// <param name="color">the color of the text</param>
        /// <param name="letterSpacing">extra space added after every glyph, in user units</param>
        public static void DrawGlyphRun(this Canvas canvas, GlyphRun run, double size, PaintPoint baselineOrigin, PaintColor color, double letterSpacing = 0)
        {
            ArgumentNullException.ThrowIfNull(canvas);
            ArgumentNullException.ThrowIfNull(run);
            Place(canvas, run.Typeface, run.Glyphs, size, baselineOrigin, letterSpacing, advance: null, color);
        }

        private static void DrawRun(Canvas canvas, PlacedRun run, PaintPoint baselineOrigin, PaintColor color) =>
            Place(canvas, run.Glyphs.Typeface, run.Glyphs.Glyphs, run.Style.Size, baselineOrigin, 0, run.GetGlyphAdvance, color);

        private static void Place(Canvas canvas, Typeface typeface, IReadOnlyList<PlacedGlyph> glyphs, double size, PaintPoint baselineOrigin,
            double letterSpacing, Func<int, double>? advance, PaintColor color)
        {
            double unitsPerEm = typeface.Metrics.UnitsPerEm;
            if (unitsPerEm == 0 || glyphs.Count == 0)
                return;

            double scale = size / unitsPerEm;
            var placements = new List<GlyphPlacement>(glyphs.Count);
            double penX = baselineOrigin.X;
            for (int i = 0; i < glyphs.Count; i++)
            {
                PlacedGlyph glyph = glyphs[i];
                if (!glyph.IsHiddenIgnorable)
                {
                    placements.Add(new GlyphPlacement(glyph.GlyphIndex, penX + glyph.XOffset * scale, baselineOrigin.Y - glyph.YOffset * scale));
                }

                penX += advance is null
                    ? (typeface.GetAdvance((ushort)glyph.GlyphIndex) + glyph.XAdvanceDelta) * scale + letterSpacing
                    : advance(i);
            }

            // A Font's size is a true point size; a canvas's user unit is 1 / PixelsPerPoint of one.
            canvas.DrawGlyphs(placements, new TypefaceFont(typeface, size / canvas.PixelsPerPoint), color);
        }

        private static void DrawDecorations(Canvas canvas, PlacedRun run, PaintPoint baselineOrigin, ParagraphPaint paint)
        {
            if (paint.Decorations == TextDecorations.None || run.Width <= 0)
                return;

            var metrics = run.Glyphs.Typeface.Metrics;
            double unitsPerEm = metrics.UnitsPerEm;
            if (unitsPerEm == 0)
                return;

            double scale = run.Style.Size / unitsPerEm;
            Brush brush = canvas.GetSolidBrush(paint.DecorationColor ?? paint.Color);

            void Line(double position, double thickness)
            {
                // Font positions are y-up from the baseline; the canvas is y-down. The line is centred on its position, and is never thinner than half a point.
                double height = Math.Max(thickness * scale, 0.5 * canvas.PixelsPerPoint);
                canvas.DrawRectangle(brush, baselineOrigin.X, baselineOrigin.Y - position * scale - height / 2, run.Width, height);
            }

            if ((paint.Decorations & TextDecorations.Underline) != 0)
                Line(metrics.UnderlinePosition, metrics.UnderlineThickness);

            if ((paint.Decorations & TextDecorations.LineThrough) != 0)
                Line(metrics.StrikeoutPosition, metrics.StrikeoutThickness);

            if ((paint.Decorations & TextDecorations.Overline) != 0)
                Line(metrics.CellAscent - metrics.UnderlineThickness / 2.0, metrics.UnderlineThickness);
        }
    }
}
