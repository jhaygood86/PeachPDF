using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
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
