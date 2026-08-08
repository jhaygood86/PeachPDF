using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragmentation;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// The last rung of <c>BreakRelaxation</c>'s ladder
    /// (<see href="https://www.w3.org/TR/css-break-3/#possible-breaks">CSS Fragmentation Level 3
    /// §4.3</see>): when a fragmentainer pass reproduces the resumption record it was handed, the
    /// driver cannot advance, and lays the remainder out monolithically instead.
    /// </summary>
    /// <remarks>
    /// <para>
    /// That recovery was written but had never once run: it sat after a call to a method named
    /// <c>ReportError</c> which threw unconditionally, so every statement of it was dead code and any
    /// document reaching the branch failed outright instead of producing one overflowing page.
    /// </para>
    /// <para>
    /// The stall is provoked by a box in the tree rather than by markup on purpose. Which content
    /// stalls the driver is a separate, open question — it is alignment-sensitive and has resisted
    /// reduction — while what the driver does <i>once</i> a pass has made no progress is a property of
    /// the driver alone, and is what these tests are about. <see cref="StallingBox"/> states the
    /// condition directly: it hands back the same record every pass, which is exactly what a box the
    /// driver cannot get past looks like from the loop's side.
    /// </para>
    /// </remarks>
    public class NoProgressBackstopTests
    {
        /// <summary>
        /// A box that <b>defers</b> the rest of its parent's children with the same resumption record on
        /// every fragmentainer pass, so nothing but the driver's no-progress backstop can end the layout.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Deferring, rather than merely reporting, is what makes the recovery's <i>output</i> observable:
        /// a pending record stops the parent's child loop, so every sibling after this box is placed by
        /// no pass but the recovery. A box that only recorded a break on the context would let the first
        /// pass lay the whole document out and emit it, and the tests below would then hold for a
        /// recovery that did nothing at all.
        /// </para>
        /// <para>
        /// It defers only while breaking is live, which is both honest — a box cannot ask for a break
        /// where none may be taken — and what makes the recovery pass identifiable: that pass is
        /// deliberately suppressed, so the box stops asking and the layout ends. It also never runs
        /// <c>BeginLayoutPass</c>, which is fine for a stub with no geometry of its own but would not be
        /// for anything that took part in fragmentation.
        /// </para>
        /// </remarks>
        private sealed class StallingBox : CssBox
        {
            private BreakToken? _record;

            internal StallingBox(CssBox parent) : base(parent, null)
            {
                InheritStyle(parent, everything: true);
                Display = CssProperty<DisplayMode>.FromValue(Keywords.Block, DisplayMode.Block);
            }

            /// <summary>Whether breaking was live on each pass this box was laid out in, in order.</summary>
            internal List<bool> FragmentingPerPass { get; } = [];

            protected override ValueTask PerformLayoutImp(RGraphics g, CssBox frame, bool framePlacesChild)
            {
                var context = HtmlContainer?.CurrentFragmentainer;
                FragmentingPerPass.Add(context is { IsFragmenting: true });

                if (context is { IsFragmenting: true })
                {
                    // Cached, so every pass hands the parent a record equal to the one the last pass did,
                    // and the chain the parent wraps it in is equal too. A fresh record naming the same
                    // place would do as well - BreakToken is a record type, so the driver's test is value
                    // equality - but caching states the intent.
                    _record ??= new BlockBreakToken(
                        this, context.SlotIndex, 0, null, IsBreakBefore: true, null);
                }

                // Cleared explicitly on the suppressed pass, because this override never runs
                // BeginLayoutPass - which is what clears a box's pending record for the pass about to
                // run. Left set, it would stop the parent's child loop on the recovery pass too, and
                // the siblings the recovery exists to place would never be laid out at all.
                SetPendingBreakToken(context is { IsFragmenting: true } ? _record : null);

                return default;
            }
        }

        /// <summary>
        /// A box that gets nowhere in a cycle <b>two</b> passes long: it hands back one record, then the
        /// other, then the first again. Neither pass reproduces the record it was handed, so the
        /// consecutive-pass equality test says both made progress.
        /// </summary>
        /// <remarks>
        /// This is the second of the two ways the driver's backstop can be defeated, and unlike the first
        /// it does not depend on any token type's equality being wrong. Before the driver remembered every
        /// pair it had been entered with, this ran to <c>MaxFragmentainers</c> — 100,000 passes — and then
        /// left the loop having emitted nothing after the stall, which is a truncated document reported as
        /// a successful render.
        /// </remarks>
        private sealed class AlternatingBox : CssBox
        {
            private BreakToken? _even;
            private BreakToken? _odd;
            private int _passes;

            internal AlternatingBox(CssBox parent) : base(parent, null)
            {
                InheritStyle(parent, everything: true);
                Display = CssProperty<DisplayMode>.FromValue(Keywords.Block, DisplayMode.Block);
            }

            protected override ValueTask PerformLayoutImp(RGraphics g, CssBox frame, bool framePlacesChild)
            {
                var context = HtmlContainer?.CurrentFragmentainer;

                if (context is not { IsFragmenting: true })
                {
                    // See StallingBox: this override never runs BeginLayoutPass, so what the lifecycle
                    // would have cleared has to be cleared here or the recovery pass places nothing.
                    SetPendingBreakToken(null);
                    return default;
                }

                // Two records naming two different child indices of the same box, alternating. Both are
                // cached so that each is equal to the one two passes ago rather than merely alike.
                _even ??= new BlockBreakToken(this, context.SlotIndex, 0, null, IsBreakBefore: true, null);
                _odd ??= new BlockBreakToken(this, context.SlotIndex, 1, null, IsBreakBefore: true, null);

                SetPendingBreakToken(_passes++ % 2 == 0 ? _even : _odd);
                return default;
            }
        }

        /// <summary>
        /// A box that never gets nowhere the same way twice: every pass it is laid out in while breaking
        /// is live, it hands back a record naming a child index one higher than the last, so no <c>(slot,
        /// token)</c> pair it produces is ever repeated.
        /// </summary>
        /// <remarks>
        /// This is the third way the driver's pass budget can be reached, distinct from both
        /// <see cref="StallingBox"/>'s one-pass cycle and <see cref="AlternatingBox"/>'s two-pass one:
        /// <see cref="HasAlreadyBeenEntered"/> never trips for a run that keeps producing pairs it has
        /// never been entered with before, so only the loop's own bound — <c>MaxFragmentainers</c>, or
        /// <see cref="MaxFragmentainersOverride"/> in a test — can end it. Before that fallback existed,
        /// running out of passes this way fell out of the driver loop with whatever the last pass produced
        /// never emitted.
        /// </remarks>
        private sealed class WalkingBox : CssBox
        {
            private int _steps;

            internal WalkingBox(CssBox parent) : base(parent, null)
            {
                InheritStyle(parent, everything: true);
                Display = CssProperty<DisplayMode>.FromValue(Keywords.Block, DisplayMode.Block);
            }

            protected override ValueTask PerformLayoutImp(RGraphics g, CssBox frame, bool framePlacesChild)
            {
                var context = HtmlContainer?.CurrentFragmentainer;

                if (context is not { IsFragmenting: true })
                {
                    // See StallingBox: this override never runs BeginLayoutPass, so what the lifecycle
                    // would have cleared has to be cleared here or the recovery pass places nothing.
                    SetPendingBreakToken(null);
                    return default;
                }

                SetPendingBreakToken(
                    new BlockBreakToken(this, context.SlotIndex, _steps++, null, IsBreakBefore: true, null));
                return default;
            }
        }

        /// <summary>
        /// Lays <paramref name="before"/>, then a box the driver cannot get past, then
        /// <paramref name="after"/> — so everything in <paramref name="after"/> exists only if the
        /// recovery laid it out and the emitter kept it.
        /// </summary>
        private static async Task<(HtmlContainerInt Container, StallingBox Stall)> LayoutWithAStall(
            string before, string after, string pageCss = "")
        {
            StallingBox? stall = null;

            var container = await LayoutWith(before, after, pageCss, parent => stall = new StallingBox(parent));

            Assert.NotNull(stall);
            return (container, stall);
        }

        /// <summary>
        /// The same, with a box whose cycle is two passes long rather than one.
        /// </summary>
        private static async Task<HtmlContainerInt> LayoutWithATwoPassCycle(string before, string after) =>
            await LayoutWith(before, after, "", parent => new AlternatingBox(parent));

        /// <summary>
        /// The same, with a box that never repeats a <c>(slot, token)</c> pair — a genuine walk — and the
        /// driver's pass budget shrunk to <paramref name="maxPasses"/> so it exhausts in a handful of
        /// passes rather than the real 100,000.
        /// </summary>
        private static async Task<HtmlContainerInt> LayoutWithAWalkToExhaustion(
            string before, string after, int maxPasses) =>
            await LayoutWith(before, after, "", parent => new WalkingBox(parent), maxPasses);

        private static async Task<HtmlContainerInt> LayoutWith(
            string before, string after, string pageCss, Func<CssBox, CssBox> makeStall,
            int? maxFragmentainersOverride = null)
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><head><style>" + pageCss + "</style></head><body style='margin:0'>"
                    + before + "<div id='stall-anchor'></div>" + after + "</body></html>",
                pageHeight: 200,
                margin: 0,
                prepare: root =>
                {
                    root.HtmlContainer!.MaxFragmentainersOverride = maxFragmentainersOverride;

                    var anchor = LayoutHarness.FindById(root, "stall-anchor")!;
                    var parent = anchor.ParentBox!;

                    var stall = makeStall(parent);

                    // Constructed at the end of the child list, then moved into the anchor's place, so
                    // the content in `after` really is after it. (CssBox.ParentBox's setter appends,
                    // which is why this is a move rather than an insert.)
                    parent.Boxes.Remove(stall);
                    parent.Boxes.Insert(parent.Boxes.IndexOf(anchor), stall);
                });

            return container;
        }

        /// <summary>
        /// The whole point of the fix: a document the driver cannot advance through still renders. Before
        /// it, this threw <c>HtmlRenderException("Layout could not advance past a fragmentainer
        /// boundary")</c> out of layout and the caller got no document at all.
        /// </summary>
        [Fact]
        public async Task APassThatMakesNoProgress_StillProducesADocument()
        {
            var (container, _) = await LayoutWithAStall(
                before: "<p style='margin:0'>alpha</p>", after: "<p style='margin:0'>omega</p>");

            Assert.NotNull(container.FragmentTree);
            Assert.NotEmpty(container.FragmentTree!.Fragmentainers);
        }

        /// <summary>
        /// And it says so: a non-zero count is the only signal there is that a document contains a
        /// fragmentainer layout could not get past, since PeachPDF has no non-fatal diagnostic channel.
        /// </summary>
        [Fact]
        public async Task APassThatMakesNoProgress_IsCountedAsALastResortRelayout()
        {
            var (container, _) = await LayoutWithAStall(
                before: "<p style='margin:0'>alpha</p>", after: "<p style='margin:0'>omega</p>");

            Assert.Equal(1, container.LastResortRelayouts);
        }

        /// <summary>
        /// An ordinary document reaches none of this — the backstop is a last resort, not a step every
        /// paginated layout takes.
        /// </summary>
        [Fact]
        public async Task AnOrdinaryPaginatedDocument_TakesNoLastResortRelayout()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(string.Concat(Enumerable.Range(0, 40)
                    .Select(i => $"<p style='margin:0'>paragraph {i}</p>"))),
                pageHeight: 200, margin: 0);

            Assert.True(container.FragmentainerPasses > 1, "the fixture must paginate");
            Assert.Equal(0, container.LastResortRelayouts);
        }

        /// <summary>
        /// The recovery is <i>monolithic</i>, which is the half of it that makes it terminate: the pass
        /// exists and fills a real slot the emitter reads, but no break may be taken in it, so the box
        /// that could not be got past cannot ask again.
        /// </summary>
        [Fact]
        public async Task TheRecoveryPass_RunsWithBreakingSuppressed()
        {
            var (_, stall) = await LayoutWithAStall(
                before: "<p style='margin:0'>alpha</p>", after: "<p style='margin:0'>omega</p>");

            Assert.True(stall.FragmentingPerPass.Count >= 2,
                $"the fixture must stall, but the box was laid out {stall.FragmentingPerPass.Count} time(s)");
            Assert.True(stall.FragmentingPerPass[0], "the passes before the backstop are ordinary fragmenting passes");
            Assert.False(stall.FragmentingPerPass[^1], "the recovery pass must not allow another break");
        }

        /// <summary>
        /// "An overflowing fragmentainer is a far better outcome than dropped content" is the recovery's
        /// stated reason for existing, so the content has to actually be there — and claimed once, not
        /// duplicated by the pass that was abandoned.
        /// </summary>
        [Fact]
        public async Task TheRecovery_KeepsTheDocumentsContent()
        {
            var (container, _) = await LayoutWithAStall(
                before: "<p style='margin:0'>alpha</p>",
                after: "<p style='margin:0'>beta</p><p style='margin:0'>gamma</p>");

            var claimed = WordsIn(container);

            // Everything the stall deferred is there, and claimed exactly once - the invariant that
            // fails one way if the recovery drops content and the other way if it duplicates what the
            // abandoned pass had already emitted.
            Assert.Equal(["alpha", "beta", "gamma"], claimed.Order());
        }

        /// <summary>
        /// The count is reset per <c>LayoutDocument</c>, not per document — <c>ShrinkToFit</c> and the
        /// per-page reflow loop each re-run it, and a counter that survived would report one recovery per
        /// layout rather than the one the fragment tree was actually built from.
        /// </summary>
        [Fact]
        public async Task ADocumentLaidOutSeveralTimes_CountsOnlyTheLastLayoutsRecovery()
        {
            // Per-page left/right @page margins put PerformLayout into its reflow loop, so LayoutDocument
            // runs several times over the same box tree.
            var (container, stall) = await LayoutWithAStall(
                before: "<p style='margin:0'>alpha</p>",
                after: "<p style='margin:0'>omega</p>",
                pageCss: "@page{margin:10pt}@page :left{margin-left:60pt}@page :right{margin-right:60pt}");

            Assert.True(container.UseVariablePageWidth, "the fixture must drive the reflow loop");
            Assert.True(stall.FragmentingPerPass.Count > 3,
                $"the fixture must lay out more than once, but the box was laid out {stall.FragmentingPerPass.Count} time(s)");
            Assert.Equal(1, container.LastResortRelayouts);
            Assert.Equal(["alpha", "omega"], WordsIn(container).Order());
        }

        /// <summary>
        /// A cycle two passes long is a run that gets nowhere just as surely as one pass long, and the
        /// driver has to end it the same way. The consecutive-pass equality test cannot see it: neither
        /// pass reproduces the record it was handed.
        /// </summary>
        /// <remarks>
        /// The counter is the assertion, never the clock. Before the driver remembered every pair it had
        /// been entered with, this took the full 100,000-pass budget and recovered <i>not at all</i> — so
        /// a bound well under the budget and a recovery that was counted are the two halves of the same
        /// statement.
        /// </remarks>
        [Fact]
        public async Task ACycleTwoPassesLong_IsAlsoRecognisedAsNoProgress()
        {
            var container = await LayoutWithATwoPassCycle(
                before: "<p style='margin:0'>alpha</p>", after: "<p style='margin:0'>omega</p>");

            Assert.Equal(1, container.LastResortRelayouts);
            Assert.True(container.FragmentainerPasses < 20,
                $"the run must be cut short, but took {container.FragmentainerPasses} passes");
        }

        /// <summary>
        /// And the content the cycle was deferring survives it, which is the whole reason the recovery is
        /// preferred to letting the pass budget run out: the budget drops everything after the stall.
        /// </summary>
        [Fact]
        public async Task ACycleTwoPassesLong_KeepsTheDocumentsContent()
        {
            var container = await LayoutWithATwoPassCycle(
                before: "<p style='margin:0'>alpha</p>",
                after: "<p style='margin:0'>beta</p><p style='margin:0'>gamma</p>");

            Assert.Equal(["alpha", "beta", "gamma"], WordsIn(container).Order());
        }

        /// <summary>
        /// A run that gets nowhere by <i>walking</i> rather than cycling — never repeating a
        /// <c>(slot, token)</c> pair — trips no cycle detection at all, so only the driver's pass budget
        /// can end it. Running out of that budget must not be silent truncation: before this issue was
        /// fixed, the loop fell out with the last pass's content never emitted (#422).
        /// </summary>
        [Fact]
        public async Task APassThatWalksForever_StillProducesADocument()
        {
            var container = await LayoutWithAWalkToExhaustion(
                before: "<p style='margin:0'>alpha</p>", after: "<p style='margin:0'>omega</p>", maxPasses: 5);

            Assert.NotNull(container.FragmentTree);
            Assert.NotEmpty(container.FragmentTree!.Fragmentainers);
        }

        /// <summary>
        /// Reaching the pass budget without a detected cycle is routed through the same last-resort
        /// recovery a cycle is, so it is counted the same way and the run actually stops at the budget
        /// rather than running past it looking for a cycle that will never come.
        /// </summary>
        [Fact]
        public async Task APassThatWalksForever_IsCountedAsALastResortRelayoutAndStopsAtTheBudget()
        {
            const int maxPasses = 5;

            var container = await LayoutWithAWalkToExhaustion(
                before: "<p style='margin:0'>alpha</p>", after: "<p style='margin:0'>omega</p>", maxPasses);

            Assert.Equal(1, container.LastResortRelayouts);
            // One extra pass: the recovery itself also counts against FragmentainerPasses.
            Assert.Equal(maxPasses + 1, container.FragmentainerPasses);
        }

        /// <summary>
        /// And the content the walk was deferring survives it — the whole point of routing exhaustion
        /// through the recovery instead of letting the loop simply end.
        /// </summary>
        [Fact]
        public async Task APassThatWalksForever_KeepsTheDocumentsContent()
        {
            var container = await LayoutWithAWalkToExhaustion(
                before: "<p style='margin:0'>alpha</p>",
                after: "<p style='margin:0'>beta</p><p style='margin:0'>gamma</p>",
                maxPasses: 5);

            Assert.Equal(["alpha", "beta", "gamma"], WordsIn(container).Order());
        }

        /// <summary>
        /// A multi-column container laid out entirely by the recovery still establishes its own
        /// per-column fragmentation context (<c>CssLayoutEngineColumns</c>, <c>inheritsSuppression:
        /// true</c>), which used to still record a column break nothing above it ever reads — the
        /// recovery emits with <c>outgoing: null</c> unconditionally — silently dropping whatever content
        /// that break named (#423). Fixed by gating the column-break arms in
        /// <c>CssBox.LayoutBlockChildren</c> on <c>IsFragmenting</c> as well as <c>HasOwnBand</c>, the same
        /// pattern already used elsewhere (e.g. the escaped-forced-break arm), so a column context nested
        /// in a non-fragmenting scope stops trying to record breaks at all.
        /// </summary>
        /// <remarks>
        /// Twelve items is not enough to reproduce the drop — a small multicol container's own two-column
        /// capacity happens to hold all of it. Sixty is: without the fix, everything past the point the
        /// multicol container exhausts both its columns is never positioned at all and silently missing.
        /// Asserted as containment rather than exactly-once: an unbreakable box that straddles a page
        /// boundary while breaking is suppressed is drawn on both pages it touches, deliberately (#484) —
        /// unrelated to what this test is about, and a large enough fixture to reproduce the drop can
        /// happen to land an item or two across such a boundary too.
        /// </remarks>
        [Fact]
        public async Task TheRecovery_KeepsAMulticolChildsContent()
        {
            var items = string.Concat(Enumerable.Range(1, 60)
                .Select(i => $"<div style='height:40px'>item{i}</div>"));

            var (container, _) = await LayoutWithAStall(
                before: "<p style='margin:0'>alpha</p>",
                after: $"<div style='columns:2; column-gap:0; width:200px'>{items}</div>");

            var expected = new[] { "alpha" }.Concat(Enumerable.Range(1, 60).Select(i => $"item{i}"));
            var actual = WordsIn(container).ToHashSet();
            var missing = expected.Where(word => !actual.Contains(word)).ToList();

            Assert.Equal(1, container.LastResortRelayouts);
            Assert.Empty(missing);
        }

        private static IReadOnlyList<string> WordsIn(HtmlContainerInt container) =>
            container.FragmentTree!.Fragmentainers
                .SelectMany(f => Flatten(f.Root))
                .SelectMany(f => f.Words)
                .Select(w => w.Word.Text!)
                .ToList();

        private static IEnumerable<BoxFragment> Flatten(BoxFragment fragment)
        {
            yield return fragment;

            foreach (var child in fragment.Children)
            {
                foreach (var descendant in Flatten(child))
                {
                    yield return descendant;
                }
            }
        }
    }
}
