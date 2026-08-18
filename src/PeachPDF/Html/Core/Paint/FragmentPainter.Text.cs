using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Html.Core.Utils;
using System;
using System.Text;

namespace PeachPDF.Html.Core.Paint
{
    /// <summary>
    /// The text half of the painter: drawing a fragment's laid-out words.
    /// </summary>
    internal sealed partial class FragmentPainter
    {
        /// <summary>
        /// Paints all the words of one fragment, each at its own fragment rectangle.
        /// </summary>
        /// <param name="g">the device to draw into</param>
        /// <param name="box">the box whose text style is painted</param>
        /// <param name="fragment">the fragment whose words are painted</param>
        private static void PaintWords(RGraphics g, CssBox box, BoxFragment fragment)
        {
            if (box.Width is null or { Length: <= 0 }) return;

            foreach (var wordFragment in fragment.Words)
            {
                var word = wordFragment.Word;
                if (word.IsLineBreak || word.IsImage) continue;
                var clip = g.GetClip();
                clip.Intersect(wordFragment.Rect);

                // A word whose box was relocated to the next page's content top (keep-with-next,
                // break-inside:avoid, orphans/widows) sits exactly flush against the previous page's
                // clip bottom - RRect.Intersect can land a hair off exact zero in either direction
                // (floating-point rounding across the several arithmetic steps a relocated box's Y goes
                // through), so neither RRect.Empty nor a strict zero check reliably catches it; the
                // epsilon does. Without this, a fully-clipped (invisible on screen, but present in the
                // content stream and text-extraction layer) duplicate of the word painted on the page it
                // just left. See GitHub issue #113.
                if (clip.Width <= VisibilityClipEpsilon || clip.Height <= VisibilityClipEpsilon) continue;

                if (word is CssRectLeader leader)
                {
                    PaintLeader(g, leader.FirstLineStyle ?? box, leader, wordFragment.Rect);
                    continue;
                }

                // A word on the target's first formatted line, under a ::first-line rule, uses that
                // resolved shadow box's font/color/letter-spacing instead of the box's own - it was
                // already measured against this same styleSource (see ApplyFirstLineStyleOverride), so
                // word.Top/Height are already consistent with it.
                var styleSource = word.FirstLineStyle ?? box;

                // A fragment drawn with a different font than the box's own ActualFont - a synthesized
                // small-caps run (smaller size) or a per-codepoint fallback face (different metrics) - is
                // top-anchored at the same word.Top (the shared line box's top), so without correction its
                // baseline would sit at a different height than its full-size neighbors'. Shift down by the
                // ascent difference so every fragment's baseline lines up. This is exactly 0 for an ordinary
                // word (font == ActualFont), so it is a no-op there.
                var font = CssBox.ResolveWordFont(word, styleSource);
                var baselineAdjust = styleSource.ActualFont.Ascent - font.Ascent;
                var text = word.FirstLineText ?? word.Text!;

                if (box.WritingMode.Value is WritingMode.VerticalRl or WritingMode.VerticalLr)
                {
                    // text-orientation decides per this box (upright/sideways force one answer for
                    // every word) or per fragment (mixed, the default - CssBox.AddWord/
                    // EmitPerCodepointFragments already split the word into maximal same-orientation
                    // runs, so word.IsUprightOrientation is a real per-fragment fact here, not a guess).
                    var isUpright = box.TextOrientation.Value switch
                    {
                        TextOrientation.Upright => true,
                        TextOrientation.Sideways => false,
                        _ => word.IsUprightOrientation
                    };

                    if (isUpright)
                    {
                        PaintUprightVerticalRun(g, text, font, styleSource, wordFragment.Rect, baselineAdjust);
                    }
                    else
                    {
                        // wordFragment.Rect is already the word's true physical (rotated) footprint, set
                        // by CssLayoutEngine.CreateVerticalLineBoxes via WritingModeFrame, which swaps
                        // width/height for a vertical box - so the glyph run's own natural (pre-rotation)
                        // size is that swap undone.
                        var naturalSize = new RSize(wordFragment.Rect.Height, wordFragment.Rect.Width);
                        var rotation = SidewaysRotation(wordFragment.Rect);
                        g.PushTransform(rotation);
                        g.DrawString(text, font, styleSource.ActualColor, new RPoint(0, baselineAdjust), naturalSize,
                            styleSource.ActualLetterSpacing, styleSource.ActualFontPalette, styleSource.ActualTextShapingFeatures);
                        g.PopTransform();
                    }
                }
                else
                {
                    var wordPoint = new RPoint(wordFragment.Rect.X, wordFragment.Rect.Y + baselineAdjust);
                    g.DrawString(text, font, styleSource.ActualColor, wordPoint, new RSize(word.Width, word.Height), styleSource.ActualLetterSpacing, styleSource.ActualFontPalette, styleSource.ActualTextShapingFeatures);
                }
            }
        }

        /// <summary>
        /// Paints an upright (unrotated) run within a vertical writing mode - one or more codepoints
        /// classified <see cref="PeachPDF.Text.VerticalOrientationClass.U"/>/
        /// <see cref="PeachPDF.Text.VerticalOrientationClass.Tu"/> (<see cref="CssRect.IsUprightOrientation"/>),
        /// stacked top-to-bottom down <paramref name="rect"/>'s
        /// own physical extent rather than rotated to fill it, since - unlike a rotated run, which is one
        /// natural horizontal glyph run reoriented as a whole (<see cref="SidewaysRotation"/>) - upright
        /// text has no single natural horizontal layout to reuse: each character keeps its own reading
        /// orientation and simply advances along the column instead of along a line.
        /// </summary>
        /// <remarks>
        /// No true OpenType vertical metrics (<c>vmtx</c>) are consulted yet (issue #770) - each
        /// character's own down-the-column advance is <paramref name="font"/>'s own line height
        /// (ascender + descender), the exact same basis <see cref="CssLayoutEngine.NaturalWordSize"/>
        /// already reserved this run's own <paramref name="rect"/> extent from. This is deliberately
        /// not each character's individually-measured horizontal advance width
        /// (<see cref="CssLayoutEngine.MeasureUprightRunCharacters"/>'s own per-character <c>Size</c>,
        /// still used below for cross-axis centering only): <see cref="RGraphics.DrawString"/> always
        /// renders a glyph across the font's full line-height span from its anchor regardless of that
        /// glyph's own advance width, so stepping by a narrower advance (a real CJK codepoint can
        /// measure a materially narrower hmtx advance than its font's line height) visibly overlapped
        /// each character with the next, and overran into whatever followed once the run finished. Each
        /// character is centered across the column (<paramref name="rect"/>'s own thickness) rather than
        /// left-aligned, matching CJK vertical typesetting convention.
        /// </remarks>
        private static void PaintUprightVerticalRun(RGraphics g, string text, RFont font, CssBox styleSource, RRect rect, double baselineAdjust)
        {
            double offset = 0;

            foreach (var (charText, charSize) in CssLayoutEngine.MeasureUprightRunCharacters(g, text, font, styleSource.ActualTextShapingFeatures))
            {
                var x = rect.X + Math.Max(0, (rect.Width - charSize.Width) / 2);
                var y = rect.Y + offset + baselineAdjust;

                g.DrawString(charText, font, styleSource.ActualColor, new RPoint(x, y), charSize,
                    styleSource.ActualLetterSpacing, styleSource.ActualFontPalette, styleSource.ActualTextShapingFeatures);

                offset += font.Height + styleSource.ActualLetterSpacing;
            }
        }

        /// <summary>
        /// The matrix that rotates a glyph run 90° clockwise from its natural (horizontal) orientation so
        /// it exactly fills <paramref name="physicalFootprint"/> - the proven rotate-about-a-point
        /// mechanism <c>SvgRenderer.PaintGlyphs</c> already uses for arbitrary angles
        /// (<c>PushTransform</c>/draw at the natural origin/<c>PopTransform</c>), specialized to the one
        /// fixed angle and target-rect shape vertical text needs, rather than forcing the two together:
        /// SVG rotates an arbitrary angle around a point it already has: this always rotates 90° to fill a
        /// footprint it is handed instead, a different enough shape that sharing more than the underlying
        /// <see cref="RGraphics.PushTransform"/> primitive would cost more than it saves.
        /// </summary>
        /// <remarks>
        /// Derivation: rotating a natural top-left-origin box of size (w, h) by 90° clockwise
        /// ((x, y) → (-y, x), the same convention CSS <c>rotate(90deg)</c> uses in a Y-down space) sends
        /// its corners to X ∈ [-h, 0], Y ∈ [0, w] - i.e. a box of the swapped size (h, w), whose own
        /// top-left corner is the rotated image of the natural box's bottom-left corner, (0, h) ↦ (-h, 0).
        /// Translating that corner onto <paramref name="physicalFootprint"/>'s own top-left
        /// (<c>X</c>, <c>Y</c>) is what <c>OffsetX</c>/<c>OffsetY</c> below do; drawing then happens at the
        /// natural, untranslated origin, exactly as <c>SvgRenderer.PaintGlyphs</c> already does.
        /// </remarks>
        private static RMatrix SidewaysRotation(RRect physicalFootprint) =>
            new(0, 1, -1, 0, physicalFootprint.X + physicalFootprint.Width, physicalFootprint.Y);

        /// <summary>
        /// Paints a <c>leader()</c> content-list item (css-content-3 §6) - the "Chapter One ..........
        /// 12" table-of-contents idiom's fill. <see cref="LeaderKind.Dotted"/>/<see cref="LeaderKind.Custom"/>
        /// tile the pattern unit as real glyphs in <paramref name="styleSource"/>'s own font (matching
        /// what typing the literal characters would look like - a real UA's own rendering - rather than a
        /// dashed pen line, whose gaps are drawn at underline position/spacing, not baseline-anchored
        /// glyph spacing). <see cref="LeaderKind.Solid"/> is drawn as one continuous filled rule instead of
        /// tiled underscores, which would show font-dependent gaps a real continuous rule never has.
        /// <see cref="LeaderKind.Space"/> paints nothing - an invisible reserved gap, as its name implies.
        /// </summary>
        private static void PaintLeader(RGraphics g, CssBox styleSource, CssRectLeader leader, RRect rect)
        {
            if (leader.Kind == LeaderKind.Space || rect.Width <= 0) return;

            var font = styleSource.ActualFont;

            if (leader.Kind == LeaderKind.Solid)
            {
                var thickness = Math.Max(1d, font.Height / 12d);
                var y = Math.Round(rect.Y + font.UnderlineOffset);
                using var brush = g.GetSolidBrush(styleSource.ActualColor);
                g.DrawRectangle(brush, rect.X, y, rect.Width, thickness);
                return;
            }

            var unit = leader.Kind == LeaderKind.Custom ? leader.CustomPattern : ".";
            if (string.IsNullOrEmpty(unit)) return;

            var unitWidth = g.MeasureString(unit, font, styleSource.ActualTextShapingFeatures).Width;
            if (unitWidth <= 0) return;

            var repeatCount = (int)(rect.Width / unitWidth);
            if (repeatCount <= 0) return;

            var tiled = new StringBuilder(unit.Length * repeatCount);
            for (var i = 0; i < repeatCount; i++) tiled.Append(unit);

            g.DrawString(tiled.ToString(), font, styleSource.ActualColor, new RPoint(rect.X, rect.Y),
                new RSize(rect.Width, rect.Height), styleSource.ActualLetterSpacing, styleSource.ActualFontPalette,
                styleSource.ActualTextShapingFeatures);
        }
    }
}
