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
                var wordPoint = new RPoint(wordFragment.Rect.X, wordFragment.Rect.Y + baselineAdjust);
                var text = word.FirstLineText ?? word.Text!;
                g.DrawString(text, font, styleSource.ActualColor, wordPoint, new RSize(word.Width, word.Height), styleSource.ActualLetterSpacing, styleSource.ActualFontPalette, styleSource.ActualTextShapingFeatures);
            }
        }

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
