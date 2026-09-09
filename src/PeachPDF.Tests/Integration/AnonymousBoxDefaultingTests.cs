using PeachPDF;
using PeachPDF.PdfSharpCore;
using System;
using System.Text;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// The cascade's defaulting step does not re-parse the whole initial-value table for an
    /// anonymous box that holds nothing but its structural display.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ComputedStyleTests.CascadeDefaultingLoop_OnAFreshBox_OnlyKnownExceptionsAreNotNoOps"/>
    /// already establishes the fact this rests on: setting a property to its own initial value leaves
    /// the box on the shared <c>ComputedStyle.Default</c>. So the loop reconstructs, one property parse
    /// at a time, a state that re-pointing at <c>Default</c> gives directly — and it is equivalent for
    /// <i>any</i> prior divergence, not just one known cause, because it is a full state overwrite and
    /// the loop discarded every non-display property unconditionally too.
    /// </para>
    /// <para>
    /// <b>Every list item is one such box</b>, which is what makes this worth gating rather than
    /// leaving to a profiler. An ordinary <c>&lt;ul&gt;</c> generates one forked anonymous box per
    /// item — measured at 2,031 of them across a 26-document corpus — and each one was re-parsing a
    /// few hundred initial values it already held.
    /// </para>
    /// <para>
    /// <b>Why the assertion is a ratio of slopes.</b> Allocated bytes are deterministic for a given
    /// build and input, but their absolute value depends on the font a machine resolves. So this
    /// measures each shape at two sizes and takes the <i>marginal</i> cost per item, which subtracts
    /// the fixed baseline — document setup, font loading, the page machinery — and then divides the
    /// list's slope by a plain block's, which cancels the per-item text work as well. What is left is
    /// the list machinery itself.
    /// </para>
    /// <para>
    /// The plainer form this replaced — one list document over one plain document — left both of
    /// those in the numbers, and measured <b>1.71x</b> here against a <b>2.06x</b> a reviewer saw
    /// repeatably on their own machine, against a 2.05x bound. Same build, same direction, a fifth of
    /// the window apart purely from what the two machines' baselines cost.
    /// </para>
    /// </remarks>
    public class AnonymousBoxDefaultingTests
    {
        /// <summary>The two document sizes the slope is taken between.</summary>
        private const int Small = 200;

        /// <inheritdoc cref="Small"/>
        private const int Large = 600;

        /// <summary>
        /// Measured: <b>1.88x</b> with the fast path and <b>2.87x</b> without — repeatable to three
        /// decimal places — so the bound sits between them with 25% of margin below and 18% above.
        /// Stable both in isolation and with the whole suite running in parallel around it, but only
        /// because the measurement is per-thread; see <see cref="MarginalKbPerItem"/> for what that is
        /// guarding against.
        ///
        /// The item text is one character on purpose. The saving is a fixed cost per list item, so the
        /// longer the text the more font work dilutes it: at 200 items of a full sentence the same
        /// patch only moves the ratio from 1.60 to 1.48, which is too narrow a window to gate on
        /// across machines that resolve different fonts. Short text makes the thing being measured the
        /// dominant term.
        ///
        /// It is a ratchet, not a law — if a change makes lists legitimately dearer, move it in the
        /// same commit and say why.
        /// </summary>
        private const double MaxListOverhead = 2.35;

        [Fact]
        public void AListItemCostsLittleMoreThanAPlainBlock()
        {
            var listSlope = MarginalKbPerItem(list: true);
            var plainSlope = MarginalKbPerItem(list: false);
            var overhead = listSlope / plainSlope;

            Assert.True(overhead <= MaxListOverhead,
                $"a list item costs {overhead:F2}x what a plain block costs at the margin "
                + $"({listSlope:F1} KB against {plainSlope:F1} KB per item), over the {MaxListOverhead:F2}x "
                + "bound. Each list item generates an anonymous box, and this is what re-defaulting "
                + "every one of them from scratch looks like.");
        }

        /// <summary>
        /// Allocated kilobytes per additional item — the slope between two document sizes, so the
        /// fixed cost of rendering anything at all drops out.
        /// </summary>
        /// <remarks>
        /// Measured <b>per thread</b> and synchronously, and both of those matter.
        /// <c>GC.GetTotalAllocatedBytes</c> is process-wide, and this suite runs collections in
        /// parallel, so it counts whatever every other test is allocating at the same moment — which
        /// is how an earlier version of this, stable to a fraction of a percent in isolation, was
        /// reported swinging between 2.05x and 4.59x with the suite running.
        /// <c>GetAllocatedBytesForCurrentThread</c> is immune to that, and blocking on the render
        /// rather than awaiting it keeps the whole measured region on the one thread it counts.
        /// </remarks>
        private static double MarginalKbPerItem(bool list) =>
            (AllocatedMbPerRender(Document(Large, list)) - AllocatedMbPerRender(Document(Small, list)))
            / (Large - Small) * 1024;

        /// <summary>
        /// The same single character in each item either way, so the two shapes differ only in whether
        /// it sits in a list. One character on purpose: the saving is a fixed cost per item, so longer
        /// text dilutes it into the font work.
        /// </summary>
        private static string Document(int items, bool list)
        {
            var sb = new StringBuilder("<html><body style=\"font-family:sans-serif\">");

            if (list) sb.Append("<ul>");

            for (var i = 0; i < items; i++) sb.Append(list ? "<li>x</li>" : "<div>x</div>");

            if (list) sb.Append("</ul>");

            return sb.Append("</body></html>").ToString();
        }

        private static double AllocatedMbPerRender(string html)
        {
            // Warm the JIT, the font cache and every static table: none of it is per-render cost, and
            // all of it would otherwise land in the first measured iteration.
            for (var i = 0; i < 3; i++) Render(html);

            const int renders = 5;
            var before = GC.GetAllocatedBytesForCurrentThread();

            for (var i = 0; i < renders; i++) Render(html);

            return (GC.GetAllocatedBytesForCurrentThread() - before) / (double)renders / 1024 / 1024;
        }

        private static void Render(string html) =>
            new PdfGenerator().GeneratePdf(html, PageSize.Letter, margin: 36).GetAwaiter().GetResult();
    }
}
