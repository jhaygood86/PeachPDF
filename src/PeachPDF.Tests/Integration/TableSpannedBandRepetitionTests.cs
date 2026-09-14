using PeachPDF.CSS;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A repeating <c>&lt;thead&gt;</c>/<c>&lt;tfoot&gt;</c> on the bands a table spans without breaking on
    /// (<see href="https://github.com/jhaygood86/PeachPDF/issues/509">#509</see>), and the slicing that
    /// makes room for it there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see href="https://www.w3.org/TR/css-tables-3/#repeated-headers">css-tables-3 §6.2</see> repeats the
    /// groups "on each page spanned by a table" and "must leave room" for them. Every proxy site the engine
    /// had was keyed to a break being <i>taken</i>, so a band a row overflows <i>through</i> — the one case
    /// no pass either fills or leaves — got neither the group nor the room.
    /// </para>
    /// <para>
    /// The room is made by slicing the row's own graphical representation, which
    /// <see href="https://www.w3.org/TR/css-break-3/#possible-breaks">css-break-3 §4.3</see> allows in as
    /// many words: "the UA may also fragment the contents of monolithic elements by slicing the element's
    /// graphical representation". The row is neither resized nor moved — each band draws it from a
    /// different origin, so the strips still meet edge to edge.
    /// </para>
    /// <para>
    /// <b>The two assertions that matter are about the seam, not the count.</b> A header drawn on every
    /// page satisfies every count while the strip underneath it repeats or skips content, and no per-word
    /// census can see that — the content here has no words at all. So
    /// <see cref="TheStripsMeetExactly_WithNothingRepeatedOrSkipped"/> states it over the offset into the
    /// box that each band starts and ends at, and <see cref="TheStripsCoverTheWholeRow_AndNoMore"/> states
    /// that they sum to the row itself.
    /// </para>
    /// </remarks>
    public class TableSpannedBandRepetitionTests
    {
        private const double PageHeight = 300;
        private const double Margin = 20;

        /// <summary>
        /// A table whose first body row holds a block taller than the page's whole content band, so there
        /// is no page that could hold it and §4.3 leaves it where it is to overflow.
        /// </summary>
        private static string TallRowTableWith(
            string group, double tall = 700, string groupStyle = "", string groupContent = "") =>
            $"<table style='width:150pt'><{group} style='{groupStyle}'><tr><td>{groupContent}"
            + $"{group.ToUpperInvariant()}WORD</td></tr></{group}><tbody>"
            + $"<tr><td><div style='height:{tall}pt;background:#ddd'></div></td></tr>"
            + "<tr><td>word0001</td></tr><tr><td>word0002</td></tr></tbody></table>";

        // ─── §6.2 on the bands no break falls on ─────────────────────────────────────────────────

        /// <summary>
        /// The group is drawn on every fragmentainer the table covers, including the ones the tall row
        /// merely overflows through — which is #509 itself.
        /// </summary>
        [Theory]
        [InlineData("thead")]
        [InlineData("tfoot")]
        public async Task AGroupIsRepeatedOnEveryBandTheTableSpans(string group)
        {
            var (_, container) = await Paginate(TallRowTableWith(group));

            Assert.Equal(
                container.FragmentTree!.Fragmentainers.Select(f => f.SlotIndex),
                SlotsDrawnOn(container, $"{group.ToUpperInvariant()}WORD"));
        }

        /// <summary>
        /// And it is drawn exactly once per band — the failure a second proxy site is most likely to
        /// introduce, and the one step 5b's read of the live proxies exists to prevent.
        /// </summary>
        [Theory]
        [InlineData("thead")]
        [InlineData("tfoot")]
        public async Task AGroupIsDrawnOnceOnEachOfThem(string group)
        {
            var (root, container) = await Paginate(TallRowTableWith(group));

            var display = group == "thead" ? DisplayMode.TableHeaderGroup : DisplayMode.TableFooterGroup;

            var proxies = TableOf(root).Boxes.OfType<CssProxyBox>()
                .Where(p => p.Display.Value == display)
                .ToList();

            Assert.Equal(container.FragmentTree!.Fragmentainers.Count, proxies.Count);

            var bands = proxies.Select(p => container.SlotStartingAt(p.Location.Y)).ToList();

            Assert.Equal(bands.Count, bands.Distinct().Count());
        }

        /// <summary>
        /// The strip a band shows begins <i>below</i> the header repeated on it, rather than underneath it
        /// — §6.2's "user agents must leave room", which is the half a proxy on its own does not deliver.
        /// </summary>
        [Fact]
        public async Task TheStripOnEachBand_BeginsBelowTheHeaderRepeatedThere()
        {
            var (_, container) = await Paginate(TallRowTableWith("thead"));

            var strips = StripsOfTheTallRow(container);

            Assert.True(strips.Count > 1, "the fixture has to overflow, or this asserts nothing");

            // The first band opens with the table itself, so its header is in flow above the row and is
            // covered by the ordinary path. Every later one is what this change is about.
            foreach (var (slot, strip) in strips.Skip(1))
            {
                var header = HeaderOn(container, slot);

                Assert.True(strip.Top >= header.Bottom - 0.01,
                    $"the strip on band {slot} starts at {strip.Top:F2}, above the repeated header's "
                    + $"bottom edge at {header.Bottom:F2}");
            }
        }

        /// <summary>
        /// Each band picks the row up exactly where the previous one stopped — nothing drawn twice, and
        /// nothing missed between them.
        /// </summary>
        /// <remarks>
        /// Stated as the offset <i>into the row's own box</i> that each strip begins and ends at, which is
        /// the only form that can catch either failure: the box is one rectangle drawn from a different
        /// origin per band, so a wrong displacement moves both edges together and leaves every other
        /// assertion — including a per-word census and a "drawn once per page" count — perfectly satisfied.
        /// </remarks>
        [Fact]
        public async Task TheStripsMeetExactly_WithNothingRepeatedOrSkipped()
        {
            var (_, container) = await Paginate(TallRowTableWith("thead"));

            var strips = StripsOfTheTallRow(container);

            Assert.True(strips.Count > 1, "the fixture has to overflow, or this asserts nothing");

            for (var i = 1; i < strips.Count; i++)
            {
                var (previousSlot, previous) = strips[i - 1];
                var (slot, current) = strips[i];

                Assert.Equal(previous.EndsAt, current.StartsAt, 1);

                Assert.True(current.StartsAt > 0,
                    $"band {slot} begins at offset {current.StartsAt:F2} into the row, so band "
                    + $"{previousSlot} showed nothing");
            }
        }

        /// <summary>
        /// And between them the strips cover the row once over — its whole height, and no more than that.
        /// </summary>
        [Fact]
        public async Task TheStripsCoverTheWholeRow_AndNoMore()
        {
            var (root, container) = await Paginate(TallRowTableWith("thead"));

            var strips = StripsOfTheTallRow(container);
            var row = TallRowOf(root);

            var covered = strips.Sum(s => s.Strip.EndsAt - s.Strip.StartsAt);

            // Against the cells' own depth, not the row's. The row's ActualBottom is the bottom it really
            // reaches on the page, which the gaps the repeated groups open have grown; the cells were
            // never resized, so their extent is the run the strips have to cover between them.
            var depth = row.Boxes.Max(cell => cell.ActualBottom) - row.Location.Y;

            Assert.Equal(depth, covered, 1);

            // And they cover it from its own top, so no part of the row is missed before the first strip.
            Assert.Equal(0, strips[0].Strip.StartsAt, 1);
        }

        // ─── The room is not charged where the group is not drawn ────────────────────────────────

        /// <summary>
        /// A group §6.2 declined gets neither a proxy on those bands nor their room — the "gate both"
        /// rule, on the one path that had no gate at all.
        /// </summary>
        [Theory]
        [InlineData("thead")]
        [InlineData("tfoot")]
        public async Task AGroupDeclinedBySixTwo_IsNotRepeatedOnThemEither(string group)
        {
            var (root, container) = await Paginate(
                TallRowTableWith(group, groupStyle: "break-inside: auto"));

            var display = group == "thead" ? DisplayMode.TableHeaderGroup : DisplayMode.TableFooterGroup;

            Assert.Single(TableOf(root).Boxes.OfType<CssProxyBox>(), p => p.Display.Value == display);

            // And the row is not sliced for room that is never taken. Stated as the absence of a
            // confinement rather than by comparing the row's bottom against the same table without a
            // group, which differs for a reason that has nothing to do with slicing: a declined group is
            // still laid out once, in flow, so the rows below it start a group's height further down.
            Assert.All(RowFragmentsOf(container), fragment => Assert.Null(fragment.Fragment.OverflowClip));
        }

        /// <summary>
        /// A table whose rows all fit the bands they are placed in is untouched — every band it covers is
        /// one a break opened, so there is nothing for step 5b to add and nothing to slice.
        /// </summary>
        [Fact]
        public async Task AnOrdinaryTable_GainsNoProxyAndIsNotSliced()
        {
            var rows = string.Concat(Enumerable.Range(1, 40)
                .Select(i => $"<tr><td>word{i:0000}</td></tr>"));

            var (root, container) = await Paginate(
                "<table style='width:150pt'><thead><tr><th>THEADWORD</th></tr></thead>"
                + $"<tbody>{rows}</tbody></table>");

            var proxies = TableOf(root).Boxes.OfType<CssProxyBox>()
                .Where(p => p.Display.Value == DisplayMode.TableHeaderGroup)
                .ToList();

            // One per page, and no page twice - which is what a duplicate from step 5b would look like.
            Assert.Equal(container.FragmentTree!.Fragmentainers.Count, proxies.Count);

            Assert.All(
                container.FragmentTree.Fragmentainers.Select(f => f.SlotIndex),
                slot => Assert.Single(proxies, p => container.SlotStartingAt(p.Location.Y) == slot));

            Assert.All(RowFragmentsOf(container), fragment => Assert.Null(fragment.Fragment.OverflowClip));
        }

        /// <summary>
        /// A table that breaks between two rows <i>and then</i> overflows through a band gets the groups on
        /// both kinds of page, once each.
        /// </summary>
        /// <remarks>
        /// The two halves of the engine meet here, and only this shape reaches the dedupe that keeps them
        /// apart: with a tall row alone, step 5b runs before any footer proxy exists, so nothing it could
        /// collide with has been drawn yet. Put an ordinary row break in front of the tall row and the
        /// break's own footer is already on the band step 5b is about to consider — which is exactly the
        /// case a set keyed to the live proxies has to get right.
        /// </remarks>
        [Theory]
        [InlineData("thead")]
        [InlineData("tfoot")]
        public async Task ATableThatBreaksAndThenOverflows_GetsTheGroupOnceOnEitherKindOfPage(string group)
        {
            var rows = string.Concat(Enumerable.Range(1, 18)
                .Select(i => $"<tr><td>word{i:0000}</td></tr>"));

            var (root, container) = await Paginate(
                $"<table style='width:150pt'><{group}><tr><td>{group.ToUpperInvariant()}WORD</td></tr>"
                + $"</{group}><tbody>{rows}"
                + "<tr><td><div style='height:700pt;background:#ddd'></div></td></tr>"
                + "<tr><td>word9999</td></tr></tbody></table>");

            var display = group == "thead" ? DisplayMode.TableHeaderGroup : DisplayMode.TableFooterGroup;

            var proxies = TableOf(root).Boxes.OfType<CssProxyBox>()
                .Where(p => p.Display.Value == display)
                .ToList();

            var bands = proxies.Select(p => container.SlotStartingAt(p.Location.Y)).ToList();

            Assert.Equal(bands.Count, bands.Distinct().Count());

            Assert.Equal(
                container.FragmentTree!.Fragmentainers.Select(f => f.SlotIndex),
                SlotsDrawnOn(container, $"{group.ToUpperInvariant()}WORD"));
        }

        /// <summary>
        /// A <c>position: fixed</c> box inside a sliced row keeps repeating at its own coordinates on
        /// every page, rather than travelling with the strip around it.
        /// </summary>
        /// <remarks>
        /// Its containing block is the page box (CSS Position 3), so it is not part of the run being
        /// sliced even though it descends from one. The two halves of a displacement have to agree about
        /// that: dropping the shift from <c>originY</c> alone would still leave the membership test asking
        /// about a displaced rectangle, so the box would be claimed by a band a strip away from the one it
        /// is drawn in — and then confined to that strip as well.
        /// </remarks>
        [Theory]
        [InlineData(20)]
        [InlineData(240)]
        public async Task AFixedBoxInsideASlicedRow_RepeatsAtItsOwnPositionOnEveryPage(double top)
        {
            var (_, container) = await Paginate(
                "<table style='width:150pt'><thead><tr><td>THEADWORD</td></tr></thead><tbody>"
                + "<tr><td><div style='height:700pt;background:#ddd'>"
                + $"<span style='position:fixed;top:{top}pt;left:10pt'>FIXWORD</span>"
                + "</div></td></tr></tbody></table>");

            var drawn = container.FragmentTree!.Fragmentainers
                .Select(f => Flatten(f.Root).SelectMany(b => b.Words)
                    .Where(w => w.Word.Text == "FIXWORD").ToList())
                .ToList();

            Assert.All(drawn, hits => Assert.Single(hits));

            // Identical coordinates on every page - the whole point of `fixed`, and what a leaked
            // displacement would move by a strip's worth on all but the first.
            Assert.All(drawn, hits => Assert.Equal(top, hits[0].Rect.Top, 1));
        }

        /// <summary>
        /// Every band of a sliced run keeps its background and borders, including a last strip shorter
        /// than the gaps opened above it.
        /// </summary>
        /// <remarks>
        /// The whole-box questions are resolved at materialization, and each is a membership question that
        /// has to be asked of where the geometry <b>lands</b>. Asked of the un-displaced rectangle, a last
        /// strip shorter than the accumulated shift tested as being in no band at all, and the block's
        /// background and the cell's border were drawn on <i>neither</i> page — content lost off the edge
        /// of a fragmentainer, which is the outcome §4.3's slicing permission exists to prevent. The
        /// heights here are chosen to land in that window; 700pt (the showcase's) is far outside it, which
        /// is why nothing else here sees it.
        /// </remarks>
        [Theory]
        [InlineData(246)]
        [InlineData(250)]
        [InlineData(490)]
        [InlineData(495)]
        [InlineData(500)]
        public async Task EveryBandOfASlicedRun_KeepsItsDecorations(double tall)
        {
            var (root, container) = await Paginate(
                "<table style='width:150pt'><tfoot><tr><td>TFOOTWORD</td></tr></tfoot><tbody>"
                + $"<tr><td style='border:1pt solid #000'><div id='tall' style='height:{tall}pt;background:#ddd'></div></td></tr>"
                + "<tr><td>word0001</td></tr></tbody></table>");

            var tallBox = LayoutHarness.FindById(root, "tall")!;

            var fragments = container.FragmentTree!.Fragmentainers
                .SelectMany(f => Flatten(f.Root)
                    .Where(b => ReferenceEquals(b.Box, tallBox))
                    .Select(b => (f.SlotIndex, Fragment: b)))
                .ToList();

            Assert.True(fragments.Count > 1, "the fixture has to be sliced, or this asserts nothing");

            Assert.All(fragments, f => Assert.True(f.Fragment.Lines.Count > 0,
                $"the block's fragment on band {f.SlotIndex} has no decoration rectangle, so its "
                + "background is drawn on no page at all"));
        }

        /// <summary>
        /// A <c>rowspan</c> cell taller than a band does not make the row that <i>ends</i> the span the one
        /// that gets sliced.
        /// </summary>
        /// <remarks>
        /// <c>TableRowCursor.MaxBottom</c> is the lowest edge any cell placed so far reached, and a
        /// spanning cell reaches it only on the row that ends the span — a row that does not contain it. A
        /// depth taken from the cursor therefore displaced the wrong row's subtree: measured with a
        /// three-row span holding a 700pt block, the ending row (13pt of text) was stretched to 684pt, the
        /// rows actually holding the tall cell got no strips at all, and the table finished 26.4pt taller
        /// than its content.
        /// </remarks>
        [Fact]
        public async Task ARowspanTallerThanABand_DoesNotSliceTheRowThatEndsIt()
        {
            var (root, _) = await Paginate(
                "<table style='width:200pt'><tfoot><tr><td>TFOOTWORD</td><td>F2</td></tr></tfoot><tbody>"
                + "<tr><td rowspan='3'><div style='height:700pt;background:#ddd'></div></td>"
                + "<td id='r0'>word0000</td></tr>"
                + "<tr><td id='r1'>word0001</td></tr><tr><td id='r2'>word0002</td></tr></tbody></table>");

            var endingRow = LayoutHarness.FindById(root, "r2")!.ParentBox!;

            Assert.True(endingRow.ActualBottom - endingRow.Location.Y < 100,
                $"the row ending the span holds one line, but was stretched to "
                + $"{endingRow.ActualBottom - endingRow.Location.Y:F2}pt");
        }

        /// <summary>
        /// A clipping ancestor <i>outside</i> the sliced row clips at its own bottom edge, not at a
        /// position pushed down by the row's own displacement.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The row's own fragment is what shows this: its nearest clipping ancestor is a wrapping
        /// <c>overflow: hidden</c> `&lt;div&gt;`, found by walking past the table's own containing block —
        /// the row's own cells, by the UA default (<c>td, th { overflow: hidden }</c>), clip their
        /// <i>content</i> before the search ever reaches that wrapper, so the wrapper is not the row's
        /// deepest descendants' clip ancestor and this is the one case that actually reaches it.
        /// </para>
        /// <para>
        /// The row itself is displaced (it is the run being sliced) while the wrapper is not, so its clip
        /// must be localized against the row's <i>slot</i> origin, not its displaced one. Getting this
        /// backwards drew content a repeated footer's worth past the wrapper's own bottom edge — measured
        /// on the very fixture below, on band 1, as a clip 13.2pt taller than the wrapper's own window
        /// there (153.2 instead of 140).
        /// </para>
        /// <para>
        /// The wrapper's window ends inside the row's own second band and is fully behind it by the
        /// third, which is deliberate: a band the wrapper has already ended before is legitimately
        /// clipped to nothing on <i>both</i> the broken and the fixed code, so it would not tell the two
        /// apart, and is excluded rather than asserted on.
        /// </para>
        /// </remarks>
        [Fact]
        public async Task AClipOutsideTheSlicedRow_IsNotPushedDownByItsDisplacement()
        {
            var (root, container) = await Paginate(
                "<div id='wrap' style='overflow:hidden;height:400pt'>"
                + "<table style='width:150pt'><tfoot><tr><td>TFOOTWORD</td></tr></tfoot><tbody>"
                + "<tr><td><div id='tall' style='height:700pt;background:#ddd'></div></td></tr>"
                + "</tbody></table></div>");

            var wrap = LayoutHarness.FindById(root, "wrap")!;
            var row = LayoutHarness.FindById(root, "tall")!.ParentBox!.ParentBox!;

            Assert.Equal(DisplayMode.TableRow, row.Display.Value);

            var displacedFragments = container.FragmentTree!.Fragmentainers
                .SelectMany(f => Flatten(f.Root)
                    .Where(b => ReferenceEquals(b.Box, row))
                    .Select(b => (f.SlotIndex, Fragment: b)))
                .Where(f => f.Fragment.Rect.Top < 0 && f.Fragment.OverflowClip is { Height: > 0 })
                .ToList();

            Assert.True(displacedFragments.Count > 0,
                "the row has to be displaced onto a band where the wrapper's window is still open, "
                + "or this asserts nothing");

            foreach (var (slot, fragment) in displacedFragments)
            {
                var wrapBottomLocal = wrap.ActualBottom - container.PageTopOf(slot) + Margin;

                Assert.True(fragment.OverflowClip!.Value.Bottom <= wrapBottomLocal + 0.51,
                    $"band {slot}: the row's clip bottom {fragment.OverflowClip.Value.Bottom:F2} is past "
                    + $"the overflow:hidden wrapper's own bottom edge at {wrapBottomLocal:F2}");
            }
        }

        /// <summary>
        /// With no real page grid there are no bands to spread the row over, and nothing states geometry
        /// against a grid that does not exist.
        /// </summary>
        [Fact]
        public async Task WithNoRealPageGrid_NothingIsSliced()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(TallRowTableWith("thead")));

            Assert.Single(TableOf(root).Boxes.OfType<CssProxyBox>(),
                p => p.Display.Value == DisplayMode.TableHeaderGroup);

            Assert.All(RowFragmentsOf(container), fragment => Assert.Null(fragment.Fragment.OverflowClip));
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────────────────────

        private static async Task<(CssBox Root, HtmlContainerInt Container)> Paginate(string markup)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(markup), pageHeight: PageHeight, margin: Margin);

            Assert.True(container.FragmentTree!.Fragmentainers.Count > 1,
                "fixture does not paginate, so it asserts nothing");

            return (root, container);
        }

        private static CssBox TableOf(CssBox root) =>
            LayoutHarness.Descendants(root).First(b => b.Display.Value == DisplayMode.Table);

        /// <summary>The body row holding the over-tall block — the first one in every fixture here.</summary>
        private static CssBox TallRowOf(CssBox root) =>
            LayoutHarness.Descendants(root).First(b => b.Display.Value == DisplayMode.TableRow
                                                       && b.Boxes.Count > 0
                                                       && b.Boxes[0].Display.Value == DisplayMode.TableCell
                                                       && b.Boxes[0].Words.Count == 0);

        /// <summary>
        /// Where the header repeated on <paramref name="slot"/> sits in that fragmentainer's own space.
        /// </summary>
        private static RRect HeaderOn(HtmlContainerInt container, int slot)
        {
            var word = Flatten(container.FragmentTree!.Fragmentainers.Single(f => f.SlotIndex == slot).Root)
                .SelectMany(f => f.Words)
                .Single(w => w.Word.Text == "THEADWORD");

            return word.Rect;
        }

        /// <summary>
        /// The strip of the tall row each band shows, as offsets <i>into the row's own box</i> together
        /// with where that strip sits on the page.
        /// </summary>
        /// <remarks>
        /// Read off the fragment's confinement rather than its rectangle, which is the whole box on every
        /// band by construction — a displaced fragment draws the same rectangle from a different origin,
        /// and the confinement is what says which part of it this band shows.
        /// </remarks>
        private static List<(int Slot, (double Top, double Bottom, double StartsAt, double EndsAt) Strip)>
            StripsOfTheTallRow(HtmlContainerInt container) =>
            RowFragmentsOf(container)
                .Where(f => f.Fragment.OverflowClip is not null)
                .OrderBy(f => f.Slot)
                .Select(f =>
                {
                    var clip = f.Fragment.OverflowClip!.Value;

                    return (f.Slot, (clip.Top, clip.Bottom,
                        // How far into the row's own box this band's strip begins and ends. The fragment's
                        // rectangle is the whole row, drawn from this band's origin, so the difference is
                        // the offset the strip picks the row up at.
                        clip.Top - f.Fragment.Rect.Top, clip.Bottom - f.Fragment.Rect.Top));
                })
                .ToList();

        /// <summary>Every fragment of the tall row, with the slot it was emitted in.</summary>
        private static List<(int Slot, BoxFragment Fragment)> RowFragmentsOf(HtmlContainerInt container) =>
            container.FragmentTree!.Fragmentainers
                .SelectMany(f => Flatten(f.Root)
                    .Where(b => b.Box.Display.Value == DisplayMode.TableRow
                                && b.Box.Boxes.Count > 0
                                && b.Box.Boxes[0].Words.Count == 0)
                    .Select(b => (f.SlotIndex, b)))
                .ToList();

        private static List<int> SlotsDrawnOn(HtmlContainerInt container, string text) =>
            container.FragmentTree!.Fragmentainers
                .Where(f => Flatten(f.Root).SelectMany(b => b.Words).Any(w => w.Word.Text == text))
                .Select(f => f.SlotIndex)
                .ToList();

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
