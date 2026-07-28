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

        /// <summary>
        /// Comfortably over <see cref="PageHeight"/>/4 — §6.2's cap is a quarter of the whole page box,
        /// not of the smaller content band — and comfortably under the band, so the group still fits a
        /// page at all.
        /// </summary>
        private const double OverAQuarterOfThePage = 90;

        private static string Words(int count) =>
            string.Join(" ", Enumerable.Range(0, count).Select(i => $"word{i:0000}"));

        /// <summary>
        /// A table whose single body row runs out of room <i>inside its cell</i>, so every fragmentainer
        /// after the first is opened by a pass that resumes the table through <see cref="TableSetup"/>.
        /// </summary>
        private static string TableWith(string group, string groupStyle = "", string cellContent = "") =>
            $"<table style='width:150pt'><{group} style='{groupStyle}'><tr><td>{cellContent}"
            + $"{group.ToUpperInvariant()}WORD</td></tr></{group}>"
            + "<tbody><tr><td>{W}</td></tr></tbody></table>";

        /// <summary>
        /// A table of many short rows, which fragments at a break <i>between two rows</i> — the other
        /// path entirely, and the one an ordinary document takes.
        /// </summary>
        /// <remarks>
        /// <see cref="TableWith"/>'s single row never enters <c>TakeBreakBeforeRow</c>, which is where a
        /// later band's header and footer proxies are written. Both of its gates could be reverted with the
        /// whole suite still green until this shape existed.
        /// </remarks>
        private static string RowsTableWith(string group, string groupStyle = "", string cellContent = "")
        {
            var rows = string.Join("", Enumerable.Range(0, 40).Select(i => $"<tr><td>word{i:0000}</td></tr>"));

            return $"<table style='width:150pt'><{group} style='{groupStyle}'><tr><td>{cellContent}"
                   + $"{group.ToUpperInvariant()}WORD</td></tr></{group}><tbody>{rows}</tbody></table>";
        }

        /// <summary>The same rows with no repeatable group at all — the baseline a declined group must match.</summary>
        private static string RowsTableWithNoGroup() =>
            "<table style='width:150pt'><tbody>"
            + string.Join("", Enumerable.Range(0, 40).Select(i => $"<tr><td>word{i:0000}</td></tr>"))
            + "</tbody></table>";

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

            AssertLaidOutOnce(root, group);

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
        public async Task AGroupTallerThanAQuarterOfThePage_IsDrawnOnceAndNotRepeated(string group)
        {
            var (root, container) = await Paginate(TableWith(
                group, cellContent: $"<div style='height:{OverAQuarterOfThePage}pt'></div>"));

            AssertDrawnOnlyOn(container, $"{group.ToUpperInvariant()}WORD",
                group == "thead" ? First(container) : Last(container));

            AssertLaidOutOnce(root, group);

            Assert.False(RepeatsOf(root, group));
        }

        /// <summary>
        /// The threshold is strict: §6.2 says the height must be <i>“inferior to”</i> a quarter, so a group
        /// measuring exactly a quarter does not repeat.
        /// </summary>
        /// <remarks>
        /// Self-calibrating rather than hand-computed. A first layout reports what the group really
        /// measured — cell padding, borders and the row's own line height included — and the second is
        /// given the page that is exactly four of it. Hand-picking a height instead would pin the
        /// arithmetic of the fixture rather than the comparison.
        /// </remarks>
        [Fact]
        public async Task AGroupExactlyAQuarterOfThePage_DoesNotRepeat()
        {
            var (measured, _) = await Paginate(TableWith("thead"));
            var height = TableOf(measured).TableSetup!.Header!.Height;

            Assert.True(height > 0, "the fixture's header measured nothing, so the page below is degenerate");

            // The harness's pageHeight is the sheet, which is exactly what §6.2's quarter is taken of.
            var (exactly, exactlyContainer) = await LayoutHarness.LayoutAsync(
                Fixture(TableWith("thead")), pageHeight: 4 * height, margin: Margin);

            // Pin the premise before the conclusion. The container recovers the sheet by adding the two
            // margins back onto the band it was given, and each of those rounds - so a change in font
            // metrics, or a platform difference, could leave the quarter an ulp either side of the
            // measured height. An ulp high fails this test for a reason that is not about strictness; an
            // ulp low would let it pass under `<=` as well, and quietly stop testing anything.
            Assert.Equal(height, exactlyContainer.PageSheetHeight / 4);

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
                "thead", cellContent: $"<div style='height:{OverAQuarterOfThePage}pt'></div>"));

            var (_, headerless) = await Paginate("<table style='width:150pt'><tbody><tr><td>{W}</td></tr></tbody></table>");

            Assert.Equal(TopOfTheFlowOn(headerless, 1), TopOfTheFlowOn(capped, 1), 3);
        }

        /// <summary>
        /// The same fact read from the other end of the band, which is where four of the five gated
        /// reservations are: a declined <c>&lt;tfoot&gt;</c> leaves the flow running as far down each band
        /// as it would with no footer at all.
        /// </summary>
        /// <remarks>
        /// Its own test rather than a theory arm of the header's, because the two are asked of opposite
        /// edges. Without it, reverting <c>RepeatedFooterHeight</c> to the raw height — the single change
        /// the repeated-group cost invariant calls load-bearing — left the whole suite green: the footer
        /// would be drawn on one page while every band still paid its height, and content would stop above
        /// a blank strip at each band's foot with nothing to say so.
        /// </remarks>
        [Fact]
        public async Task ATallFooterThatDoesNotRepeat_LeavesTheLaterBandsToTheRows()
        {
            var (_, capped) = await Paginate(TableWith(
                "tfoot", cellContent: $"<div style='height:{OverAQuarterOfThePage}pt'></div>"));

            var (_, footerless) = await Paginate("<table style='width:150pt'><tbody><tr><td>{W}</td></tr></tbody></table>");

            // The first band, not a later one: the footer's room is reserved from the foot of every band
            // the table spans, so the first is already charged for it, and the last carries the table's own
            // closing footer whichever way §6.2 answered.
            Assert.Equal(BottomOfTheFlowOn(footerless, 0), BottomOfTheFlowOn(capped, 0), 3);
        }

        // ─── The break between two rows, which is the other path entirely ────────────────────────

        /// <summary>
        /// A declined group is not redrawn at a break between two rows either — the path an ordinary
        /// many-row table takes, and the one <c>TakeBreakBeforeRow</c> owns.
        /// </summary>
        /// <remarks>
        /// Every other fixture here fragments <i>inside</i> a cell, so each later fragmentainer is opened by
        /// a continuation and <c>TakeBreakBeforeRow</c> never runs. Both of its gates could be reverted with
        /// the full suite green until this existed, which would have redrawn a declined group on every page
        /// of the most ordinary table shape there is.
        /// </remarks>
        [Theory]
        [InlineData("thead", "break-inside:auto", "")]
        [InlineData("tfoot", "break-inside:auto", "")]
        [InlineData("thead", "", "<div style='height:90pt'></div>")]
        [InlineData("tfoot", "", "<div style='height:90pt'></div>")]
        public async Task AGroupDeclinedBySixTwo_IsNotRedrawnAtABreakBetweenTwoRows(
            string group, string groupStyle, string cellContent)
        {
            var (root, _) = await Paginate(RowsTableWith(group, groupStyle, cellContent));

            AssertLaidOutOnce(root, group);

            Assert.False(RepeatsOf(root, group));
        }

        /// <summary>
        /// And the band a break opens is given wholly to the rows, so nothing was reserved there for the
        /// group that is not drawn on it.
        /// </summary>
        /// <remarks>
        /// The reservation half of the test above, against a table with no group at all: a header's room
        /// comes off the band's head, a footer's off its foot, and each is asked at the edge it takes.
        /// </remarks>
        [Theory]
        [InlineData("thead", "break-inside:auto", "")]
        [InlineData("tfoot", "break-inside:auto", "")]
        [InlineData("thead", "", "<div style='height:90pt'></div>")]
        [InlineData("tfoot", "", "<div style='height:90pt'></div>")]
        public async Task ABandOpenedByARowBreak_HoldsAsManyRowsAsItWouldWithNoGroupAtAll(
            string group, string groupStyle, string cellContent)
        {
            var (_, declined) = await Paginate(RowsTableWith(group, groupStyle, cellContent));
            var (_, plain) = await Paginate(RowsTableWithNoGroup());

            if (group == "thead")
            {
                Assert.Equal(TopOfTheFlowOn(plain, 1), TopOfTheFlowOn(declined, 1), 3);
            }
            else
            {
                Assert.Equal(BottomOfTheFlowOn(plain, 0), BottomOfTheFlowOn(declined, 0), 3);
            }
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

        // ─── A declined footer still has to fit the band it is drawn in ──────────────────────────

        /// <summary>
        /// A <c>&lt;tfoot&gt;</c> §6.2 declines to repeat is carried onto the next page rather than drawn
        /// across the foot of the one its last row ended on
        /// (<see href="https://github.com/jhaygood86/PeachPDF/issues/518">#518</see>).
        /// </summary>
        /// <remarks>
        /// <para>
        /// The room a <i>repeating</i> footer needs is reserved out of every band the table spans, so its
        /// last row stops clear of the foot. A declined one gets no such reservation — that is exactly what
        /// declining means — and was placed straight after a last row that may have run all the way down.
        /// </para>
        /// <para>
        /// The row counts are the ones that reproduce it: sweeping 8 to 60 rows over this fixture, 15 of
        /// the 53 put the footer past its band, by up to 64.6pt — and they recur once per page cycle
        /// (13–17, 30–34, …) rather than at a single transition, because what matters is where the last
        /// row happens to land relative to the band's foot. 12 and 20 are neighbours that always fitted,
        /// so the theory fails if the fix ever starts moving a footer that had room.
        /// </para>
        /// </remarks>
        [Theory]
        [InlineData(12)]
        [InlineData(13)]
        [InlineData(14)]
        [InlineData(15)]
        [InlineData(16)]
        [InlineData(17)]
        [InlineData(20)]
        [InlineData(31)]
        public async Task ADeclinedFooter_IsDrawnInsideTheBandItIsPlacedIn(int rows)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                DeclinedFooterTable(rows), pageHeight: PageHeight, margin: Margin);

            var footer = Assert.Single(
                TableOf(root).Boxes.OfType<CssProxyBox>(),
                p => p.Display == CssConstants.TableFooterGroup);

            var slot = container.SlotStartingAt(footer.Location.Y);

            Assert.True(footer.ActualBottom <= container.PageBottomOf(slot) + 0.5,
                $"the footer runs to {footer.ActualBottom} in a band ending at {container.PageBottomOf(slot)}");
        }

        /// <summary>
        /// And it is still drawn exactly once when it moves — carried, not copied.
        /// </summary>
        [Theory]
        [InlineData(14)]
        [InlineData(31)]
        public async Task AFooterCarriedOntoTheNextPage_IsStillDrawnOnlyOnce(int rows)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                DeclinedFooterTable(rows), pageHeight: PageHeight, margin: Margin);

            AssertLaidOutOnce(root, "tfoot");
            AssertDrawnOnlyOn(container, "FOOTWORD", Last(container));
        }

        /// <summary>
        /// A <i>repeating</i> footer is untouched by that correction: its room is reserved at every band's
        /// foot, so it never needed carrying, and the move is deliberately scoped away from it rather than
        /// left to be a no-op by arithmetic.
        /// </summary>
        /// <remarks>
        /// The footer here is a bare cell rather than <see cref="DeclinedFooterTable"/>'s 60pt one, and
        /// that matters. Taking the declined fixture and merely dropping its <c>break-inside: auto</c>
        /// leaves a group measuring 73.2pt against this page's 75pt quarter — 1.8pt of headroom, which
        /// Windows font metrics ate, declining the footer and collapsing the premise this test is built on
        /// (see <c>testing-the-reflow-fixtures-are-platform-sensitive-by-design</c>). A fixture that has to
        /// land on the *repeating* side of a threshold has to be nowhere near it.
        /// </remarks>
        [Theory]
        [InlineData(14)]
        [InlineData(31)]
        public async Task ARepeatingFooter_IsStillClosedUnderTheLastRow(int rows)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<table style='width:150pt'><tfoot><tr><td>FOOTWORD</td></tr></tfoot><tbody>"
                    + string.Join("", Enumerable.Range(0, rows).Select(i => $"<tr><td>word{i:0000}</td></tr>"))
                    + "</tbody></table>"),
                pageHeight: PageHeight, margin: Margin);

            Assert.True(RepeatsOf(root, "tfoot"));

            Assert.Equal(
                container.FragmentTree!.Fragmentainers.Select(f => f.SlotIndex),
                SlotsDrawnOn(container, "FOOTWORD"));
        }

        /// <summary>
        /// The page a carried footer opens gets the header the table repeats, exactly as a page opened by
        /// a break between two rows does — §6.2 repeats the header on every page the table <i>spans</i>,
        /// and that page is one of them.
        /// </summary>
        /// <remarks>
        /// The one combination the other fixtures here cannot reach: a footer §6.2 declines beside a header
        /// it does not. Both conditions are per-group, so a table can easily have one of each — an author
        /// writing <c>tfoot { break-inside: auto }</c> and leaving the <c>&lt;thead&gt;</c> alone gets
        /// exactly this.
        /// </remarks>
        [Theory]
        [InlineData(14)]
        [InlineData(31)]
        public async Task ThePageACarriedFooterOpens_StillGetsTheRepeatingHeader(int rows)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                DeclinedFooterTable(rows).Replace(
                    "<table style='width:150pt'>",
                    "<table style='width:150pt'><thead><tr><th>HEADWORD</th></tr></thead>"),
                pageHeight: PageHeight, margin: Margin);

            Assert.True(RepeatsOf(root, "thead"));
            Assert.False(RepeatsOf(root, "tfoot"));

            var footerSlots = SlotsDrawnOn(container, "FOOTWORD");

            Assert.Equal([Last(container)], footerSlots);

            // The band the footer was carried into is one the table spans, so the header is on it too.
            Assert.Contains(Last(container), SlotsDrawnOn(container, "HEADWORD"));

            var footer = Assert.Single(
                TableOf(root).Boxes.OfType<CssProxyBox>(),
                p => p.Display == CssConstants.TableFooterGroup);

            var slot = container.SlotStartingAt(footer.Location.Y);

            Assert.True(footer.ActualBottom <= container.PageBottomOf(slot) + 0.5,
                $"the footer runs to {footer.ActualBottom} in a band ending at {container.PageBottomOf(slot)}");
        }

        /// <summary>
        /// A table whose <c>&lt;tfoot&gt;</c> §6.2 declines to repeat, over <paramref name="rows"/> short rows.
        /// </summary>
        /// <remarks>
        /// The footer measures 73.2pt against this page's 75pt quarter, which is deliberate and safe: it is
        /// declined by <c>break-inside: auto</c>, and both conditions have to hold to repeat, so the answer
        /// does not depend on which side of the cap the height lands — unlike a fixture that has to come
        /// out repeating. The 60pt block is what makes the footer big enough to overhang a band's foot,
        /// which is the thing being tested.
        /// </remarks>
        private static string DeclinedFooterTable(int rows) =>
            LayoutHarness.Wrap(
                "<table style='width:150pt'><tfoot style='break-inside:auto'><tr><td>"
                + "<div style='height:60pt'></div>FOOTWORD</td></tr></tfoot><tbody>"
                + string.Join("", Enumerable.Range(0, rows).Select(i => $"<tr><td>word{i:0000}</td></tr>"))
                + "</tbody></table>");

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
        /// The group was placed exactly once — one <c>CssProxyBox</c> of its own display type, where a
        /// repeating group gets one per band.
        /// </summary>
        /// <remarks>
        /// Asked of the proxies rather than of which fragmentainers claim the group's word, because the
        /// two questions come apart for a <b>footer</b>. A declined <c>&lt;tfoot&gt;</c> has no room
        /// reserved for it — that is the point — so where the last row ends flush with the band's foot it
        /// straddles the boundary and is legitimately claimed by two fragmentainers while having been drawn
        /// once (<see href="https://github.com/jhaygood86/PeachPDF/issues/518">#518</see>). Windows font
        /// metrics put the row-break fixture in exactly that state; Linux's did not, so a slot assertion
        /// here passes on one platform and fails on the other while the engine does the same thing on both.
        /// The fragment-tree assertions on the mid-cell fixtures still state where the group is drawn; this
        /// states how many times it was placed, which is the claim §6.2's conditions actually make.
        /// </remarks>
        private static void AssertLaidOutOnce(CssBox root, string group)
        {
            var display = group == "thead" ? CssConstants.TableHeaderGroup : CssConstants.TableFooterGroup;

            var proxies = TableOf(root).Boxes
                .OfType<CssProxyBox>()
                .Where(p => p.Display == display)
                .ToList();

            Assert.Single(proxies);
        }

        /// <summary>
        /// Where the topmost body word on <paramref name="slot"/> sits within that fragmentainer. The
        /// group's own words are excluded by construction — every fixture that reaches here names its body
        /// words <c>wordNNNN</c>.
        /// </summary>
        private static double TopOfTheFlowOn(HtmlContainerInt container, int slot) =>
            BodyWordsOn(container, slot).Min(w => w.Rect.Top);

        /// <summary>
        /// How far down <paramref name="slot"/> the flow ran — the question a footer's reservation
        /// answers, where <see cref="TopOfTheFlowOn"/> answers a header's.
        /// </summary>
        private static double BottomOfTheFlowOn(HtmlContainerInt container, int slot) =>
            BodyWordsOn(container, slot).Max(w => w.Rect.Bottom);

        /// <summary>
        /// The body words drawn on <paramref name="slot"/>, in that fragmentainer's own coordinates. The
        /// group's own words are excluded by construction — every fixture that reaches here names its body
        /// words <c>wordNNNN</c> and its group's word <c>THEADWORD</c>/<c>TFOOTWORD</c>.
        /// </summary>
        private static List<TextFragment> BodyWordsOn(HtmlContainerInt container, int slot)
        {
            var words = Flatten(container.FragmentTree!.Fragmentainers[slot].Root)
                .SelectMany(f => f.Words)
                .Where(w => w.Word.Text?.StartsWith("word", StringComparison.Ordinal) == true)
                .ToList();

            Assert.NotEmpty(words);

            return words;
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
