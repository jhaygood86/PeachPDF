using PeachPDF;
using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Handlers;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Text.Bidi;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PeachPDF.Html.Core.Dom
{
    internal static class MarginBoxRenderer
    {
        /// <summary>
        /// Renders every applicable margin box. <paramref name="margins"/> is the effective, already
        /// cascade-merged set of margin-box declarations for this page (see
        /// <see cref="PdfGenerator.SelectApplicableMarginRules"/>) — not necessarily all from the same
        /// <c>@page</c> rule, since <c>@page</c> rules cascade per-declaration like any other CSS.
        /// <paramref name="pageStyle"/> is the page context's own top-level declarations (see
        /// <see cref="PdfGenerator.SelectApplicablePageStyle"/>), consulted as a font-property
        /// inheritance fallback for margin boxes that don't set their own <c>font-family</c>/
        /// <c>font-size</c>/<c>font-weight</c>/<c>font-style</c> — per CSS Paged Media, margin boxes
        /// inherit these from their page context.
        /// </summary>
        public static async Task Render(
            XGraphics g,
            XSize pageSize,
            double marginLeft,
            double marginTop,
            double marginRight,
            double marginBottom,
            IReadOnlyList<MarginStyleRule> margins,
            int pageNumber,
            int totalPages,
            double pageY,
            IReadOnlyList<NamedString> namedStrings,
            RAdapter adapter,
            StyleDeclaration? pageStyle,
            HtmlContainerInt htmlContainer,
            Dictionary<string, CssImage?> imageCache,
            Dictionary<string, IReadOnlyList<CssImage>?> backgroundImageCache)
        {
            foreach (var marginRule in margins)
            {
                var boxName = marginRule.Selector?.Text?.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(boxName))
                    continue;

                var contentValue = marginRule.Style.Content;

                // content: element() (css-gcpm-3) is handled entirely by HtmlContainerInt's own margin-box
                // layout phase - a real CssBox subtree needs real layout, which this text/image-only
                // pipeline cannot give it - and painted (background/border included) from
                // PdfGenerator.PaintElementMarginBoxes instead.
                if (!string.IsNullOrEmpty(contentValue) && TryParseElementFunction(contentValue, out _, out _))
                    continue;

                var remPt = htmlContainer.PageLengthContext?.RemPt ?? DefaultFontResolver.FontSize;
                var outerRect = GetMarginBoxRect(boxName, pageSize, marginLeft, marginTop, marginRight, marginBottom, margins,
                    pageStyle, remPt);
                var cbWidth = MarginAreaWidth(boxName, pageSize, marginLeft, marginRight);
                var cbHeight = MarginAreaHeight(boxName, pageSize, marginTop, marginBottom);

                // Background/border paint unconditionally (a margin box can have a decorative background
                // with no content at all) - gated on their own border-box rect rather than the content
                // rect below, so a box whose content is empty/none still gets them.
                await PaintBackgroundAndBorder(g, outerRect, pageSize, marginRule, pageStyle, remPt,
                    cbWidth, cbHeight, adapter, htmlContainer, backgroundImageCache);

                if (string.IsNullOrEmpty(contentValue) ||
                    contentValue.Equals("none", StringComparison.OrdinalIgnoreCase) ||
                    contentValue.Equals("normal", StringComparison.OrdinalIgnoreCase))
                    continue;

                var rect = ApplyBoxModel(outerRect, marginRule, pageStyle, remPt, cbWidth, cbHeight);
                if (rect.Width <= 0 || rect.Height <= 0)
                    continue;

                var image = await ResolveContentImage(contentValue, adapter, htmlContainer, imageCache);
                if (image != null)
                {
                    var position = ResolveImagePosition(marginRule.Style, pageStyle, boxName);
                    // Only a gradient's em/ex/ch stop-position/explicit-radius resolution ever consults
                    // this (issue #827) - skip it for a plain url()/SVG image, which never does.
                    var emSizePt = image is CssImage.LinearGradient or CssImage.RadialGradient
                        ? ResolveFontSizePt(marginRule.Style, pageStyle)
                        : (double?)null;
                    PaintImage(g, image, rect, position, adapter, htmlContainer, emSizePt);
                    continue;
                }

                // pageY is internal-pixel document space, the same space NamedString Ys are registered
                // in - deriving page attribution from the point-space pageSize/margins here used to
                // skew it whenever ShrinkToFit made PixelsPerPoint diverge from 1.0.
                var text = ResolveContent(contentValue, pageNumber, totalPages, pageY, htmlContainer, namedStrings);
                if (text == null)
                    continue;

                var font = BuildFont(marginRule.Style, pageStyle, adapter);
                var brush = BuildBrush(marginRule.Style);
                var format = BuildStringFormat(marginRule.Style, pageStyle, boxName);

                var visualText = ResolveBidiText(text, marginRule.Style, pageStyle, out var logicalText);
                g.DrawString(visualText, font, brush, rect, format, logicalText: logicalText);
            }
        }

        /// <summary>
        /// A margin box's own margin and padding on one axis, resolved against
        /// <paramref name="basisPt"/>. One implementation because the two callers need it in
        /// opposite directions: <see cref="GetMarginBoxRect"/> ADDS it, since a declared
        /// <c>width</c>/<c>height</c> is the CONTENT-box dimension and the slot it distributes is
        /// therefore the outer size (css-page-3 §5.3.2/§5.3.3's fixed-dimension equality:
        /// <c>margin + border + padding + width + padding + border + margin = the margin area's
        /// extent</c>), and <see cref="ApplyBoxModel"/> SUBTRACTS the same amount back to reach the
        /// content box the caller paints into.
        ///
        /// <paramref name="basisPt"/> is the CONTAINING BLOCK's extent along this axis — its WIDTH for
        /// the left/right edges, its HEIGHT for the top/bottom ones. css-page-3 §6 overrides CSS 2.1
        /// §8.3/§8.4 here, which resolve every edge against the containing block's width: "For right
        /// and left values, percentages are relative to the width of the containing block; for top and
        /// bottom values, percentages are relative to the height of the containing block."
        ///
        /// The containing block is the page-margin box's own slot in the margin area (§5.3.1): for a
        /// top or bottom row, the content band's width by the used page margin's thickness; for a left
        /// or right column, that margin's thickness by the content band's height; and for a corner, the
        /// rectangle where the two page margins meet. <see cref="MarginAreaWidth"/> and
        /// <see cref="MarginAreaHeight"/> resolve those two dimensions from the box's name.
        ///
        /// Padding may not be negative (CSS 2.1 §8.4); a margin may be, which is how a footer band
        /// on a page margin shallower than the inset it wants grows back over the page the way a
        /// browser's print-footer overlay does.
        ///
        /// Composed from <see cref="MarginExtent"/>, <see cref="BorderExtent(MarginStyleRule,StyleDeclaration,double,bool)"/> and
        /// <see cref="PaddingExtent(MarginStyleRule,StyleDeclaration,double,double,bool)"/> (closing #943 - border is now part of this, resolved and
        /// charged exactly like margin/padding already were) so <see cref="ApplyBoxModel"/> and
        /// <see cref="GetMarginBoxRect"/>'s own <c>Outer</c> pick it up with no further change.
        /// </summary>
        internal static (double Start, double End) BoxModelExtent(
            MarginStyleRule rule, StyleDeclaration? pageStyle, double remPt, double basisPt, bool horizontal)
        {
            var (marginStart, marginEnd) = MarginExtent(rule, pageStyle, remPt, basisPt, horizontal);
            var (borderStart, borderEnd) = BorderExtent(rule, pageStyle, remPt, horizontal);
            var (paddingStart, paddingEnd) = PaddingExtent(rule, pageStyle, remPt, basisPt, horizontal);
            return (marginStart + borderStart + paddingStart, marginEnd + borderEnd + paddingEnd);
        }

        /// <summary>
        /// The margin half of <see cref="BoxModelExtent"/>, split out so a caller that needs only the
        /// border-box rect (background/border painting - see <see cref="ApplyMarginOnly"/>) doesn't have
        /// to resolve padding/border too. Unlike padding, a margin may be negative (CSS 2.1 §8.4) - see
        /// <see cref="BoxModelExtent"/>'s own remarks on why.
        /// </summary>
        internal static (double Start, double End) MarginExtent(
            MarginStyleRule rule, StyleDeclaration? pageStyle, double remPt, double basisPt, bool horizontal)
        {
            var style = rule.Style;
            var emPt = ResolveFontSizePt(style, pageStyle);

            double Len(string? value) => string.IsNullOrWhiteSpace(value)
                ? 0
                : DomParser.ParseLengthToPdfPoints(value, new PageLengthContext(emPt, remPt, basisPt)) ?? 0;

            return horizontal
                ? (Len(style.MarginLeft), Len(style.MarginRight))
                : (Len(style.MarginTop), Len(style.MarginBottom));
        }

        /// <summary>
        /// The padding half of <see cref="BoxModelExtent"/>, split out so <see cref="ApplyMarginAndBorder"/>
        /// (the padding-box rect - a <c>background-origin</c>/<c>-clip: padding-box</c> layer's own
        /// positioning/clip area, and the default per CSS Backgrounds 3 §3.9/§3.10) can stop one step
        /// short of it. Clamped to zero, unlike margin - CSS 2.1 §8.4 disallows a negative padding.
        /// </summary>
        internal static (double Start, double End) PaddingExtent(
            MarginStyleRule rule, StyleDeclaration? pageStyle, double remPt, double basisPt, bool horizontal)
            => PaddingExtent(rule.Style, pageStyle, remPt, basisPt, horizontal);

        /// <summary>
        /// The <see cref="StyleDeclaration"/>-based overload <see cref="PaddingExtent(MarginStyleRule,StyleDeclaration,double,double,bool)"/>
        /// delegates to - see that overload's own remarks and <see cref="BorderExtent(StyleDeclaration,StyleDeclaration,double,bool)"/>'s
        /// (issue #1147).
        /// </summary>
        internal static (double Start, double End) PaddingExtent(
            StyleDeclaration style, StyleDeclaration? pageStyle, double remPt, double basisPt, bool horizontal)
        {
            var emPt = ResolveFontSizePt(style, pageStyle);

            double Len(string? value) => string.IsNullOrWhiteSpace(value)
                ? 0
                : Math.Max(0, DomParser.ParseLengthToPdfPoints(value, new PageLengthContext(emPt, remPt, basisPt)) ?? 0);

            return horizontal
                ? (Len(style.PaddingLeft), Len(style.PaddingRight))
                : (Len(style.PaddingTop), Len(style.PaddingBottom));
        }

        /// <summary>
        /// A margin box's own border width per axis (css-page-3 §5.1's whole box model - closes #943).
        /// Unlike <see cref="MarginExtent"/>/<see cref="PaddingExtent(MarginStyleRule,StyleDeclaration,double,double,bool)"/>, <c>border-*-width</c> never
        /// accepts a percentage, so this needs no containing-block basis, only the box's own em (for a
        /// value like <c>0.1em</c>) via <see cref="ResolveBorderWidthPt"/>, which also treats
        /// <c>border-*-style: none</c>/<c>hidden</c> as zero width regardless of any declared
        /// <c>border-*-width</c> (CSS 2.1 §8.5.3).
        /// </summary>
        internal static (double Start, double End) BorderExtent(
            MarginStyleRule rule, StyleDeclaration? pageStyle, double remPt, bool horizontal)
            => BorderExtent(rule.Style, pageStyle, remPt, horizontal);

        /// <summary>
        /// The <see cref="StyleDeclaration"/>-based overload <see cref="BorderExtent(MarginStyleRule,StyleDeclaration,double,bool)"/>
        /// delegates to - split out so a caller with no <see cref="MarginStyleRule"/> wrapper (the
        /// <c>@page</c> box's own border, resolved from its per-declaration-merged page style rather
        /// than a single margin-box rule - see <see cref="PageRuleResolver.ResolvePageBorderAndPadding"/>)
        /// can reuse the identical box-model math instead of re-deriving it (issue #1147).
        /// </summary>
        internal static (double Start, double End) BorderExtent(
            StyleDeclaration style, StyleDeclaration? pageStyle, double remPt, bool horizontal)
        {
            var emPt = ResolveFontSizePt(style, pageStyle);

            double Width(string? widthValue, string? styleValue) => ResolveBorderWidthPt(widthValue, styleValue, emPt, remPt);

            return horizontal
                ? (Width(style.BorderLeftWidth, style.BorderLeftStyle), Width(style.BorderRightWidth, style.BorderRightStyle))
                : (Width(style.BorderTopWidth, style.BorderTopStyle), Width(style.BorderBottomWidth, style.BorderBottomStyle));
        }

        /// <summary>
        /// Resolves one margin-box border edge's width in points. In practice
        /// <paramref name="widthValue"/> is never the bare <c>thin</c>/<c>medium</c>/<c>thick</c>
        /// keyword by the time it reaches here - the <c>border-*-width</c> CSS-OM property already
        /// resolves those to their UA-default pixel lengths (<see cref="Length.Thin"/>/
        /// <see cref="Length.Medium"/>/<see cref="Length.Thick"/>: <c>1px</c>/<c>3px</c>/<c>5px</c>,
        /// confirmed empirically) at declaration-set time, the same as it does for a normal element's
        /// <c>border-*-width</c> (which is why <see cref="CssValueParser.GetActualBorderWidth"/>'s own
        /// hardcoded thin/medium/thick branch is equally unreachable there) - so those three keyword
        /// cases below are a defensive fallback, not the common path; the ordinary length parse handles
        /// every real declaration.
        /// <c>border-*-style</c>'s own initial value is <c>none</c> (CSS 2.1 §8.5.3), so an
        /// <em>unset</em> style computes to <c>none</c> exactly like an explicit one - both force width
        /// to zero regardless of any declared <c>border-*-width</c>, the same as <c>hidden</c>.
        /// </summary>
        internal static double ResolveBorderWidthPt(string? widthValue, string? styleValue, double emPt, double remPt)
        {
            if (string.IsNullOrWhiteSpace(styleValue) ||
                string.Equals(styleValue, Keywords.None, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(styleValue, Keywords.Hidden, StringComparison.OrdinalIgnoreCase))
                return 0;

            var value = string.IsNullOrWhiteSpace(widthValue) ? Keywords.Medium : widthValue;
            return value switch
            {
                Keywords.Thin => Length.Thin.Value * Length.PointsPerPx,
                Keywords.Medium => Length.Medium.Value * Length.PointsPerPx,
                Keywords.Thick => Length.Thick.Value * Length.PointsPerPx,
                _ => Math.Max(0, DomParser.ParseLengthToPdfPoints(value, new PageLengthContext(emPt, remPt, 0)) ?? 0)
            };
        }

        /// <summary>
        /// Applies a margin box's margin and padding to its rect, so <c>content</c> is painted in the
        /// content box rather than the border box. Per css-page-3 §5.1 a margin box is a block-level
        /// box that accepts the whole box model, and <see cref="GetMarginBoxRect"/> already resolves
        /// <c>width</c>/<c>height</c> from the same declaration; margin and padding were simply never
        /// read.
        ///
        /// It matters because a text margin box is painted with a single <c>DrawString</c> into this
        /// rect, so <c>vertical-align</c> offers exactly three positions within it and nothing else
        /// moves the content -- <c>line-height</c> included. On a 1.5in bottom margin those three are
        /// 98pt, 49pt and 0pt from the page edge, and a page number that has to land 15pt from the
        /// edge (which is where a browser's print footer sits) cannot be expressed at all. A negative
        /// margin covers the other end of the same problem: a browser's footer band is an overlay over
        /// the page rather than a box inside the page margin, so it keeps that 15pt inset even on a
        /// margin thinner than 15pt, which a margin box can only follow by growing past its band.
        ///
        /// This subtracts exactly what <see cref="GetMarginBoxRect"/> added for an explicitly sized
        /// box, so a declared <c>width</c> survives as the content width rather than being charged
        /// for its own padding twice. For an auto-sized box the slot really was an outer allocation,
        /// and subtracting is what turns it into the content box.
        /// </summary>
        /// <param name="rect">the box's slot, as <see cref="GetMarginBoxRect"/> allocated it</param>
        /// <param name="rule">the <c>@page</c> margin-box rule whose box model to apply</param>
        /// <param name="pageStyle">the base <c>@page</c> declaration, for the em basis</param>
        /// <param name="remPt">the root element's font size, in points</param>
        /// <param name="containingBlockWidthPt">
        /// The width of this box's containing block — the percentage basis for the left and right
        /// edges. See <see cref="MarginAreaWidth"/>.
        /// </param>
        /// <param name="containingBlockHeightPt">
        /// The height of this box's containing block — the percentage basis for the top and bottom
        /// edges, which is a DIFFERENT number per css-page-3 §6. See <see cref="MarginAreaHeight"/>.
        /// </param>
        internal static XRect ApplyBoxModel(XRect rect, MarginStyleRule rule, StyleDeclaration? pageStyle,
            double remPt, double containingBlockWidthPt, double containingBlockHeightPt)
        {
            var (left, right) = BoxModelExtent(rule, pageStyle, remPt, containingBlockWidthPt, horizontal: true);
            var (top, bottom) = BoxModelExtent(rule, pageStyle, remPt, containingBlockHeightPt, horizontal: false);
            return Shrink(rect, left, top, right, bottom);
        }

        /// <summary>
        /// Shrinks <paramref name="rect"/> to the margin box's own border-box - its outer slot
        /// (<see cref="GetMarginBoxRect"/>) minus its own margin only, per <see cref="MarginExtent"/>.
        /// The background/border paint rect: background's <c>background-origin</c>/<c>-clip:
        /// border-box</c> layer positions/clips against exactly this, and border itself paints at this
        /// rect's own four edges (see <see cref="PaintBorder"/>).
        /// </summary>
        internal static XRect ApplyMarginOnly(XRect rect, MarginStyleRule rule, StyleDeclaration? pageStyle,
            double remPt, double containingBlockWidthPt, double containingBlockHeightPt)
        {
            var (left, right) = MarginExtent(rule, pageStyle, remPt, containingBlockWidthPt, horizontal: true);
            var (top, bottom) = MarginExtent(rule, pageStyle, remPt, containingBlockHeightPt, horizontal: false);
            return Shrink(rect, left, top, right, bottom);
        }

        /// <summary>
        /// Shrinks <paramref name="rect"/> to the margin box's own padding-box - its border-box
        /// (<see cref="ApplyMarginOnly"/>) minus its own border width. The default
        /// <c>background-origin</c>/<c>-clip</c> positioning/clip area (CSS Backgrounds 3 §3.9/§3.10) -
        /// distinct from the border-box now that a margin box's own border can be non-zero (#943).
        /// </summary>
        internal static XRect ApplyMarginAndBorder(XRect rect, MarginStyleRule rule, StyleDeclaration? pageStyle,
            double remPt, double containingBlockWidthPt, double containingBlockHeightPt)
        {
            var (marginLeft, marginRight) = MarginExtent(rule, pageStyle, remPt, containingBlockWidthPt, horizontal: true);
            var (marginTop, marginBottom) = MarginExtent(rule, pageStyle, remPt, containingBlockHeightPt, horizontal: false);
            var (borderLeft, borderRight) = BorderExtent(rule, pageStyle, remPt, horizontal: true);
            var (borderTop, borderBottom) = BorderExtent(rule, pageStyle, remPt, horizontal: false);
            return Shrink(rect, marginLeft + borderLeft, marginTop + borderTop, marginRight + borderRight, marginBottom + borderBottom);
        }

        /// <summary>
        /// Shared box-model rect shrink - <see cref="ApplyBoxModel"/>/<see cref="ApplyMarginOnly"/>/
        /// <see cref="ApplyMarginAndBorder"/> differ only in which extents they resolve before calling
        /// this. <c>XRect</c> refuses a negative extent; a caller's own positive-size guard then skips
        /// the box, which is what an over-padded/over-bordered box should do anyway. Promoted to
        /// internal so <c>PdfGenerator</c> can derive the page box's own border-box/padding-box/
        /// content-box rects from the same primitive (issue #1147) instead of re-deriving the shrink
        /// arithmetic a second time.
        /// </summary>
        internal static XRect Shrink(XRect rect, double left, double top, double right, double bottom)
        {
            if (left == 0 && right == 0 && top == 0 && bottom == 0)
                return rect;

            return new XRect(rect.X + left, rect.Y + top,
                Math.Max(0, rect.Width - left - right),
                Math.Max(0, rect.Height - top - bottom));
        }

        /// <summary>
        /// The WIDTH of <paramref name="name"/>'s containing block (css-page-3 §5.3.1) — the basis a
        /// <c>margin-left</c>/<c>margin-right</c>/<c>padding-left</c>/<c>padding-right</c> percentage
        /// resolves against. A left or right column is as wide as that page margin; a corner is as wide
        /// as the margin it sits in; a top or bottom row spans the content band.
        /// </summary>
        internal static double MarginAreaWidth(string name, XSize page, double mL, double mR) => name switch
        {
            "top-left-corner" or "bottom-left-corner" or "left-top" or "left-middle" or "left-bottom" => mL,
            "top-right-corner" or "bottom-right-corner" or "right-top" or "right-middle" or "right-bottom" => mR,
            _ => page.Width - mL - mR,
        };

        /// <summary>
        /// The HEIGHT of <paramref name="name"/>'s containing block (css-page-3 §5.3.1) — the basis a
        /// <c>margin-top</c>/<c>margin-bottom</c>/<c>padding-top</c>/<c>padding-bottom</c> percentage
        /// resolves against, and a different number from <see cref="MarginAreaWidth"/> for every box.
        /// A top or bottom row is as tall as that page margin; a corner is as tall as the margin it sits
        /// in; a left or right column spans the content band's height.
        /// </summary>
        internal static double MarginAreaHeight(string name, XSize page, double mT, double mB) => name switch
        {
            "top-left-corner" or "top-right-corner" or "top-left" or "top-center" or "top-right" => mT,
            "bottom-left-corner" or "bottom-right-corner" or "bottom-left" or "bottom-center" or "bottom-right" => mB,
            _ => page.Height - mT - mB,
        };

        /// <summary>
        /// Detects and resolves an <c>&lt;image&gt;</c>-valued <c>content</c> (a bare <c>url()</c>, or
        /// one of the gradient functions) on a margin box - per CSS Paged Media Level 3 §7, a margin
        /// box's <c>content</c> accepts an <c>&lt;image&gt;</c> value, not just text/counter/string
        /// content. Mirrors the same first-token image detection <see cref="CssContentEngine.ApplyContent"/>
        /// uses for in-flow <c>content</c> (an image value makes the whole declaration an image, not
        /// text to append to) via the shared <see cref="CssContentEngine.IsGradientFunctionName"/>.
        /// Loaded images are cached by their raw declaration text across the whole document render,
        /// since the same margin-box rule (and so the same image) repeats identically on every page.
        /// </summary>
        internal static async Task<CssImage?> ResolveContentImage(
            string contentValue,
            RAdapter adapter,
            HtmlContainerInt htmlContainer,
            Dictionary<string, CssImage?> imageCache)
        {
            // A ref struct (PooledTokenList) local can't live in an async method's own body under this
            // project's net8.0 C# 12 language version (CS9202 - relaxed only in C# 13+), so the pooled
            // tokenization is isolated in this local function, which compiles as its own ordinary method
            // rather than becoming part of the async state machine. An empty token list and a
            // not-image-content first token both fall through to the same "return null" outcome below,
            // so collapsing both into a single false is behavior-preserving.
            static bool IsImageContent(string value)
            {
                using var pooledTokens = CssValueParser.GetCssTokensPooled(value);
                List<Token> tokens = pooledTokens;
                if (tokens.Count == 0)
                    return false;

                var first = tokens[0];
                return first.Type == TokenType.Url ||
                    (first is { Type: TokenType.Function } ft && CssContentEngine.IsGradientFunctionName(ft.Data));
            }

            if (!IsImageContent(contentValue))
                return null;

            if (imageCache.TryGetValue(contentValue, out var cached))
                return cached;

            var image = new CssValueParser(adapter).ParseImage(contentValue);
            if (image != null)
                await image.EnsureLoadedAsync(htmlContainer);

            imageCache[contentValue] = image;
            return image;
        }

        /// <summary>
        /// Paints one margin box's own <c>background</c> then <c>border</c>, in that order - matching
        /// normal CSS box painting order and #943's own suggested placement ("between the background
        /// fill and the content"). <paramref name="outerRect"/> is the box's slot as
        /// <see cref="GetMarginBoxRect"/> allocated it (unconditionally, even for a box with no
        /// <c>content</c> at all - a margin box can be a purely decorative band). Background layers pick
        /// between the border-box (<see cref="ApplyMarginOnly"/>), padding-box
        /// (<see cref="ApplyMarginAndBorder"/>) and content-box (<see cref="ApplyBoxModel"/>) rects per
        /// their own <c>background-origin</c>/<c>-clip</c> value via
        /// <see cref="LayeredBackgroundPainter.PaintAsync"/> - the same shared primitive the <c>@page</c>
        /// box's own background uses (see that class's own remarks). Border paints as four independent
        /// edge strokes rather than a mitred box (<see cref="PaintBorder"/>) - a margin box never
        /// fragments across pages, so unlike a real <c>CssBox</c>'s border it has no shared corner with a
        /// neighboring fragment to mitre into (#943's own flagged open question).
        /// </summary>
        internal static async Task PaintBackgroundAndBorder(
            XGraphics g, XRect outerRect, XSize pageSize, MarginStyleRule rule, StyleDeclaration? pageStyle,
            double remPt, double containingBlockWidthPt, double containingBlockHeightPt,
            RAdapter adapter, HtmlContainerInt htmlContainer,
            Dictionary<string, IReadOnlyList<CssImage>?> backgroundImageCache)
        {
            // Root is only ever null before the document's initial layout, which has already run by the
            // time page rendering (and so margin-box painting) begins - null here would mean there's no
            // laid-out document at all to be generating a PDF page for (see PaintImage's own remarks,
            // which this mirrors for consistency between the two).
            if (htmlContainer.Root is not { } rootBox)
                return;

            var borderBoxRect = ApplyMarginOnly(outerRect, rule, pageStyle, remPt, containingBlockWidthPt, containingBlockHeightPt);
            if (borderBoxRect.Width <= 0 || borderBoxRect.Height <= 0)
                return;

            // Built by shrinking borderBoxRect further (border, then padding) rather than independently
            // re-deriving margin+border(+padding) from outerRect the way the public ApplyMarginAndBorder/
            // ApplyBoxModel do - those stay as their own tested, single-purpose API for a caller that only
            // needs one rect (e.g. Render's content-path ApplyBoxModel call below), but resolving all
            // three margin-box rects here from the same outer slot would otherwise re-parse the same
            // margin/border/padding declarations up to three times over.
            var emPt = ResolveFontSizePt(rule.Style, pageStyle);
            var (borderLeft, borderRight) = BorderExtent(rule, pageStyle, remPt, horizontal: true);
            var (borderTop, borderBottom) = BorderExtent(rule, pageStyle, remPt, horizontal: false);
            var paddingBoxRect = Shrink(borderBoxRect, borderLeft, borderTop, borderRight, borderBottom);

            var (paddingLeft, paddingRight) = PaddingExtent(rule, pageStyle, remPt, containingBlockWidthPt, horizontal: true);
            var (paddingTop, paddingBottom) = PaddingExtent(rule, pageStyle, remPt, containingBlockHeightPt, horizontal: false);
            var contentBoxRect = Shrink(paddingBoxRect, paddingLeft, paddingTop, paddingRight, paddingBottom);

            var pixelsPerPoint = (adapter as PdfSharpAdapter)?.PixelsPerPoint ?? 1.0;
            using var graphicsAdapter = new GraphicsAdapter(adapter, g, pixelsPerPoint);

            RRect ToPixelRect(XRect r) => new(r.X * pixelsPerPoint, r.Y * pixelsPerPoint, r.Width * pixelsPerPoint, r.Height * pixelsPerPoint);

            var borderBoxPx = ToPixelRect(borderBoxRect);
            var paddingBoxPx = ToPixelRect(paddingBoxRect);
            var contentBoxPx = ToPixelRect(contentBoxRect);
            // background-attachment: fixed's positioning area is the page box, the same paginated-media
            // convention the canvas (html/body) background already uses - not this one box's own rect.
            var pageBoxPx = new RRect(0, 0, pageSize.Width * pixelsPerPoint, pageSize.Height * pixelsPerPoint);

            RRect ResolvePositioningRect(string value) => value switch
            {
                Keywords.ContentBox => contentBoxPx,
                Keywords.BorderBox => borderBoxPx,
                // padding-box is the initial value (CSS Backgrounds 3 §3.9/§3.10) and what an
                // unrecognized/empty value falls back to.
                _ => paddingBoxPx,
            };

            await LayeredBackgroundPainter.PaintAsync(
                graphicsAdapter, rule.Style, adapter, htmlContainer, rootBox, emPt,
                ResolvePositioningRect, pageBoxPx, backgroundImageCache);

            PaintBorder(graphicsAdapter, borderBoxPx, rule.Style, emPt, remPt, pixelsPerPoint, adapter);
        }

        /// <summary>
        /// Paints a margin box's own border as four independent edge strokes at
        /// <paramref name="borderBoxRect"/>'s own edges (full width/height each, so adjacent edges
        /// overlap at the corners rather than mitre into each other - see
        /// <see cref="PaintBackgroundAndBorder"/>'s own remarks on why that's the right call here), via
        /// <see cref="BordersDrawHandler.DrawCollapsedSegment"/> - the same value-based, no-<c>CssBox</c>
        /// primitive already used for collapsed table borders. <c>border-color</c>'s own initial value
        /// is <c>currentcolor</c> (CSS Backgrounds 3 §4), so an edge with no explicit color (or an
        /// explicit <c>currentcolor</c>) resolves against the box's own already-resolved text
        /// <c>color</c>, defaulting to black exactly as <see cref="BuildBrush"/> already does for text
        /// with no <c>color</c> of its own.
        /// Internal (not private) so <c>PdfGenerator</c> can reuse it for the page box's own border - a
        /// distinct paint layer from a page-<em>margin</em>-box's border (this method still paints those
        /// too, via <see cref="PaintBackgroundAndBorder"/>) but identical box-model math (issue #1147).
        /// </summary>
        internal static void PaintBorder(RGraphics g, RRect borderBoxRect, StyleDeclaration style,
            double emPt, double remPt, double pixelsPerPoint, RAdapter adapter)
        {
            // borderBoxRect is already known positive-size here - the sole caller, PaintBackgroundAndBorder,
            // returns before this call otherwise. emPt is the same em-basis BorderExtent already used to
            // charge this box's own space - a border-*-width of "0.1em" must paint at the identical width
            // it was charged, or content would sit under (or float above) the stroke it made room for.
            var colorParser = new CssValueParser(adapter);
            var textColor = string.IsNullOrEmpty(style.Color) ? RColor.Black : colorParser.GetActualColor(style.Color);

            // An edge with no explicit colour resolves currentColor exactly as CssUtils.ApplyCurrentColor
            // does for an ordinary box: against the text colour normally, but against the fixed light
            // base when the edge is bevelled - a page box is never a table display type, so it always
            // takes that base. Without the second arm an `@page { border: 20px inset }` would shade
            // black text into a bevel the identical declaration on a <div> no longer produces.
            //
            // The literal "initial" counts as "no explicit colour" here: an omitted slot of a `border`/
            // `border-top` shorthand is exported as that sentinel (ShorthandProperty.Export, and see
            // OptionValueConverter), and border-*-color's initial value IS currentcolor. Resolving it
            // through GetActualColor instead yields black - which is how `border-top: 4pt inset` came
            // out a shade of black on a margin box while the identical declaration on a <div>, whose
            // cascade path leaves the longhand at `currentcolor`, came out the two greys.
            RColor ResolveBorderColor(string? colorValue, LineStyle lineStyle) =>
                string.IsNullOrWhiteSpace(colorValue) ||
                colorValue.Equals(Keywords.CurrentColor, StringComparison.OrdinalIgnoreCase) ||
                colorValue.Equals(Keywords.Initial, StringComparison.OrdinalIgnoreCase)
                    ? BorderBevelColors.IsBeveled(lineStyle) ? BorderBevelColors.CurrentColorBase : textColor
                    : colorParser.GetActualColor(colorValue);

            void PaintEdge(bool isHorizontal, RRect edgeRect, double widthPx, string? styleValue, string? colorValue)
            {
                if (!Map.LineStyles.TryGetValue(styleValue ?? string.Empty, out var lineStyle))
                    lineStyle = LineStyle.None;

                if (widthPx <= 0 || lineStyle is LineStyle.None or LineStyle.Hidden)
                    return;

                BordersDrawHandler.DrawCollapsedSegment(g, isHorizontal, edgeRect, lineStyle, ResolveBorderColor(colorValue, lineStyle), widthPx);
            }

            var topWidthPx = ResolveBorderWidthPt(style.BorderTopWidth, style.BorderTopStyle, emPt, remPt) * pixelsPerPoint;
            var bottomWidthPx = ResolveBorderWidthPt(style.BorderBottomWidth, style.BorderBottomStyle, emPt, remPt) * pixelsPerPoint;
            var leftWidthPx = ResolveBorderWidthPt(style.BorderLeftWidth, style.BorderLeftStyle, emPt, remPt) * pixelsPerPoint;
            var rightWidthPx = ResolveBorderWidthPt(style.BorderRightWidth, style.BorderRightStyle, emPt, remPt) * pixelsPerPoint;

            PaintEdge(true, new RRect(borderBoxRect.Left, borderBoxRect.Top, borderBoxRect.Width, topWidthPx),
                topWidthPx, style.BorderTopStyle, style.BorderTopColor);
            PaintEdge(true, new RRect(borderBoxRect.Left, borderBoxRect.Bottom - bottomWidthPx, borderBoxRect.Width, bottomWidthPx),
                bottomWidthPx, style.BorderBottomStyle, style.BorderBottomColor);
            PaintEdge(false, new RRect(borderBoxRect.Left, borderBoxRect.Top, leftWidthPx, borderBoxRect.Height),
                leftWidthPx, style.BorderLeftStyle, style.BorderLeftColor);
            PaintEdge(false, new RRect(borderBoxRect.Right - rightWidthPx, borderBoxRect.Top, rightWidthPx, borderBoxRect.Height),
                rightWidthPx, style.BorderRightStyle, style.BorderRightColor);
        }

        /// <summary>
        /// Paints a margin box's image content via the same <see cref="CssImagePainter"/>/
        /// <see cref="BackgroundImageDrawHandler"/> pipeline used for in-flow <c>content: url(...)</c>
        /// (<c>FragmentPainter.PaintContentImage</c>) - natural size, aligned within the box per
        /// <paramref name="positionList"/> (a CSS <c>background-position</c> value derived from the
        /// box's resolved <c>text-align</c>/<c>vertical-align</c>, so an image follows the same
        /// alignment its text content would - CSS Paged Media Level 3 §7.2), clipped to the box, no
        /// repeat. <paramref name="g"/> paints in raw, unshrunk PDF-point space (see <see cref="BuildFont"/>'s
        /// own doc comment), so it's wrapped in a <see cref="GraphicsAdapter"/> with the rect
        /// pre-multiplied by <c>PixelsPerPoint</c> to cancel that adapter's own division back out.
        /// <paramref name="htmlContainer"/>'s root box stands in for the (non-existent, for a margin
        /// box) owning <c>CssBox</c> that <see cref="BackgroundLayerResolver"/> needs for potential
        /// em/rem length resolution - unreachable for the keyword position/"auto" values used here, but
        /// a real, already-laid-out box is a safe, defensible fallback if that ever changes. A
        /// gradient's own stop-position/explicit-radius <c>em</c>/<c>ex</c>/<c>ch</c> units (issues
        /// #823/#824) would otherwise resolve against this root box's font-size instead of the margin
        /// box's own declared font-size when the two differ (issue #827) - <paramref name="emSizePt"/>
        /// (the same <see cref="ResolveFontSizePt(StyleDeclaration,StyleDeclaration?)"/> resolution the
        /// margin box's own text content and its width/height em-basis already use, css-page-3 §8)
        /// overrides <see cref="CssBox.GetEmHeight"/> for that resolution specifically. <c>rem</c> is
        /// unaffected - it always resolves against the document root's font-size regardless of context,
        /// which <paramref name="htmlContainer"/>'s root box's own <see cref="CssBox.GetRemHeight"/>
        /// already correctly is.
        /// </summary>
        private static void PaintImage(XGraphics g, CssImage image, XRect rect, string positionList, RAdapter adapter,
            HtmlContainerInt htmlContainer, double? emSizePt)
        {
            // Root is only ever null before the document's initial layout, which has already run by
            // the time page rendering (and so margin-box painting) begins - null here would mean
            // there's no laid-out document at all to be generating a PDF page for.
            if (htmlContainer.Root is not { } rootBox)
                return;

            var pixelsPerPoint = (adapter as PdfSharpAdapter)?.PixelsPerPoint ?? 1.0;
            using var graphicsAdapter = new GraphicsAdapter(adapter, g, pixelsPerPoint);
            var paintRect = new RRect(rect.X * pixelsPerPoint, rect.Y * pixelsPerPoint, rect.Width * pixelsPerPoint, rect.Height * pixelsPerPoint);

            CssImagePainter.Paint(graphicsAdapter, image, layerIndex: 0,
                originRect: paintRect, clipRect: paintRect, roundedClipPath: null,
                positionList: positionList, sizeList: Keywords.Auto, repeatList: "no-repeat",
                attachmentList: Keywords.Scroll, viewportRect: paintRect, box: rootBox,
                drawBrush: brush =>
                {
                    graphicsAdapter.DrawRectangle(brush, paintRect.X, paintRect.Y, paintRect.Width, paintRect.Height);
                    brush.Dispose();
                },
                gradientEmSizePt: emSizePt);
        }

        /// <summary>
        /// Resolves a margin box's text <c>content</c> declaration - string literals, <c>counter()</c>/
        /// <c>counters()</c>, and <c>string()</c> - against this page's context. <c>string()</c>'s page
        /// attribution goes through <see cref="ResolveNamedString"/> via <see cref="HtmlContainerInt.SlotStartingAt"/>,
        /// the same table <paramref name="pageY"/> itself was derived from - see that method's own
        /// <c>pageIndexOf</c> doc for why a raw Y-range window isn't used here.
        /// </summary>
        internal static string? ResolveContent(
            string contentValue,
            int pageNumber,
            int totalPages,
            double pageY,
            HtmlContainerInt htmlContainer,
            IReadOnlyList<NamedString> namedStrings)
        {
            if (contentValue.Equals("none", StringComparison.OrdinalIgnoreCase))
                return null;

            using var pooledTokens = CssValueParser.GetCssTokensPooled(contentValue);
            List<Token> tokens = pooledTokens;
            var sb = new StringBuilder();

            foreach (var token in tokens)
            {
                switch (token)
                {
                    case { Type: TokenType.String } stringToken:
                        sb.Append(stringToken.Data);
                        break;
                    case { Type: TokenType.Function, Data: "counter" } counterToken:
                    {
                        var args = counterToken.ArgumentTokens
                            .Where(t => t.Type != TokenType.Whitespace)
                            .ToArray();
                        if (args.Length > 0 && args[0] is { Type: TokenType.Hash or TokenType.AtKeyword or TokenType.Ident } nameToken)
                        {
                            sb.Append(nameToken.Data.Equals("pages", StringComparison.OrdinalIgnoreCase)
                                ? totalPages.ToString()
                                : pageNumber.ToString());
                        }
                        break;
                    }
                    case { Type: TokenType.Function, Data: "string" } stringFunctionToken:
                    {
                        var args = stringFunctionToken.ArgumentTokens
                            .Where(t => t.Type != TokenType.Whitespace && t.Type != TokenType.Comma)
                            .ToArray();
                        if (args.Length > 0 && args[0] is { Type: TokenType.Hash or TokenType.AtKeyword or TokenType.Ident } nameToken)
                        {
                            var keyword = args.Length > 1 && args[1] is { Type: TokenType.Hash or TokenType.AtKeyword or TokenType.Ident } kw ? kw.Data.ToString() : "first";
                            var currentPageIndex = htmlContainer.SlotStartingAt(pageY);
                            sb.Append(ResolveNamedString(nameToken.Data.ToString(), keyword, currentPageIndex, htmlContainer.SlotStartingAt, namedStrings));
                        }
                        break;
                    }
                }
            }

            return sb.Length > 0 ? sb.ToString() : null;
        }

        /// <summary>
        /// Detects <c>content: element(&lt;name&gt; [, first|start|last|first-except]?)</c> (css-gcpm-3) -
        /// checked as an exclusive, first-checked whole-declaration match (mirroring how
        /// <see cref="ResolveContentImage"/>'s image detection already short-circuits before
        /// <see cref="ResolveContent"/>'s text resolution), since per spec <c>element()</c> cannot combine
        /// with any other <c>content</c> value.
        /// </summary>
        internal static bool TryParseElementFunction(
            string contentValue,
            [NotNullWhen(true)] out string? name,
            out string keyword)
        {
            name = null;
            keyword = "first";

            using var pooledTokens = CssValueParser.GetCssTokensPooled(contentValue);
            List<Token> tokens = pooledTokens;
            if (tokens is not [{ Type: TokenType.Function, Data: "element" } elementToken])
                return false;

            var args = elementToken.ArgumentTokens
                .Where(t => t.Type != TokenType.Whitespace && t.Type != TokenType.Comma)
                .ToArray();

            if (args.Length == 0 || args[0] is not { Type: TokenType.Ident } nameToken)
                return false;

            name = nameToken.Data.ToString();

            if (args.Length > 1)
            {
                if (args[1] is not { Type: TokenType.Ident } keywordToken)
                    return false;

                keyword = keywordToken.Data.ToString();
            }

            return true;
        }

        /// <summary>
        /// Resolves <c>string(&lt;name&gt;, first|start|last|first-except)</c> against the document-level
        /// <see cref="NamedString"/> list for the page starting at pagination slot <paramref name="currentPageIndex"/>.
        /// </summary>
        /// <param name="name">The <c>string-set</c> name (the <c>string()</c> function's first argument).</param>
        /// <param name="keyword">One of <c>first</c>/<c>start</c>/<c>last</c>/<c>first-except</c> (the
        /// <c>string()</c> function's optional second argument, defaulting to <c>first</c>).</param>
        /// <param name="currentPageIndex">The pagination slot of the page this margin box is being
        /// painted for, from the same <paramref name="pageIndexOf"/> table every candidate is measured
        /// against.</param>
        /// <param name="pageIndexOf">
        /// Maps a <see cref="NamedString"/>'s own document Y to the pagination slot that owns it - always
        /// <see cref="HtmlContainerInt.SlotStartingAt"/> in production, so every candidate is attributed to
        /// a page via the exact same authoritative table <paramref name="currentPageIndex"/> itself was
        /// derived from. This replaces an earlier design that compared a candidate's raw Y against
        /// <c>[pageY - epsilon, pageY + pageHeight + epsilon)</c> directly: two boxes at the very top of
        /// different columns on the *same* page can land on the same Y (both are "row 1" of their own
        /// column), and when that shared Y sits within a hairline of a page boundary, a symmetric epsilon
        /// widened at both ends of *every* page's window let a box on the far side of the boundary satisfy
        /// the near side's window too - observed against css4.pub's Icelandic dictionary, where a
        /// paragraph opening column 2 of one page was picked as the running header's "last" value for the
        /// *previous* page. Attributing every candidate to an unambiguous single slot up front - the same
        /// slot a fresh top-of-page box would resolve to via <see cref="HtmlContainerInt.SlotStartingAt"/>
        /// elsewhere - removes the overlap a symmetric window can't avoid, without reintroducing the
        /// original hairline-exclusion bug the epsilon existed to fix in the first place (a value meant to
        /// land exactly on a page's own top, but off by float noise, is still nudged onto that same page by
        /// the shared <see cref="HtmlContainerInt.PageBoundaryEpsilon"/> baked into <c>SlotStartingAt</c>).
        /// </param>
        /// <param name="namedStrings">The document-level named-string list, in registration order.</param>
        internal static string ResolveNamedString(
            string name,
            string keyword,
            int currentPageIndex,
            Func<double, int> pageIndexOf,
            IReadOnlyList<NamedString> namedStrings)
        {
            return RunningSelectionEngine.SelectByKeyword(
                namedStrings, name, keyword, currentPageIndex,
                static s => s.Name, static s => s.Y, pageIndexOf)?.Value ?? string.Empty;
        }

        /// <summary>
        /// Resolves <c>element(&lt;name&gt;, first|start|last|first-except)</c> (css-gcpm-3) against the
        /// document-level <see cref="RunningElement"/> list, for the page starting at pagination slot
        /// <paramref name="currentPageIndex"/> - the whole-box analog of <see cref="ResolveNamedString"/>,
        /// sharing its selection rules via <see cref="RunningSelectionEngine"/>. Returns the selected
        /// box's <see cref="CssBox"/> itself (not a value to draw), since <c>element()</c> content needs
        /// to be laid out for real against the margin box's own rect - see <c>RunningElementLayout</c>.
        /// </summary>
        internal static CssBox? ResolveRunningElement(
            string name,
            string keyword,
            int currentPageIndex,
            Func<double, int> pageIndexOf,
            IReadOnlyList<RunningElement> runningElements)
        {
            return RunningSelectionEngine.SelectByKeyword(
                runningElements, name, keyword, currentPageIndex,
                static r => r.Name, static r => r.Y, pageIndexOf)?.Box;
        }

        /// <summary>
        /// Returns the rectangle (in PDF points) for a named margin box.
        /// Uses explicit width/height from each box's CSS rule if present; falls back to equal thirds.
        /// Relative <c>width</c>/<c>height</c>/<c>min-*</c>/<c>max-*</c> values resolve per
        /// <see href="https://www.w3.org/TR/css-page-3/#margin-dimension">css-page-3 §8</see>:
        /// <c>%</c> against the margin-area dimension the box sits in (the content-box width for
        /// top/bottom rows, the content-box height for left/right columns), <c>em</c>/<c>ex</c>/<c>ch</c>
        /// against the box's own computed font size (<c>ch</c> approximates <c>0.5em</c>), and
        /// <c>rem</c> against the root (<paramref name="remPt"/>). Viewport units (<c>vw</c>/<c>vh</c>/
        /// <c>vmin</c>/<c>vmax</c>, and their logical/small/large/dynamic variants) have no page context
        /// here, resolve to null, and the box sizes as <c>auto</c>.
        /// </summary>
        internal static XRect GetMarginBoxRect(string name, XSize page, double mL, double mT, double mR, double mB,
            IReadOnlyList<MarginStyleRule> margins, StyleDeclaration? pageStyle, double remPt)
        {
            var contentLeft   = mL;
            var contentRight  = page.Width - mR;
            var contentWidth  = contentRight - contentLeft;
            var contentTop    = mT;
            var contentBottom = page.Height - mB;
            var contentHeight = contentBottom - contentTop;

            // Resolve a margin-box dimension against its own font (em/ex/ch), the root (rem), and the
            // margin-area extent it sits in (%): contentWidth for width-family props on top/bottom
            // rows, contentHeight for height-family props on left/right columns. Viewport units (no page
            // context here - see DomParser.ParseLengthToPdfPoints) come back null → the box sizes as auto.
            double? ResolveDim(MarginStyleRule? r, string? value, double hundredPercentPt)
            {
                if (r is null || string.IsNullOrWhiteSpace(value))
                    return null;
                var emPt = ResolveFontSizePt(r.Style, pageStyle);
                return DomParser.ParseLengthToPdfPoints(value, new PageLengthContext(emPt, remPt, hundredPercentPt));
            }

            // A declared width/height is the CONTENT box (css-page-3 §5.3.2/§5.3.3), so what gets
            // distributed into the row or column is that plus the box's own margin and padding --
            // otherwise ApplyBoxModel subtracts them from a slot that never accounted for them and
            // the box renders narrower than it asked for. An auto dimension stays null and is
            // unaffected.
            //
            // `bmBasis` is the containing block's extent along the axis being added, per css-page-3
            // §6: contentWidth for a top/bottom row's horizontal edges, contentHeight for a
            // left/right column's vertical ones. Those are the only two combinations reached here --
            // PW is only ever asked of a row, PH only of a column.
            double? Outer(double? contentDim, MarginStyleRule? r, double bmBasis, bool horizontal)
            {
                // A non-null contentDim means ResolveDim found a value, which it only does for a
                // non-null rule.
                if (contentDim is not { } value) return null;
                var (start, end) = BoxModelExtent(r!, pageStyle, remPt, bmBasis, horizontal);
                return value + start + end;
            }

            double? PW(MarginStyleRule? r)    => Outer(ResolveDim(r, r?.Style.Width,     contentWidth), r, contentWidth, true);
            double? PMinW(MarginStyleRule? r) => Outer(ResolveDim(r, r?.Style.MinWidth,  contentWidth), r, contentWidth, true);
            double? PMaxW(MarginStyleRule? r) => Outer(ResolveDim(r, r?.Style.MaxWidth,  contentWidth), r, contentWidth, true);
            double? PH(MarginStyleRule? r)    => Outer(ResolveDim(r, r?.Style.Height,    contentHeight), r, contentHeight, false);
            double? PMinH(MarginStyleRule? r) => Outer(ResolveDim(r, r?.Style.MinHeight, contentHeight), r, contentHeight, false);
            double? PMaxH(MarginStyleRule? r) => Outer(ResolveDim(r, r?.Style.MaxHeight, contentHeight), r, contentHeight, false);

            var tlR = FindMargin(margins, "top-left");
            var tcR = FindMargin(margins, "top-center");
            var trR = FindMargin(margins, "top-right");
            var (tL, tC, tR) = ComputeThreeBoxSizes(contentWidth,
                PW(tlR), PMinW(tlR), PMaxW(tlR),
                PW(tcR), PMinW(tcR), PMaxW(tcR),
                PW(trR), PMinW(trR), PMaxW(trR));

            var blR = FindMargin(margins, "bottom-left");
            var bcR = FindMargin(margins, "bottom-center");
            var brR = FindMargin(margins, "bottom-right");
            var (bL, bC, bR) = ComputeThreeBoxSizes(contentWidth,
                PW(blR), PMinW(blR), PMaxW(blR),
                PW(bcR), PMinW(bcR), PMaxW(bcR),
                PW(brR), PMinW(brR), PMaxW(brR));

            var rtR = FindMargin(margins, "right-top");
            var rmR = FindMargin(margins, "right-middle");
            var rbR = FindMargin(margins, "right-bottom");
            var (rT, rM, rB) = ComputeThreeBoxSizes(contentHeight,
                PH(rtR), PMinH(rtR), PMaxH(rtR),
                PH(rmR), PMinH(rmR), PMaxH(rmR),
                PH(rbR), PMinH(rbR), PMaxH(rbR));

            var ltR = FindMargin(margins, "left-top");
            var lmR = FindMargin(margins, "left-middle");
            var lbR = FindMargin(margins, "left-bottom");
            var (lT, lM, lB) = ComputeThreeBoxSizes(contentHeight,
                PH(ltR), PMinH(ltR), PMaxH(ltR),
                PH(lmR), PMinH(lmR), PMaxH(lmR),
                PH(lbR), PMinH(lbR), PMaxH(lbR));

            return name switch
            {
                // ── top row ──
                "top-left-corner"   => new XRect(0,              0, mL,  mT),
                "top-left"          => new XRect(contentLeft,    0, tL,  mT),
                "top-center"        => new XRect(contentLeft + tL, 0, tC, mT),
                "top-right"         => new XRect(contentLeft + tL + tC, 0, tR, mT),
                "top-right-corner"  => new XRect(contentRight,   0, mR,  mT),

                // ── right column ──
                "right-top"         => new XRect(contentRight, contentTop,          mR, rT),
                "right-middle"      => new XRect(contentRight, contentTop + rT,     mR, rM),
                "right-bottom"      => new XRect(contentRight, contentTop + rT + rM, mR, rB),

                // ── bottom row ──
                "bottom-right-corner" => new XRect(contentRight,              contentBottom, mR,  mB),
                "bottom-right"        => new XRect(contentLeft + bL + bC,     contentBottom, bR,  mB),
                "bottom-center"       => new XRect(contentLeft + bL,          contentBottom, bC,  mB),
                "bottom-left"         => new XRect(contentLeft,               contentBottom, bL,  mB),
                "bottom-left-corner"  => new XRect(0,                         contentBottom, mL,  mB),

                // ── left column ──
                "left-top"          => new XRect(0, contentTop,          mL, lT),
                "left-middle"       => new XRect(0, contentTop + lT,     mL, lM),
                "left-bottom"       => new XRect(0, contentTop + lT + lM, mL, lB),

                _ => XRect.Empty,
            };
        }

        internal static MarginStyleRule? FindMargin(IReadOnlyList<MarginStyleRule> margins, string name) =>
            margins.FirstOrDefault(m =>
                (m.Selector?.Text?.Trim().ToLowerInvariant() ?? "") == name);

        /// <summary>
        /// Distributes <paramref name="available"/> space among three boxes (a, b, c).
        /// Explicit sizes are honoured and clamped to min/max; remaining space is split equally
        /// among auto (null) boxes. Returns equal thirds if all are auto.
        /// </summary>
        private static (double a, double b, double c) ComputeThreeBoxSizes(
            double available,
            double? sizeA, double? minA, double? maxA,
            double? sizeB, double? minB, double? maxB,
            double? sizeC, double? minC, double? maxC)
        {
            static double Clamp(double v, double? min, double? max) =>
                Math.Max(min ?? 0, Math.Min(max ?? double.MaxValue, v));

            // All auto → equal thirds
            if (sizeA == null && sizeB == null && sizeC == null)
            {
                var third = available / 3.0;
                return (third, third, third);
            }

            double fixedSum = 0;
            int autoCount = 0;

            double a = sizeA.HasValue ? Clamp(sizeA.Value, minA, maxA) : 0;
            double b = sizeB.HasValue ? Clamp(sizeB.Value, minB, maxB) : 0;
            double c = sizeC.HasValue ? Clamp(sizeC.Value, minC, maxC) : 0;

            if (sizeA != null) fixedSum += a; else autoCount++;
            if (sizeB != null) fixedSum += b; else autoCount++;
            if (sizeC != null) fixedSum += c; else autoCount++;

            var remaining = Math.Max(0, available - fixedSum);
            var autoShare = autoCount > 0 ? remaining / autoCount : 0;

            if (sizeA == null) a = autoShare;
            if (sizeB == null) b = autoShare;
            if (sizeC == null) c = autoShare;

            return (a, b, c);
        }

        /// <summary>
        /// Resolves a margin box's computed font size in points, falling back to the page context's
        /// own <c>font-size</c> then the CSS initial. Margin boxes have no real inheritance chain, so
        /// a keyword or absolute length is resolved directly; this is the <c>em</c>/<c>ex</c> basis
        /// for a margin box's own <c>width</c>/<c>height</c> per css-page-3 §8.
        /// </summary>
        internal static double ResolveFontSizePt(StyleDeclaration style, StyleDeclaration? pageStyle)
        {
            var sizeStr = FirstNonEmpty(style.FontSize, pageStyle?.FontSize);
            return string.IsNullOrEmpty(sizeStr) ? DefaultFontResolver.FontSize : ResolveFontSizePt(sizeStr);
        }

        /// <summary>
        /// Resolves a <c>font-size</c> declaration string to true PDF points for a page/margin context that
        /// has no real inheritance chain (an absolute length directly; otherwise a keyword or relative unit
        /// against the CSS initial for both the parent and root basis). Reused to derive the <c>em</c>/<c>ex</c>
        /// basis of a <c>@page</c> context's own <c>font-size</c> (issue #162).
        /// </summary>
        internal static double ResolveFontSizePt(string sizeStr) =>
            DomParser.ParseLengthToPdfPoints(sizeStr)
            ?? FontSizeResolver.Resolve(sizeStr, DefaultFontResolver.FontSize, DefaultFontResolver.FontSize);

        internal static XFont BuildFont(StyleDeclaration style, StyleDeclaration? pageStyle, RAdapter adapter)
        {
            var familyList = FirstNonEmpty(style.FontFamily, pageStyle?.FontFamily) ?? DefaultFontResolver.DefaultFont;
            var weightStr = FirstNonEmpty(style.FontWeight, pageStyle?.FontWeight);
            var styleStr = FirstNonEmpty(style.FontStyle, pageStyle?.FontStyle);

            var sizePt = ResolveFontSizePt(style, pageStyle);

            var fontStyle = RFontStyle.Regular;
            // Margin boxes have no real inheritance chain (see FontSizeResolver's own doc comment), so
            // bolder/lighter step relative to the CSS initial weight (400) rather than a real parent's.
            if (weightStr is not null && FontWeightResolver.Resolve(weightStr, 400) >= 700)
                fontStyle |= RFontStyle.Bold;
            if (styleStr is not null &&
                (styleStr.Equals("italic", StringComparison.OrdinalIgnoreCase) ||
                 styleStr.StartsWith("oblique", StringComparison.OrdinalIgnoreCase)))
                fontStyle |= RFontStyle.Italic;

            // MarginBoxRenderer paints in raw, unshrunk PDF-point space (margin-box rects are computed
            // directly from orgPageSize/margins), but RAdapter.GetFont -> PdfSharpAdapter.CreateFontInt
            // divides its `size` argument by PixelsPerPoint (matching in-flow content, whose entire
            // coordinate system - including font size - is uniformly in "pixel" space and shrunk together
            // by that same later division). Since margin-box rect positions never go through that
            // division, pre-multiply so the two cancel out to the CSS-specified point size regardless of
            // PixelsPerPoint (normally 1.0, but not under ShrinkToFit/ScaleToPageSize or non-72
            // PixelsPerInch).
            var pixelsPerPoint = (adapter as PdfSharpAdapter)?.PixelsPerPoint ?? 1.0;
            var pixelSize = sizePt * pixelsPerPoint;
            var resolvedFont = FontFamilyResolver.Resolve(adapter, familyList, pixelSize, fontStyle)
                                ?? FontFamilyResolver.Resolve(adapter, DefaultFontResolver.DefaultFont, pixelSize, fontStyle);

            if (resolvedFont is not FontAdapter fontAdapter)
            {
                throw new HtmlRenderException(
                    $"Cannot find font: {familyList} and Default Font {DefaultFontResolver.DefaultFont} is not installed",
                    HtmlRenderErrorType.General);
            }

            return fontAdapter.Font;
        }

        private static string? FirstNonEmpty(string? a, string? b) =>
            !string.IsNullOrEmpty(a) ? a : (!string.IsNullOrEmpty(b) ? b : null);

        private static XBrush BuildBrush(StyleDeclaration style)
        {
            var colorStr = style.Color;
            if (!string.IsNullOrEmpty(colorStr))
            {
                var color = ParseColor(colorStr);
                if (color.HasValue)
                    return new XSolidBrush(color.Value);
            }
            return XBrushes.Black;
        }

        private static XColor? ParseColor(string value)
        {
            if (string.IsNullOrEmpty(value))
                return null;

            value = value.Trim();

            if (value.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase))
            {
                var inner = value.Substring(4, value.Length - 5);
                var parts = inner.Split(',');
                if (parts.Length >= 3 &&
                    double.TryParse(parts[0].Trim(), out var r) &&
                    double.TryParse(parts[1].Trim(), out var gg) &&
                    double.TryParse(parts[2].Trim(), out var b))
                {
                    return XColor.FromArgb((int)r, (int)gg, (int)b);
                }
            }

            if (value.StartsWith("#"))
            {
                try { return XColor.FromArgb(int.Parse(value.Substring(1), System.Globalization.NumberStyles.HexNumber)); }
                catch { }
            }

            return null;
        }

        private static (string TextAlign, string VerticalAlign) ResolveAlignment(StyleDeclaration style, StyleDeclaration? pageStyle, string boxName)
        {
            // StyleDeclaration.TextAlignAll/VerticalAlign return string.Empty (not null) for an unset
            // property, so a null-coalescing fallback never fires - an unset text-align must fall
            // through to the box's position-inferred default (CSS Paged Media Level 3 §7.2), not an
            // empty string. Check for empty explicitly. Reads TextAlignAll rather than the text-align
            // shorthand's own StyleDeclaration.TextAlign - text-align-last is never relevant to a
            // margin box, and TextAlignAll always resolves to a bare keyword unlike the shorthand's
            // own serialization, which can be empty for a longhand combination it can't represent.
            var textAlign = string.IsNullOrWhiteSpace(style.TextAlignAll)
                ? InferAlignment(boxName)
                : style.TextAlignAll.ToLowerInvariant();

            // text-align's initial/logical values (CSS Text 3 §7.1) resolve against the margin box's
            // own direction, the same as an in-flow box's - see CssLayoutEngine.ResolveHorizontalAlign.
            // match-parent (#1027) is the one exception: css-text-3 §6.2 has it resolve against the
            // *parent's* own direction rather than the box's own - a margin box has no real box-tree
            // parent to consult (see IsRtl's own doc comment), so the page context (pageStyle) is that
            // parent, the same role it already plays as this method's direction/font fallback elsewhere.
            textAlign = textAlign switch
            {
                "start" => IsRtl(style, pageStyle) ? Keywords.Right : Keywords.Left,
                "end" => IsRtl(style, pageStyle) ? Keywords.Left : Keywords.Right,
                "match-parent" => IsPageRtl(pageStyle) ? Keywords.Right : Keywords.Left,
                _ => textAlign
            };

            var verticalAlign = string.IsNullOrWhiteSpace(style.VerticalAlign)
                ? "middle"
                : style.VerticalAlign.ToLowerInvariant();
            return (textAlign, verticalAlign);
        }

        /// <summary>
        /// A margin box's own resolved <c>direction</c> - falls back to the <c>@page</c> context's own
        /// declaration, then <c>ltr</c>, since margin boxes have no real inheritance chain (see
        /// <see cref="ResolveFontSizePt(StyleDeclaration,StyleDeclaration?)"/>'s own doc comment).
        /// </summary>
        private static bool IsRtl(StyleDeclaration style, StyleDeclaration? pageStyle) =>
            (FirstNonEmpty(style.Direction, pageStyle?.Direction) ?? Keywords.Ltr)
            .Equals(Keywords.Rtl, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The page context's own resolved <c>direction</c> - unlike <see cref="IsRtl"/>, never falls
        /// back to the margin box's own declaration, since <c>match-parent</c> (css-text-3 §6.2)
        /// specifically wants the *parent's* direction rather than the box's own, and the page context
        /// is a margin box's stand-in parent (see <see cref="IsRtl"/>'s own doc comment).
        /// </summary>
        private static bool IsPageRtl(StyleDeclaration? pageStyle) =>
            (FirstNonEmpty(pageStyle?.Direction, null) ?? Keywords.Ltr)
            .Equals(Keywords.Rtl, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Applies real UAX#9 resolution (<see cref="BidiResolver"/>) to a margin box's resolved
        /// <c>content</c> text - reordering (L2) and mirroring (L4) it for its own resolved
        /// <c>direction</c>, exactly as in-flow text does (<see cref="CssLayoutEngine"/>), rather than
        /// drawing the logical-order string as-is. Margin-box content is drawn as one already-shaped
        /// string (no line wrapping/word model the way in-flow text has), so reordering runs at
        /// character granularity directly rather than needing a word-level split first.
        /// </summary>
        /// <param name="text">the margin box's resolved logical-order <c>content</c> text</param>
        /// <param name="style">the margin box's own resolved declarations (consulted for <c>direction</c>)</param>
        /// <param name="pageStyle">the page context's declarations, as a <c>direction</c> fallback</param>
        /// <param name="logicalText">
        /// <paramref name="text"/>'s true logical-order source, positionally aligned with the returned
        /// visual string (see <c>PeachPDF.Fonts.CMapInfo.AddShapedText</c>'s own remarks on that
        /// contract) - populated via <c>BidiMirrorResolver.ReverseRunes</c> (position only, no
        /// mirroring - mirroring only changes a character's value, reversal alone already recovers its
        /// position) when the returned visual string is a single run's whole-string reversal+mirror
        /// (<c>BidiMirrorResolver.ApplyMirroring</c>'s own contract), so a caller can recover it for
        /// ToUnicode text-extraction fidelity (see
        /// <see cref="Html.Adapters.RGraphics.DrawString(string, Html.Adapters.RFont, Html.Adapters.Entities.RColor, Html.Adapters.Entities.RPoint, Html.Adapters.Entities.RSize, double, Html.Adapters.Entities.RFontPalette?, PeachPDF.Text.TextShapingFeatures?, string?)"/>).
        /// Null whenever that contract doesn't hold: no reordering happened at all (the visual string
        /// already equals <paramref name="text"/>, so there is nothing to recover), or the content mixed
        /// multiple bidi runs of different direction - a per-run reorder-and-concatenate, not a single
        /// whole-string reversal, which <c>ReverseRunes</c> alone cannot reproduce the alignment of.
        /// </param>
        internal static string ResolveBidiText(string text, StyleDeclaration style, StyleDeclaration? pageStyle, out string? logicalText)
        {
            logicalText = null;
            if (text.Length == 0) return text;

            var direction = IsRtl(style, pageStyle) ? BidiParagraphDirection.Rtl : BidiParagraphDirection.Ltr;
            var result = BidiResolver.Resolve(text, direction);
            var runs = BidiResolver.ReorderLine(result.Levels, 0, text.Length);

            if (runs.Count == 1 && !runs[0].IsRtl) return text;

            var visual = new StringBuilder(text.Length);
            foreach (var run in runs)
            {
                var runText = text.Substring(run.Start, run.Length);
                visual.Append(run.IsRtl ? BidiMirrorResolver.ApplyMirroring(runText, run.Level) : runText);
            }

            if (runs.Count == 1 && runs[0].IsRtl)
                logicalText = BidiMirrorResolver.ReverseRunes(text);

            return visual.ToString();
        }

        /// <summary>
        /// Translates a margin box's resolved <c>(text-align, vertical-align)</c> - the same alignment
        /// its text content uses (defaulting from the box's position per CSS Paged Media Level 3 §7.2,
        /// via <see cref="ResolveAlignment"/>/<see cref="InferAlignment"/>) - into a CSS
        /// <c>background-position</c> string, so <c>&lt;image&gt;</c>-valued <c>content</c> is placed
        /// within the box exactly as text would be, instead of always at the box's content-start.
        /// Mirror of <see cref="BuildStringFormat"/> for the image paint path.
        /// </summary>
        private static string ResolveImagePosition(StyleDeclaration style, StyleDeclaration? pageStyle, string boxName)
        {
            var (textAlign, verticalAlign) = ResolveAlignment(style, pageStyle, boxName);

            var horizontal = textAlign switch
            {
                "left" => "left",
                "right" => "right",
                _ => "center",
            };
            var vertical = verticalAlign switch
            {
                "top" => "top",
                "bottom" => "bottom",
                _ => "center",
            };

            return $"{horizontal} {vertical}";
        }

        private static XStringFormat BuildStringFormat(StyleDeclaration style, StyleDeclaration? pageStyle, string boxName)
        {
            var (textAlign, verticalAlign) = ResolveAlignment(style, pageStyle, boxName);

            return (textAlign, verticalAlign) switch
            {
                ("left",   "top")    => XStringFormats.TopLeft,
                ("left",   "bottom") => XStringFormats.BottomLeft,
                ("left",   _)        => XStringFormats.CenterLeft,
                ("right",  "top")    => XStringFormats.TopRight,
                ("right",  "bottom") => XStringFormats.BottomRight,
                ("right",  _)        => XStringFormats.CenterRight,
                ("center", "top")    => XStringFormats.TopCenter,
                ("center", "bottom") => XStringFormats.BottomCenter,
                _                    => XStringFormats.Center,
            };
        }

        private static string InferAlignment(string boxName) => boxName switch
        {
            "top-left" or "top-left-corner" or "bottom-left" or "bottom-left-corner"
                or "left-top" or "left-middle" or "left-bottom" => "left",
            "top-right" or "top-right-corner" or "bottom-right" or "bottom-right-corner"
                or "right-top" or "right-middle" or "right-bottom" => "right",
            _ => "center",
        };
    }
}
