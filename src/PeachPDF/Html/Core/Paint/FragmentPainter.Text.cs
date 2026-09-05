using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PeachPDF.Html.Core.Paint
{
    /// <summary>
    /// The text half of the painter: drawing a fragment's laid-out words.
    /// </summary>
    internal sealed partial class FragmentPainter
    {
        /// <summary>
        /// Paints all the words of one fragment, each at its own fragment rectangle. <c>text-overflow</c>
        /// (like <c>overflow</c> itself - CSS Overflow 3 applies both to "block containers") is read off
        /// <paramref name="box"/>'s own <see cref="CssBox.ContainingBlock"/>, not <paramref name="box"/>
        /// directly: the box actually holding words is routinely a plain inline run - an anonymous
        /// wrapper CssBox.ParseToWords produces for a block element's own direct text, or a real
        /// <c>&lt;span&gt;</c> - never the block container the style was declared on, exactly mirroring
        /// how <c>RenderUtils.OverflowClipOf</c>/<c>TryPushOverflowClip</c> already resolve an ancestor's
        /// <c>overflow: hidden</c> for this same box.
        /// </summary>
        /// <remarks>
        /// Gated on <c>Overflow.Hidden</c> specifically, not "anything but visible": <c>auto</c>/
        /// <c>scroll</c> don't establish a real clip in this renderer either
        /// (<c>RenderUtils.OverflowClipOf</c> only special-cases <c>Hidden</c> - there is no interactive
        /// scrolling in a PDF, so this renderer already lets <c>auto</c>/<c>scroll</c> content overflow
        /// unclipped, see the <c>overflow</c> property's own <c>css-properties.json</c> comment), so an
        /// ellipsis over still-unclipped content would be a confusing half-effect. This also means
        /// <see cref="Fragments.BoxFragment.OverflowClip"/> - resolved by that exact same walk, over that
        /// exact same starting box and exact same <c>Hidden</c> check - is guaranteed populated whenever
        /// ellipsis is active, so <see cref="PaintWordsWithEllipsis"/> can use it directly as the
        /// containing block's own content-edge rectangle instead of needing that box's own fragment.
        /// </remarks>
        /// <param name="g">the device to draw into</param>
        /// <param name="box">the box whose text style is painted</param>
        /// <param name="fragment">the fragment whose words are painted</param>
        private void PaintWords(RGraphics g, CssBox box, BoxFragment fragment)
        {
            if (box.Width is null or { Length: <= 0 }) return;

            var containingBlock = box.ContainingBlock;
            var ellipsisActive = containingBlock.TextOverflow.Value == TextOverflow.Ellipsis
                                  && containingBlock.Overflow.Value == Overflow.Hidden;

            if (!ellipsisActive || fragment.Lines.Count == 0)
            {
                PaintWordSequence(g, box, fragment.Words);
                return;
            }

            PaintWordsWithEllipsis(g, box, containingBlock, fragment);
        }

        /// <summary>
        /// Paints an ordered sequence of words - the ordinary (no truncation) path, and also what a
        /// truncated line's own surviving whole words are painted through. Identical to what
        /// <see cref="PaintWords"/> always did before <c>text-overflow</c> existed, just made reusable
        /// over an arbitrary (not necessarily <see cref="Fragments.BoxFragment.Words"/> itself) ordered
        /// list.
        /// </summary>
        private static void PaintWordSequence(RGraphics g, CssBox box, IReadOnlyList<TextFragment> words)
        {
            foreach (var wordFragment in words)
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

                var text = word.FirstLineText ?? word.Text!;
                // A ::first-line override (FirstLineText) is its own independently-mirrored string
                // derived from OriginalText, not from PreMirrorText - only the ordinary (non-overridden)
                // path has a known logical-order source to recover for ToUnicode text extraction. text
                // itself equals PreMirrorText whenever this word was never actually mirrored (the
                // overwhelming common case) - CMapInfo.AddShapedText's own remap formula needs a
                // *positionally-aligned* logical source (see its own remarks), so only a genuinely
                // mirrored word's PreMirrorText gets reversed (ReverseRunes - position only, no
                // re-mirroring) before being handed down; passing it unconditionally would misalign an
                // unmirrored word's own already-correct (identity) logical text.
                string? logicalText = null;
                if (word.FirstLineText is null && word is CssRectWord { } rectWord && rectWord.PreMirrorText != text)
                    logicalText = PeachPDF.Text.Bidi.BidiMirrorResolver.ReverseRunes(rectWord.PreMirrorText);
                DrawWordGlyphs(g, box, word, wordFragment.Rect, text, new RSize(word.Width, word.Height), logicalText: logicalText);
            }
        }

        /// <summary>
        /// Draws one word's (or, for a <c>text-overflow</c> truncation, one word's kept substring's)
        /// glyphs at its own position and orientation - the per-word body every ordinary word already
        /// went through, factored out so a truncated word's shorter <paramref name="text"/> can go
        /// through the identical upright/sideways/horizontal dispatch instead of duplicating it.
        /// </summary>
        /// <param name="g">the device to draw into</param>
        /// <param name="box">the box whose writing-mode/text-orientation style governs the dispatch</param>
        /// <param name="word">the source word - its style, font, and (for an ordinary paint) own text</param>
        /// <param name="rect">
        /// the word's own fragment rectangle for an ordinary (untruncated) word; for a partially kept
        /// word, the caller has already repositioned this to where <paramref name="text"/> itself
        /// belongs (its own natural anchor edge doesn't move when a word is shortened - only how much of
        /// it is drawn does).
        /// </param>
        /// <param name="text">the exact string to draw - the full word, or a truncated substring.</param>
        /// <param name="textSize">
        /// only consulted by the horizontal branch (the vertical branches derive their own size from
        /// <paramref name="rect"/>/the text's own per-character measurement).
        /// </param>
        /// <param name="fontOverride">
        /// when set, used instead of <paramref name="word"/>'s own resolved font - a
        /// <c>text-overflow</c> ellipsis glyph is never one of <paramref name="word"/>'s own codepoints,
        /// so it needs its own per-codepoint fallback resolution (<see cref="CssBox.ActualFontForCodepoint"/>)
        /// rather than whatever font happens to cover <paramref name="word"/>'s own script - reusing the
        /// cut word's font for it silently drew nothing when that font's family didn't include a "…"
        /// glyph (e.g. a narrow embedded script subset).
        /// </param>
        /// <param name="logicalText">
        /// <paramref name="text"/>'s true logical-order (pre-bidi-mirroring) source, when known and
        /// different - see <see cref="RGraphics.DrawString(string, RFont, RColor, RPoint, RSize, double, RFontPalette?, TextShapingFeatures?, string?)"/>.
        /// Null for a truncated/ellipsis <paramref name="text"/> (a caller-kept substring or a synthesized
        /// "…" glyph, neither of which is <paramref name="word"/>'s own full text) and for any word with
        /// no distinct logical-order source to recover.
        /// </param>
        private static void DrawWordGlyphs(RGraphics g, CssBox box, CssRect word, RRect rect, string text, RSize textSize, RFont? fontOverride = null, string? logicalText = null)
        {
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
            var font = fontOverride ?? CssBox.ResolveWordFont(word, styleSource);
            var baselineAdjust = styleSource.ActualFont.Ascent - font.Ascent;
            // A word's own resolved script tag/Arabic-family joining forms (CssBox.CharScripts/
            // JoiningForms, sliced per word by AppendWordsFromText) override styleSource's own
            // box-level shaping-feature request - see ResolveWordShapingFeatures.
            var wordFeatures = styleSource.ResolveWordShapingFeatures(word);

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
                    PaintUprightVerticalRun(g, text, font, styleSource, rect, baselineAdjust, wordFeatures, logicalText);
                }
                else
                {
                    // rect is already the word's true physical (rotated) footprint, set by
                    // CssLayoutEngine.CreateVerticalLineBoxes via WritingModeFrame, which swaps
                    // width/height for a vertical box - so the glyph run's own natural (pre-rotation)
                    // size is that swap undone.
                    var naturalSize = new RSize(rect.Height, rect.Width);
                    var rotation = SidewaysRotation(rect);
                    g.PushTransform(rotation);
                    g.DrawString(text, font, styleSource.ActualColor, new RPoint(0, baselineAdjust), naturalSize,
                        styleSource.ActualLetterSpacing, styleSource.ActualFontPalette, wordFeatures, logicalText);
                    g.PopTransform();
                }
            }
            else
            {
                var wordPoint = new RPoint(rect.X, rect.Y + baselineAdjust);
                g.DrawString(text, font, styleSource.ActualColor, wordPoint, textSize, styleSource.ActualLetterSpacing, styleSource.ActualFontPalette, wordFeatures, logicalText);
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
        /// When <paramref name="font"/> carries real OpenType vertical metrics
        /// (<see cref="RFont.HasVerticalMetrics"/>, backed by <c>vhea</c>/<c>vmtx</c> - issue #770), each
        /// character's own down-the-column advance is its real <see cref="RFont.GetVerticalAdvance"/>.
        /// Otherwise each character's advance is <paramref name="font"/>'s own line height (ascender +
        /// descender), the exact same basis <see cref="CssLayoutEngine.NaturalWordSize"/> already
        /// reserved this run's own <paramref name="rect"/> extent from - both branches must stay in
        /// lockstep with that method's own gate on the same <see cref="RFont.HasVerticalMetrics"/> flag,
        /// or layout's reservation and paint's actual step disagree. The line-height fallback is
        /// deliberately not each character's individually-measured horizontal advance width
        /// (<see cref="CssLayoutEngine.MeasureUprightRunCharacters"/>'s own per-character <c>Size</c>,
        /// still used below for cross-axis centering only): <see cref="RGraphics.DrawString(string, RFont, RColor, RPoint, RSize, double, RFontPalette?, TextShapingFeatures?)"/> always
        /// renders a glyph across the font's full line-height span from its anchor regardless of that
        /// glyph's own advance width, so stepping by a narrower advance (a real CJK codepoint can
        /// measure a materially narrower hmtx advance than its font's line height) visibly overlapped
        /// each character with the next, and overran into whatever followed once the run finished. Each
        /// character is centered across the column (<paramref name="rect"/>'s own thickness) rather than
        /// left-aligned, matching CJK vertical typesetting convention.
        ///
        /// A real <c>vmtx</c> advance is legitimately, routinely *smaller* than the font's line height (a
        /// CJK vertical font typically advances by one em; ascent+descent is usually well over one em) -
        /// so once real metrics make the per-character step narrower than <see cref="RFont.Height"/>
        /// again, <see cref="RGraphics.DrawString(string, RFont, RColor, RPoint, RSize, double, RFontPalette?, TextShapingFeatures?)"/>'s own "always paints a full line-height-tall span"
        /// behavior reintroduces precisely the bleed-into-the-next-character overlap the line-height
        /// fallback above exists to avoid, unless each character's paint is confined to its own reserved
        /// cell. <see cref="RGraphics.PushClip(RRect)"/>/<see cref="RGraphics.PopClip"/> around each
        /// <see cref="RGraphics.DrawString(string, RFont, RColor, RPoint, RSize, double, RFontPalette?, TextShapingFeatures?)"/> call does exactly that when real metrics are in play; the
        /// line-height fallback needs no clip, since its advance already equals the full painted span by
        /// construction.
        ///
        /// When <paramref name="font"/> additionally carries a real <c>VORG</c> table
        /// (<see cref="RFont.HasVerticalOrigin"/> - issue #775), the anchor is nudged by
        /// <see cref="RFont.GetVerticalOriginY"/> instead of staying at the plain top-of-cell position:
        /// <see cref="RGraphics.DrawString(string, RFont, RColor, RPoint, RSize, double, RFontPalette?, TextShapingFeatures?)"/> always renders <paramref name="font"/>'s baseline at
        /// <c>point.Y + font.Ascent</c> (traced through <c>XGraphicsPdfRenderer.DrawString</c>'s own
        /// <c>cyAscent</c> shift, which uses the exact same <c>Ascender</c> field <see cref="RFont.Ascent"/>
        /// is built from), while the OpenType spec defines a glyph's vertical origin as a baseline-relative,
        /// Y-up design-space coordinate - so placing that origin at this cell's own pen position
        /// (<c>rect.Y + offset</c>, the same position the advance/clip above already treat as "where this
        /// glyph's vertical origin belongs," per the spec's own "advance height starts from the vertical
        /// origin" wording) means solving <c>point.Y + Ascent = (rect.Y+offset) + originY</c> for
        /// <c>point.Y</c>: <c>y = (rect.Y+offset) + (originY - Ascent)</c>, added, not subtracted. (An
        /// earlier attempt at this exact shift used the opposite sign and was reverted for visibly
        /// cropping every glyph - the derivation above is what the corrected version follows.) Gated on
        /// <see cref="RFont.HasVerticalOrigin"/> rather than <see cref="RFont.HasVerticalMetrics"/>
        /// because a font with vmtx/vhea but no real VORG only offers <c>vhea.ascent</c> as a Y fallback,
        /// a value not designed to mean "vertical origin" the way a real VORG entry is - extending this
        /// shift to that weaker signal is out of scope here.
        ///
        /// The clip window itself is deliberately **not** shifted along with the anchor - it stays pinned
        /// to <c>[rect.Y + offset, +advance]</c>, the same unshifted per-cell reservation
        /// <see cref="CssLayoutEngine.NaturalWordSize"/> computed at layout time. That reservation, not the
        /// origin-adjusted anchor, is what actually prevents this character's ink from bleeding into its
        /// neighbors' cells - shifting the clip to track the anchor would just relocate the bleed risk
        /// (open up the *other* edge) rather than remove it. A real, self-consistent <c>VORG</c> table
        /// keeps a glyph's ink within its own reserved cell once correctly positioned by construction (the
        /// spec's own `vertOriginY = topSideBearing + yMax` definition ties the origin to the ink's own
        /// top edge, and advance is measured downward from that same origin) - confirmed visually (PDFium
        /// and MuPDF) against a deliberately mismatched synthetic fixture whose shift crops only the empty
        /// margin above a glyph's cap-height, never real ink. Do not "fix" this by shifting the clip to
        /// track the anchor without re-verifying against real rendered output first.
        /// </remarks>
        private static void PaintUprightVerticalRun(RGraphics g, string text, RFont font, CssBox styleSource, RRect rect, double baselineAdjust, TextShapingFeatures wordFeatures, string? logicalText = null)
        {
            double offset = 0;
            var hasVerticalMetrics = font.HasVerticalMetrics;
            var hasVerticalOrigin = font.HasVerticalOrigin;

            // Each character here paints as its own single-glyph DrawString call (unlike the horizontal/
            // sideways branches' one whole-word call), so the whole-word logicalText (mismatched length
            // against a 1-character text) can't be handed through as-is - CMapInfo.AddShapedText's remap
            // needs a per-character logical source instead. logicalText is already positionally aligned
            // with `text` (see CMapInfo.AddShapedText's own remarks) - the i-th rune painted from `text`
            // corresponds directly to logicalText's i-th rune, no reversal needed here.
            Rune[]? logicalRunes = logicalText != null && logicalText.Length == text.Length && logicalText != text
                ? logicalText.EnumerateRunes().ToArray()
                : null;

            var index = 0;
            foreach (var (charText, rune, charSize) in CssLayoutEngine.MeasureUprightRunCharacters(g, text, font, wordFeatures))
            {
                var x = rect.X + Math.Max(0, (rect.Width - charSize.Width) / 2);
                var y = rect.Y + offset + baselineAdjust;
                if (hasVerticalOrigin)
                    y += font.GetVerticalOriginY(rune) - font.Ascent;
                var advance = hasVerticalMetrics ? font.GetVerticalAdvance(rune) : font.Height;
                var charLogicalText = logicalRunes != null ? logicalRunes[index].ToString() : null;

                // A VORG-shifted anchor can push the painted span past the reserved cell even when the
                // line-height fallback advance is in play (its "advance already equals the full painted
                // span" guarantee assumes an unshifted anchor) - so the clip is needed whenever either
                // real data source is active, not just when HasVerticalMetrics narrowed the advance.
                if (hasVerticalMetrics || hasVerticalOrigin)
                {
                    g.PushClip(new RRect(rect.X, rect.Y + offset, rect.Width, advance));
                    g.DrawString(charText, font, styleSource.ActualColor, new RPoint(x, y), charSize,
                        styleSource.ActualLetterSpacing, styleSource.ActualFontPalette, wordFeatures, charLogicalText);
                    g.PopClip();
                }
                else
                {
                    g.DrawString(charText, font, styleSource.ActualColor, new RPoint(x, y), charSize,
                        styleSource.ActualLetterSpacing, styleSource.ActualFontPalette, wordFeatures, charLogicalText);
                }

                offset += advance + styleSource.ActualLetterSpacing;
                index++;
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
