using PeachPDF;
using PeachPDF.PdfSharpCore;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
    /// <b>Why the assertion is a ratio.</b> Allocated bytes are deterministic for a given build and
    /// input, but their absolute value depends on the font a machine resolves. Both documents here
    /// are measured in the same run on the same machine and carry the same text; they differ only in
    /// whether that text sits in list items. That makes the comparison self-calibrating, so this
    /// says the same thing on every platform.
    /// </para>
    /// </remarks>
    public class AnonymousBoxDefaultingTests
    {
        private const int Items = 400;

        /// <summary>
        /// Measured on this fixture: <b>1.71x</b> with the fast path and <b>2.41x</b> without, so the
        /// bound sits between them with roughly 20% of margin either side. Stable to the second decimal
        /// place both in isolation and with the whole suite running in parallel around it — but only
        /// because the measurement is per-thread; see <see cref="AllocatedMbPerRender"/> for what that
        /// is guarding against.
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
        private const double MaxListOverhead = 2.05;

        [Fact]
        public void AListCostsLittleMoreThanTheSameTextWithoutOne()
        {
            var lines = Enumerable.Range(0, Items)
                .Select(i => "x")
                .ToList();

            var list = Document("<ul>" + string.Concat(lines.Select(l => $"<li>{l}</li>")) + "</ul>");
            var plain = Document(string.Concat(lines.Select(l => $"<div>{l}</div>")));

            var listMb = AllocatedMbPerRender(list);
            var plainMb = AllocatedMbPerRender(plain);
            var overhead = listMb / plainMb;

            Assert.True(overhead <= MaxListOverhead,
                $"{Items} list items allocate {overhead:F2}x what the same text costs without a list "
                + $"({listMb:F1} MB against {plainMb:F1} MB), over the {MaxListOverhead:F2}x bound. Each "
                + "list item generates an anonymous box, and this is what re-defaulting every one of "
                + "them from scratch looks like.");
        }

        private static string Document(string body) =>
            $"<html><body style=\"font-family:sans-serif\">{body}</body></html>";

        /// <summary>
        /// Allocated megabytes per render, measured <b>per thread</b> and synchronously.
        /// </summary>
        /// <remarks>
        /// Both of those matter and the first version of this had neither. <c>GC.GetTotalAllocatedBytes</c>
        /// is process-wide, and this suite runs collections in parallel, so it counts whatever every
        /// other test is allocating at the same moment — which is how a measurement claimed here to be
        /// stable to a fraction of a percent was reported swinging between 2.05x and 4.59x on a
        /// reviewer's machine. <c>GetAllocatedBytesForCurrentThread</c> is immune to that, and blocking
        /// on the render rather than awaiting it keeps the whole measured region on the one thread it
        /// counts.
        /// </remarks>
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
