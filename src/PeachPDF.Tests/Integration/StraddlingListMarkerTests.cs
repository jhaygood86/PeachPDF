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
    /// An <i>outside</i> <c>::marker</c> belongs to the fragmentainer its list item <b>begins</b> in, and
    /// that is settled the moment the item is placed. Positioned by the item's epilogue instead, it was
    /// positioned only on the pass that <i>completed</i> the item — a later pass, for an item that straddles
    /// a fragmentainer boundary, than the one whose slot the marker's own coordinates fall in. That slot was
    /// frozen by then and nothing re-opened it, so the marker was claimed by no fragment at all and did not
    /// paint on any page (issue #444).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The margin is production's (<c>PdfGenerateConfig</c>'s 10pt) rather than
    /// <see cref="LayoutHarness"/>'s tidier 20pt default — the same reason <c>UnreachedWordClaimTests</c>
    /// records, since a fixture that picks its margin for tidiness can hide a whole family of defects here.
    /// </para>
    /// <para>
    /// A straddling item is <i>guaranteed</i> rather than hoped for: the middle item is long enough to span
    /// several pages by itself, so which item breaks is not a function of the platform's font metrics. The
    /// issue's own fixture — 40 items of 60 words each, where <c>li26</c> is the one that breaks — reproduces
    /// the same thing, but only at whatever line geometry the default font happens to give, so it is not what
    /// is pinned here. The line geometry that <i>is</i> pinned (a 20pt line against an 830pt band, plus
    /// <c>orphans: 1; widows: 1</c>) leaves every page's last line 10pt clear of the boundary, keeping these
    /// fixtures out of the 0.5pt window where layout and the emitter disagree about membership (#446) — a
    /// different defect, reached on <c>windows-latest</c> only.
    /// </para>
    /// </remarks>
    public class StraddlingListMarkerTests
    {
        private const string ItemStyle = "margin:0;font-size:10pt;line-height:20pt;orphans:1;widows:1";

        /// <summary>
        /// #374's claimed-exactly-once invariant, over the whole document. A marker is a thing that can be
        /// claimed <i>zero</i> times, which is the direction a duplicate-only check would miss.
        /// </summary>
        [Fact]
        public async Task AListItemStraddlingAPageBoundary_ClaimsEveryWordExactlyOnce()
        {
            var (root, container) = await LayoutAsync();

            AssertSomeItemStraddles(root, container);

            var authored = LayoutHarness.Descendants(root).SelectMany(b => b.Words).ToList();
            var claims = ClaimsByWord(container);

            Assert.NotEmpty(authored);
            Assert.All(authored, w => Assert.True(
                claims.TryGetValue(w, out var slots) && slots.Count == 1,
                $"'{w.Text}' is claimed by [{(claims.TryGetValue(w, out var s) ? string.Join(",", s) : "")}]"));
            Assert.Equal(authored.Count, claims.Count);
        }

        /// <summary>
        /// The same statement narrowed to the markers, which is where it fails: every item's marker is
        /// claimed, and by the fragmentainer the item's own first fragment is in rather than by whichever
        /// one it happened to finish in.
        /// </summary>
        [Fact]
        public async Task AStraddlingItemsMarker_IsClaimedByTheFragmentainerItsItemBeginsIn()
        {
            var (root, container) = await LayoutAsync();

            var straddler = AssertSomeItemStraddles(root, container);
            var claims = ClaimsByWord(container);

            foreach (var item in ListItems(root))
            {
                var marker = item.Boxes.Single(b => b.IsMarkerPseudoElement);
                var word = Assert.Single(marker.Words);

                Assert.True(claims.TryGetValue(word, out var slots),
                    $"the marker of '{Id(item)}' is claimed by no fragment at all");
                Assert.Equal([SlotsOf(container, item).First()], slots!);
            }

            // Stated of the straddling item by name too, so the assertion above cannot pass vacuously on a
            // fixture where nothing broke.
            Assert.True(SlotsOf(container, straddler).Count > 1);
        }

        /// <summary>
        /// The visible symptom, asked of the paint calls themselves: a lost marker is not a mispositioned
        /// bullet, it is a bullet that is never drawn on any page. Numbered so each marker is identifiable
        /// in the log by its own text.
        /// </summary>
        [Fact]
        public async Task EveryMarker_IsDrawnOnExactlyOnePage()
        {
            var (root, container) = await LayoutAsync(listStyleType: "decimal");

            AssertSomeItemStraddles(root, container);

            var drawn = new List<string>();

            for (var page = 0; page < container.FragmentTree!.Fragmentainers.Count; page++)
            {
                var g = new TestRecordingGraphics();
                FragmentPaintHarness.PaintPage(container, g, page);

                drawn.AddRange(g.Log
                    .OfType<TestRecordingGraphics.DrawStringCall>()
                    .Select(c => c.Text));
            }

            foreach (var item in ListItems(root))
            {
                var label = item.Boxes.Single(b => b.IsMarkerPseudoElement).Text;

                Assert.Equal(1, drawn.Count(t => t == label));
            }
        }

        /// <summary>
        /// The fix moves <i>when</i> the marker is positioned, not where: it still sits against the item's
        /// own border box (CSS 2.1 §12.5.1), for an item that breaks exactly as for one that does not.
        /// </summary>
        [Fact]
        public async Task AMarkerSitsAgainstItsItemsBorderBox_WhetherOrNotTheItemBreaks()
        {
            var (root, container) = await LayoutAsync();

            var straddler = AssertSomeItemStraddles(root, container);

            var offsets = new List<double>();

            foreach (var item in ListItems(root))
            {
                var marker = item.Boxes.Single(b => b.IsMarkerPseudoElement);
                var word = Assert.Single(marker.Words);

                // Beside the item's first line, and outside its content edge.
                Assert.InRange(word.Top, item.Location.Y, item.Location.Y + item.ActualLineHeight);
                Assert.True(word.Right <= item.ClientLeft + 0.001,
                    $"the marker of '{Id(item)}' overlaps its item's content edge");

                offsets.Add(word.Top - item.Location.Y);
            }

            // The straddling item's marker is offset from its own item exactly as every other item's is,
            // which is the statement that it was not positioned against something else.
            Assert.Single(offsets.Select(o => Math.Round(o, 3)).Distinct());
            Assert.Contains(straddler, ListItems(root));
        }

        /// <summary>
        /// The page grid's answer does not transfer to a column: a box that does not finish in a column is
        /// laid out <i>again</i> at the next column's inline position, so only its last fragment's geometry
        /// survives and the marker has to be positioned on the pass that completes it. Positioned on the
        /// pass that starts it instead, the marker of the item that crosses the column boundary stayed in
        /// the column its item had left — a bullet beside nothing in column 1, and none at all beside the
        /// item's own text in column 2, on a single page with no page break anywhere in it.
        /// </summary>
        /// <remarks>
        /// The item is the one thing a column-boundary fixture cannot pin: it is settled by how much text
        /// fits, so this asserts over <i>every</i> item rather than naming one, and checks separately that
        /// some item really did cross into the second column.
        /// </remarks>
        [Fact]
        public async Task AnItemCrossingAColumnBoundary_KeepsItsMarkerBesideItsOwnColumn()
        {
            var items = string.Join("", Enumerable.Range(0, 4).Select(i =>
                $"<li id='li{i}' style='{ItemStyle}'>"
                + string.Join(" ", Enumerable.Range(0, 25).Select(w => $"i{i}w{w}"))
                + "</li>"));

            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    $"<div style='column-count:2'><ul style='margin:0;padding-left:40pt'>{items}</ul></div>"),
                pageHeight: 260, margin: 10);

            var claims = ClaimsByWord(container);
            var columns = new List<double>();
            var offsets = new List<double>();

            foreach (var item in ListItems(root))
            {
                var word = Assert.Single(item.Boxes.Single(b => b.IsMarkerPseudoElement).Words);

                Assert.True(claims.TryGetValue(word, out var slots),
                    $"the marker of '{Id(item)}' is claimed by no fragment at all");
                Assert.Single(slots!);

                columns.Add(item.Location.X);
                offsets.Add(item.ClientLeft - word.Left);
            }

            Assert.True(columns.Distinct().Count() > 1,
                "the fixture must put some item in the second column");

            // Every marker hangs the same distance outside its own item's content edge — which is exactly
            // what a marker left behind in the previous column does not do.
            Assert.Single(offsets.Select(o => Math.Round(o, 3)).Distinct());
        }

        /// <summary>
        /// A pass that <i>declines</i> to place the item — §5.2's margin truncation concluding the break
        /// falls before it — has written no position for the marker to sit against, so it must not be
        /// positioned there. The pass that does place the item is the one that positions it, and the claim
        /// still stands exactly once.
        /// </summary>
        [Fact]
        public async Task AnItemWhoseFirstPassDeclinedToPlaceIt_StillClaimsItsMarkerExactlyOnce()
        {
            // A margin big enough to carry the item across a page boundary by itself is §5.2's own case:
            // the margin is truncated and the item starts flush at the next boundary instead.
            var html = LayoutHarness.Wrap(
                "<ul style='margin:0;padding-left:40pt'>"
                + $"<li id='first' style='{ItemStyle}'>first item</li>"
                + $"<li id='pushed' style='{ItemStyle};margin-top:900pt'>pushed by its own margin</li></ul>");

            var (root, container) = await LayoutHarness.LayoutAsync(html, pageHeight: 850, margin: 10);

            var pushed = LayoutHarness.FindById(root, "pushed")!;
            var claims = ClaimsByWord(container);

            Assert.True(container.FragmentTree!.Fragmentainers.Count > 1,
                "the fixture must span more than one page");
            Assert.True(SlotsOf(container, pushed).First() > 0,
                "the pushed item must land on a later page than the one it was declined on");

            var word = Assert.Single(pushed.Boxes.Single(b => b.IsMarkerPseudoElement).Words);

            Assert.True(claims.TryGetValue(word, out var slots),
                "the pushed item's marker is claimed by no fragment at all");
            Assert.Equal([SlotsOf(container, pushed).First()], slots!);
        }

        /// <summary>
        /// Three items, the middle one long enough to run over several pages, so exactly one of them
        /// straddles and it does so whatever the platform's text measurement says.
        /// </summary>
        private static Task<(CssBox Root, HtmlContainerInt Container)> LayoutAsync(string listStyleType = "disc")
        {
            var items = string.Join("", new[] { 12, 1200, 12 }.Select((words, i) =>
                $"<li id='li{i}' style='{ItemStyle}'>"
                + string.Join(" ", Enumerable.Range(0, words).Select(w => $"i{i}w{w}"))
                + "</li>"));

            return LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    $"<ul style='margin:0;padding-left:40pt;list-style-type:{listStyleType}'>{items}</ul>"),
                pageHeight: 850, margin: 10);
        }

        /// <summary>
        /// The fixture's precondition, returned so a test can name the item it is really about: some list
        /// item's own content spans more than one fragmentainer.
        /// </summary>
        private static CssBox AssertSomeItemStraddles(CssBox root, HtmlContainerInt container)
        {
            Assert.True(container.FragmentTree!.Fragmentainers.Count > 1,
                "the fixture must span more than one page");

            var straddler = ListItems(root).FirstOrDefault(item => SlotsOf(container, item).Count > 1);

            Assert.NotNull(straddler);
            return straddler;
        }

        private static List<CssBox> ListItems(CssBox root) =>
            LayoutHarness.Descendants(root)
                .Where(b => b.Display == CssConstants.ListItem)
                .ToList();

        private static string? Id(CssBox box) => box.HtmlTag?.TryGetAttribute("id");

        /// <summary>The pagination slots <paramref name="box"/> produced a fragment in, in order.</summary>
        private static List<int> SlotsOf(HtmlContainerInt container, CssBox box) =>
            container.FragmentTree!.Fragmentainers
                .Where(f => Flatten(f.Root).Any(x => ReferenceEquals(x.Box, box)))
                .Select(f => f.SlotIndex)
                .ToList();

        private static Dictionary<CssRect, List<int>> ClaimsByWord(HtmlContainerInt container)
        {
            var claims = new Dictionary<CssRect, List<int>>(ReferenceEqualityComparer.Instance);

            foreach (var fragmentainer in container.FragmentTree!.Fragmentainers)
            {
                foreach (var word in Flatten(fragmentainer.Root).SelectMany(f => f.Words))
                {
                    if (!claims.TryGetValue(word.Word, out var slots))
                    {
                        claims[word.Word] = slots = [];
                    }

                    slots.Add(fragmentainer.SlotIndex);
                }
            }

            return claims;
        }

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
