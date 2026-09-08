using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using System;
using System.Threading.Tasks;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// Lays a <c>position: running(name)</c> box (css-gcpm-3) out for real against a page margin box's
    /// own rect, so <c>content: element(name)</c> can show it "complete with formatting and any
    /// descendant elements" rather than a captured plain string. The hard problem: the same shared
    /// <see cref="CssBox"/> may be selected onto several pages, each with a different margin-box width
    /// (different <c>@page</c> rules, <c>:first</c> vs. later pages) - so this cannot be
    /// <c>CssProxyBox</c>'s rigid-translation trick, which assumes identical constraints at every repeat.
    /// Instead this re-runs genuine layout per page, sequentially, immediately followed by the caller
    /// capturing the result into an immutable fragment before the next page's call mutates the same box
    /// again - see the margin-box layout phase that calls this.
    /// </summary>
    internal static class RunningElementLayout
    {
        /// <summary>
        /// Lays <paramref name="runningBox"/> out against <paramref name="marginBoxContentRect"/> as if it
        /// were the sole child of a fresh block container sized to that rect, using the same
        /// detached-fragmentainer, breaking-suppressed, direct <see cref="CssBox.PerformLayout"/> pattern
        /// <c>CssLayoutEngineFlex.PerformLayoutBlockified</c> already establishes for "lay this box out
        /// again, off to the side, at a provisional position, within one document-layout generation".
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Why a synthetic containing block.</b> A flex item's own re-measurement resolves percentages/
        /// <c>auto</c> correctly because its real parent already <i>is</i> the flex container being
        /// measured - <see cref="CssBox.ContainingBlock"/> (a computed getter walking <see cref="CssBox.ParentBox"/>,
        /// not a settable field) needs no help. A running box's real parent is wherever it sits in the
        /// document, not the margin box, so width/percentage resolution needs to see the margin box's rect
        /// instead. <paramref name="runningBox"/>.<see cref="CssBox.ParentBox"/> is temporarily reparented
        /// to a throwaway block box sized to the rect, then restored - <c>ParentBox</c>'s setter already
        /// removes/re-adds the box from/to <c>Boxes</c> on each assignment, so no separate list bookkeeping
        /// is needed here.
        /// </para>
        /// <para>
        /// <b>Why <see cref="ResetRectanglesRecursively"/>, not a single top-level <c>RectanglesReset</c>.</b>
        /// A box's own layout prologue (<c>PerformLayoutPrologue</c>) - which owns <c>RectanglesReset</c> -
        /// runs once per box per layout generation; a second <c>PerformLayout</c> call on the same box
        /// within one generation skips it (exactly why the flex precedent's own callers call
        /// <c>RectanglesReset</c> themselves before every repeat). A running box can hold nested block
        /// children of its own, each with the identical once-per-generation prologue guard, so resetting
        /// only <paramref name="runningBox"/> itself would leave a nested child's stale line boxes in
        /// place on the second and later pages it's selected onto - this resets the whole captured
        /// subtree defensively.
        /// </para>
        /// </remarks>
        internal static async ValueTask LayoutRunningElementFor(
            RGraphics g, CssBox runningBox, RRect marginBoxContentRect, HtmlContainerInt container)
        {
            var savedParent = runningBox.ParentBox;
            var syntheticContainer = new CssBox(null, null)
            {
                HtmlContainer = container,
                Display = CssProperty<DisplayMode>.FromValue(Keywords.Block, DisplayMode.Block),
                Location = new RPoint(marginBoxContentRect.X, marginBoxContentRect.Y),
                Size = new RSize(marginBoxContentRect.Width, marginBoxContentRect.Height)
            };

            // Before the reparent, deliberately. Re-resolving `content` re-runs
            // CssContentEngine.ApplyContent, whose open-quote/close-quote handling walks
            // box.ParentBox and previous siblings live (GetQuoteDepthAtStart has no memoization, unlike
            // the counter lookup, which short-circuits on FinalizedCounterNames and never re-walks).
            // Run after the reparent, a running element mixing quotes with counter(page) -- say
            // `content: open-quote counter(page) close-quote` -- would resolve its quote depth against
            // syntheticContainer, which has no ancestors and no siblings, instead of its real position
            // in the document.
            RefreshPageCounterContent(runningBox, container);

            runningBox.ParentBox = syntheticContainer;
            runningBox.Location = new RPoint(marginBoxContentRect.X, marginBoxContentRect.Y);
            runningBox.ActualBottom = runningBox.Location.Y;
            ResetRectanglesRecursively(runningBox);

            var previousSuppress = container.SuppressWordPageBreaks;
            container.SuppressWordPageBreaks = true;
            var fragmentainer = container.DetachFragmentainer();

            try
            {
                await runningBox.PerformLayout(g);
            }
            finally
            {
                container.RestoreFragmentainer(fragmentainer);
                container.SuppressWordPageBreaks = previousSuppress;
                runningBox.ParentBox = savedParent;
            }
        }

        /// <summary>
        /// Re-resolves any <c>counter(page)</c>/<c>counter(pages)</c> in this subtree's
        /// <c>content</c> against the page being laid out for, before that layout runs.
        /// </summary>
        /// <remarks>
        /// <c>CssContentEngine.ApplyContent</c> runs once, at DOM-construction time, and bakes the
        /// resolved string into the box's <c>Text</c>. For everything else in a running element that is
        /// correct - the text does not depend on which page the band is drawn on - but the page counters
        /// do, and left alone they read "Page 1 of 1" on every page. This is the same re-apply-then-
        /// re-parse shape <c>HtmlContainerInt.ReapplyPseudoElementContent</c> already uses for
        /// <c>string()</c>, run per page here because that is the granularity the answer changes at.
        /// Reading <c>Content</c> rather than a flag set at parse time keeps this self-contained; a
        /// running element is a header or footer band, so the subtree is small and walked once per page.
        /// </remarks>
        private static void RefreshPageCounterContent(CssBox box, HtmlContainerInt container)
        {
            if (container.RunningElementPageContext is null) return;

            if (!string.IsNullOrEmpty(box.Content)
                && box.Content != Keywords.None
                && box.Content != Keywords.Normal
                && MentionsPageCounter(box.Content))
            {
                CssContentEngine.ApplyContent(box);

                // Re-resolve bidi for the new text before re-parsing words. BidiLevels/CharScripts/
                // JoiningForms are indexed against the text the LAST resolution saw, and a page
                // counter changes its own length -- "9" becomes "10" -- so ParseToWords would index
                // past the end of a stale array and throw IndexOutOfRangeException. Same contract
                // HtmlContainerInt.ReapplyPseudoElementContent and ResolveTargetPageContent already
                // follow for the other two re-resolution paths.
                CssBidiParagraphResolver.ResolveOwnTextAsParagraph(box);

                if (!string.IsNullOrEmpty(box.Text))
                {
                    box.ParseToWords();
                }
            }

            foreach (var child in box.Boxes)
            {
                RefreshPageCounterContent(child, container);
            }
        }

        /// <summary>
        /// Whether a <c>content</c> declaration plausibly references the <c>page</c> or <c>pages</c>
        /// counter. Deliberately a cheap textual pre-check ahead of the real tokenizing resolution in
        /// <see cref="CssContentEngine.ApplyContent"/>.
        /// </summary>
        /// <remarks>
        /// It is closer to "mentions `counter` and the substring `page` somewhere" than to a real
        /// parse, so <c>counter(page-count)</c> or <c>counter(total-pages)</c> match too. That is
        /// harmless: re-resolving a document counter returns the same value, because
        /// <c>CssCounterEngine.GetCounter</c> short-circuits on <c>FinalizedCounterNames</c> and reads
        /// the value already cached on the box rather than re-walking the tree. The quote depth is the
        /// one thing a re-resolution could get wrong, which is why this runs before the reparent — see
        /// the call site.
        ///
        /// There is no false negative: any spelling that tokenizes as <c>counter(page)</c> or
        /// <c>counter(pages)</c> contains both substrings.
        /// </remarks>
        private static bool MentionsPageCounter(string content) =>
            content.Contains("page", StringComparison.OrdinalIgnoreCase)
            && content.Contains("counter", StringComparison.OrdinalIgnoreCase);

        private static void ResetRectanglesRecursively(CssBox box)
        {
            box.RectanglesReset();

            foreach (var child in box.Boxes)
            {
                ResetRectanglesRecursively(child);
            }
        }
    }
}
