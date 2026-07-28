using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// css-tables-3 §6.2's two conditions on repeating a <c>&lt;thead&gt;</c>/<c>&lt;tfoot&gt;</c> at all
    /// (<see href="https://github.com/jhaygood86/PeachPDF/issues/494">#494</see>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see href="https://www.w3.org/TR/css-tables-3/#repeated-headers">§6.2</see> repeats a group on each
    /// page a table spans <i>“if the header/footer has avoid <c>break-inside</c> applied to it”</i> and
    /// <i>“if the height required to do so is inferior to two quarters of the page height (up to one
    /// quarter for header rows, and up to one quarter for footer rows)”</i>. Neither was applied:
    /// <c>_shouldRepeatHeaders</c> was “the table has a <c>&lt;thead&gt;</c>”.
    /// </para>
    /// <para>
    /// The <c>break-inside</c> condition is behaviour-preserving only because the UA print stylesheet now
    /// carries <c>thead, tfoot { break-inside: avoid }</c> — so the two facts that matter are pinned
    /// separately here: that the sheet supplies it, and that a document taking the opt-out gets a group
    /// laid out once.
    /// </para>
    /// <para>
    /// Two kinds of assertion, deliberately. The decision itself is read off
    /// <see cref="Html.Core.Fragmentation.DetachedRowGroup.Repeats"/>, which is the only way to state a
    /// threshold exactly; what the decision <i>does</i> is stated over the fragment tree, because a flag
    /// that no longer reaches the layout would satisfy the first on its own.
    /// </para>
    /// </remarks>
    public class TableRepeatedGroupConditionsTests
    {
        private const double PageHeight = 300;
        private const double Margin = 20;

        /// <summary>The content band <see cref="Paginate"/>'s page grid gives each fragmentainer.</summary>
        private const double Band = PageHeight - 2 * Margin;

        /// <summary>Comfortably over <see cref="Band"/>/4, and comfortably under <see cref="Band"/>.</summary>
        private const double OverAQuarterOfTheBand = 90;

        private static string Words(int count) =>
            string.Join(" ", Enumerable.Range(0, count).Select(i => $"word{i:0000}"));

        private static string TableWith(string group, string groupStyle = "", string cellContent = "") =>
            $"<table style='width:150pt'><{group} style='{groupStyle}'><tr><td>{cellContent}"
            + $"{group.ToUpperInvariant()}WORD</td></tr></{group}>"
            + "<tbody><tr><td>{W}</td></tr></tbody></table>";

        // ─── The break-inside condition, and the UA rule that makes it behaviour-preserving ──────

        /// <summary>
        /// The UA print stylesheet gives a <c>&lt;thead&gt;</c> and a <c>&lt;tfoot&gt;</c> an avoiding
        /// <c>break-inside</c>, which is what keeps §6.2's first condition from silently switching every
        /// existing document's repeating header off.
        /// </summary>
        /// <remarks>
        /// Read off the detached group's own box rather than from the tree: by the time layout has
        /// finished, the <c>&lt;thead&gt;</c> is out of the table's child list and only
        /// <c>TableSetup</c> and its proxies still point at it.
        /// </remarks>
        [Fact]
        public async Task TheUaStylesheet_GivesATheadAndTfootAvoidBreakInside()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<table style='width:150pt'><thead><tr><th>H</th></tr></thead>"
                + "<tfoot><tr><td>F</td></tr></tfoot>"
                + "<tbody><tr><td>body</td></tr></tbody></table>"));

            var setup = TableOf(root).TableSetup;

            Assert.NotNull(setup);
            Assert.Equal(CssConstants.Avoid, setup!.Header!.Box.BreakInside);
            Assert.Equal(CssConstants.Avoid, setup.Footer!.Box.BreakInside);
        }

        /// <summary>
        /// That UA rule does not reach the group's rows or cells: <c>break-inside</c> is not an inherited
        /// property (css-break-3 §3.2), and the whole approach depends on it staying that way — an
        /// inherited <c>avoid</c> would declare every header cell's content unbreakable.
        /// </summary>
        [Fact]
        public async Task BreakInsideOnAThead_DoesNotReachItsRowsOrCells()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<table style='width:150pt'><thead><tr><th>H</th></tr></thead>"
                + "<tbody><tr><td>body</td></tr></tbody></table>"));

            var header = TableOf(root).TableSetup!.Header!.Box;

            Assert.All(LayoutHarness.Descendants(header).Skip(1),
                box => Assert.Equal(CssConstants.Auto, box.BreakInside));
        }

        /// <summary>
        /// The other half of the same rule: the sheet's <c>h1…h6 { break-after: avoid }</c> still says what
        /// it said when it was spelt <c>page-break-after</c>. The keep-with-next suites cover the
        /// behaviour; nothing pinned it to the sheet's own spelling, which this change alters.
        /// </summary>
        [Fact]
        public async Task TheUaStylesheet_StillGivesHeadingsAnAvoidingBreakAfter()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<h1>One</h1><h3>Three</h3><h6>Six</h6><p>Body</p>"));

            var headings = LayoutHarness.Descendants(root)
                .Where(b => b.HtmlTag?.Name is "h1" or "h3" or "h6")
                .ToList();

            Assert.Equal(3, headings.Count);
            Assert.All(headings, heading => Assert.Equal(CssConstants.Avoid, heading.BreakAfter));
        }

        /// <summary>
        /// A group whose <c>break-inside</c> the author set back to <c>auto</c> is laid out once, in flow,
        /// rather than repeated — §6.2's first condition, and the opt-out the UA rule above exists to be
        /// taken away from.
        /// </summary>
        /// <remarks>
        /// The fixture's single row runs out of room <i>inside its cell</i>, so the later fragmentainers
        /// are opened by passes that resume the table through <c>TableSetup</c> rather than by the row
        /// loop. That makes this the continuation case as well: a decision the first pass took and did not
        /// carry would come back as “repeats” on pass 2, and the group would appear on the later pages.
        /// </remarks>
        [Theory]
        [InlineData("thead")]
        [InlineData("tfoot")]
        public async Task AGroupOptedOutOfAvoidBreakInside_IsDrawnOnceAndNotRepeated(string group)
        {
            var (root, container) = await Paginate(TableWith(group, "break-inside:auto"));

            AssertDrawnOnlyOn(container, $"{group.ToUpperInvariant()}WORD",
                group == "thead" ? First(container) : Last(container));

            Assert.False(RepeatsOf(root, group));
        }

        /// <summary>
        /// With the UA default left alone, the same fixture repeats on every page the table covers — the
        /// behaviour <see href="https://github.com/jhaygood86/PeachPDF/issues/439">#439</see> and
        /// <see href="https://github.com/jhaygood86/PeachPDF/issues/493">#493</see> established, which this
        /// change must not disturb.
        /// </summary>
        [Theory]
        [InlineData("thead")]
        [InlineData("tfoot")]
        public async Task AGroupWithTheUaDefault_StillRepeatsOnEveryPage(string group)
        {
            var (root, container) = await Paginate(TableWith(group));

            Assert.Equal(
                container.FragmentTree!.Fragmentainers.Select(f => f.SlotIndex),
                SlotsDrawnOn(container, $"{group.ToUpperInvariant()}WORD"));

            Assert.True(RepeatsOf(root, group));
        }

        // ─── The quarter-of-the-band condition ───────────────────────────────────────────────────

        /// <summary>
        /// A group taller than a quarter of the page's content band is laid out once instead of repeating,
        /// however emphatically its <c>break-inside</c> avoids — §6.2's second condition.
        /// </summary>
        /// <remarks>
        /// This is the condition that stopped being free. Room for a repeated group is now genuinely
        /// reserved at the band's head (#439) and at its foot (#493), so a tall group is charged its own
        /// height out of every band the table spans; taller than the band, every page would be group and
        /// no page would make progress. The cap is what bounds that.
        /// </remarks>
        [Theory]
        [InlineData("thead")]
        [InlineData("tfoot")]
        public async Task AGroupTallerThanAQuarterOfTheBand_IsDrawnOnceAndNotRepeated(string group)
        {
            var (root, container) = await Paginate(TableWith(
                group, cellContent: $"<div style='height:{OverAQuarterOfTheBand}pt'></div>"));

            AssertDrawnOnlyOn(container, $"{group.ToUpperInvariant()}WORD",
                group == "thead" ? First(container) : Last(container));

            Assert.False(RepeatsOf(root, group));
        }

        /// <summary>
        /// The threshold is strict: §6.2 says the height must be <i>“inferior to”</i> a quarter, so a group
        /// measuring exactly a quarter does not repeat.
        /// </summary>
        /// <remarks>
        /// Self-calibrating rather than hand-computed. A first layout reports what the group really
        /// measured — cell padding, borders and the row's own line height included — and the second is
        /// given the page whose band is exactly four of it. Hand-picking a height instead would pin the
        /// arithmetic of the fixture rather than the comparison.
        /// </remarks>
        [Fact]
        public async Task AGroupExactlyAQuarterOfTheBand_DoesNotRepeat()
        {
            var (measured, _) = await Paginate(TableWith("thead"));
            var height = TableOf(measured).TableSetup!.Header!.Height;

            Assert.True(height > 0, "the fixture's header measured nothing, so the page below is degenerate");

            var (exactly, _) = await LayoutHarness.LayoutAsync(
                Fixture(TableWith("thead")), pageHeight: 4 * height + 2 * Margin, margin: Margin);

            Assert.False(RepeatsOf(exactly, "thead"));
        }

        /// <summary>
        /// The point of the whole thing: a band after the first is left to the rows, rather than still
        /// paying for a group that is no longer drawn on it.
        /// </summary>
        /// <remarks>
        /// The assertion that separates a real fix from a cosmetic one. Gating only the <i>drawing</i>
        /// while leaving the four band reservations alone would satisfy every count above and still charge
        /// each band the group's height — content would simply start below a blank strip. So this is stated
        /// as “the flow reaches the band's own content top”, measured against the same fixture with no
        /// group at all.
        /// </remarks>
        [Fact]
        public async Task ATallHeaderThatDoesNotRepeat_LeavesTheLaterBandsToTheRows()
        {
            var (_, capped) = await Paginate(TableWith(
                "thead", cellContent: $"<div style='height:{OverAQuarterOfTheBand}pt'></div>"));

            var (_, headerless) = await Paginate("<table style='width:150pt'><tbody><tr><td>{W}</td></tr></tbody></table>");

            Assert.Equal(TopOfTheFlowOn(headerless, 1), TopOfTheFlowOn(capped, 1), 3);
        }

        /// <summary>
        /// With no real page grid there is no band to take a quarter of, and the conditions leave the group
        /// alone. The sentinel an unpaginated pass uses is <c>double.MaxValue</c>, a quarter of which
        /// answers nothing.
        /// </summary>
        [Fact]
        public async Task WithNoRealPageGrid_ATallGroupStillRepeats()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                Fixture(TableWith("thead", cellContent: "<div style='height:600pt'></div>")),
                pageHeight: double.MaxValue, margin: 0);

            Assert.True(RepeatsOf(root, "thead"));
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────────────────────

        private static string Fixture(string markup) =>
            LayoutHarness.Wrap(markup.Replace("{W}", Words(244)));

        /// <summary>
        /// Lays <paramref name="markup"/> out over 244 words and checks it really does span more than one
        /// fragmentainer, since nothing above asserts anything if it does not.
        /// </summary>
        private static async Task<(CssBox Root, HtmlContainerInt Container)> Paginate(string markup)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                Fixture(markup), pageHeight: PageHeight, margin: Margin);

            Assert.True(container.FragmentTree!.Fragmentainers.Count > 1,
                "fixture does not paginate, so it asserts nothing");

            return (root, container);
        }

        private static CssBox TableOf(CssBox root) =>
            LayoutHarness.Descendants(root).First(b => b.Display == CssConstants.Table);

        private static bool RepeatsOf(CssBox root, string group)
        {
            var setup = TableOf(root).TableSetup;

            Assert.NotNull(setup);

            var settled = group == "thead" ? setup!.Header : setup!.Footer;

            Assert.NotNull(settled);

            return settled!.Repeats;
        }

        private static int First(HtmlContainerInt container) =>
            container.FragmentTree!.Fragmentainers[0].SlotIndex;

        private static int Last(HtmlContainerInt container) =>
            container.FragmentTree!.Fragmentainers[^1].SlotIndex;

        /// <summary>Which fragmentainer slots hold <paramref name="text"/>, in slot order.</summary>
        private static List<int> SlotsDrawnOn(HtmlContainerInt container, string text) =>
            container.FragmentTree!.Fragmentainers
                .Where(f => Flatten(f.Root).SelectMany(b => b.Words).Any(w => w.Word.Text == text))
                .Select(f => f.SlotIndex)
                .ToList();

        private static void AssertDrawnOnlyOn(HtmlContainerInt container, string text, int slot)
        {
            var drawn = SlotsDrawnOn(container, text);

            Assert.Equal([slot], drawn);
        }

        /// <summary>
        /// Where the topmost body word on <paramref name="slot"/> sits within that fragmentainer. The
        /// group's own words are excluded by construction — every fixture that reaches here names its body
        /// words <c>wordNNNN</c>.
        /// </summary>
        private static double TopOfTheFlowOn(HtmlContainerInt container, int slot)
        {
            var words = Flatten(container.FragmentTree!.Fragmentainers[slot].Root)
                .SelectMany(f => f.Words)
                .Where(w => w.Word.Text?.StartsWith("word", StringComparison.Ordinal) == true)
                .ToList();

            Assert.NotEmpty(words);

            return words.Min(w => w.Rect.Top);
        }

        private static IEnumerable<BoxFragment> Flatten(BoxFragment fragment)
        {
            yield return fragment;

            foreach (var child in fragment.Children)
            {
                foreach (var descendant in Flatten(child)) yield return descendant;
            }
        }
    }
}
