using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A block-level float across page boundaries: laid out in one piece so its own break cannot end the pass
    /// and lose the content beside it, moved whole to the next page when it straddles a boundary but fits on
    /// a page, and never rising above an earlier float that was moved (CSS 2.1 §9.5.1 rule 5).
    /// </summary>
    /// <remarks>
    /// The fixtures use a 300pt-wide, 200pt page with 20pt margins, so page <c>k</c>'s band is
    /// <c>[20 + 160k, 180 + 160k)</c>, and 10pt/12pt text.
    /// </remarks>
    public class FloatsAcrossPagesIntegrationTests
    {
        private const double PageHeight = 200;
        private const double Margin = 20;

        // A block beside a float taller than a page (#1339). The float's break used to end the pass, and every
        // line of the block laid out beside it was placed back on a page already emitted: only the lines after
        // the float ended were drawn. Laid out unbroken, the float is sliced across the pages and the block's
        // lines flow beside it onto each page.
        [Fact]
        public async Task BlockBesideAFloatTallerThanAPage_DrawsEveryLine()
        {
            var placed = await WordFragments(
                $"<div style='float:right;width:80pt'>{Lines("A", 30)}</div><div>{Lines("W", 30)}</div>");

            AssertEachDrawnOnceInsideABand(placed, 30);
        }

        // A float that fills its containing block's width has nothing beside it to lose, so it keeps the breaking
        // path: it breaks cleanly between its lines, and every line is drawn inside a page band. Laid out in one
        // piece it was sliced instead, with the line on each boundary cut in two.
        [Theory]
        [InlineData("width:100%")]
        [InlineData("width:240pt;padding:0 10pt")]
        [InlineData("width:50%;margin-right:50%")]
        public async Task FloatThatFillsItsContainingBlock_BreaksBetweenItsLines(string css)
        {
            var placed = await WordFragments(
                $"<div>{Lines("C", 3)}</div><div style='float:left;{css}'>{Lines("W", 30)}</div><p>after</p>");

            AssertEachDrawnOnceInsideABand(placed, 30);
        }

        // A float that does not fit in what is left of a page, and fits on one page, moves whole to the next
        // one, and the text after it is not drawn twice at the boundary. It used to be broken, and the
        // following block's boundary line was drawn at the foot of one page and again above the next one's
        // top margin.
        [Fact]
        public async Task FloatThatFitsOnAPage_MovesWholeToTheNextPage()
        {
            var placed = await WordFragments(
                $"<div>{Lines("C", 12)}</div><div style='float:right;width:80pt'>{Lines("F", 4)}</div><div>{Lines("W", 30)}</div>");

            AssertEachDrawnOnceInsideABand(placed, 30);
        }

        // The moved float's own top is the next page's top, and a later float is not placed above it
        // (CSS 2.1 §9.5.1 rule 5). The later float's static position is still on the page before, and it
        // used to be placed there, higher than the float it follows.
        [Theory]
        [InlineData("left")]
        [InlineData("right")]
        public async Task MovedFloat_StartsTheNextPage_AndALaterFloatIsNotPlacedAboveIt(string side)
        {
            var (root, container) = await LayoutFloats(
                $"<div>{Lines("C", 11)}</div><div id='f' style='float:{side};width:60pt'>{Lines("F", 4)}</div>" +
                $"<div id='g' style='float:{side};width:60pt'>G1</div><div>{Lines("W", 20)}</div>");
            var moved = LayoutHarness.FindById(root, "f")!;
            var later = LayoutHarness.FindById(root, "g")!;

            Assert.Equal(container.PageTopOf(1), moved.Location.Y, 2);
            Assert.True(later.Location.Y >= moved.Location.Y - 0.01,
                $"the later float's top {later.Location.Y:F2} is above the moved float's {moved.Location.Y:F2}");
            if (side == "left") Assert.True(later.Location.X >= moved.ActualRight - 0.01, "the later float overlaps the moved one");
        }

        // A float holding a footnote call: its note goes wherever the float goes, so the float moves only when
        // it and its note do not fit together, and the answer is the same on every layout attempt. Read off the
        // page's reservation as it stood, it flipped: the note reserved room on page 1, the float moved, the
        // note followed, page 1 was free again, and the loop stopped at its cap with the filler pushed off page 1
        // by a reservation nothing used.
        [Theory]
        [InlineData(1, false)]
        [InlineData(6, true)]
        [InlineData(8, true)]
        [InlineData(9, true)]
        public async Task FloatHoldingAFootnoteCall_MovesOnlyWhenItAndItsNoteDoNotFit(int fillerLines, bool moves)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><head><style>body{margin:0;font:10pt/12pt Arial} p{margin:0} " +
                ".fn{float:footnote;font-size:8pt;line-height:10pt}</style></head><body>" +
                $"<div>{Lines("C", fillerLines)}</div><div id='f' style='float:right;width:80pt'>" +
                "F1<br>F2<span class='fn'>Note one<br>line two<br>line three</span><br>F3<br>F4</div>" +
                $"<div>{Lines("W", 20)}</div></body></html>", pageWidth: 300, pageHeight: PageHeight, margin: Margin);
            var box = LayoutHarness.FindById(root, "f")!;
            var page = container.SlotStartingAt(box.Location.Y);

            Assert.Equal(moves ? 1 : 0, page);
            Assert.Equal([page], container.FootnoteAreaHeightsBySlot.Keys);
            Assert.True(box.ActualBottom <= container.PageBottomOf(page) - container.TotalBandEndReservationFor(page) + 0.01,
                "the float runs into its own note area");

            // Nothing on the page before is pushed off it by room the note no longer takes there.
            var lastFiller = LayoutHarness.Descendants(root).SelectMany(b => b.Words).Where(w => w.Text?.StartsWith('C') == true).Max(w => w.Top);
            Assert.Equal(0, container.SlotStartingAt(lastFiller));
        }

        // An absolutely positioned box keeps its float value but is not a float (CSS 2.1 §9.7), so rule 5 does not
        // hold a later float below it: placed by its offsets far down, it pushed the real float onto page 2.
        [Fact]
        public async Task AbsoluteBoxWithAFloatValue_DoesNotHoldALaterFloatBelowIt()
        {
            var (root, container) = await LayoutFloats(
                "<div><div style='position:absolute;float:left;top:600pt;left:0'>ABS</div>" +
                "<div id='f' style='float:left;width:60pt'>REAL</div><p>TEXT</p></div>");
            var real = LayoutHarness.FindById(root, "f")!;

            Assert.Equal(container.PageTopOf(0), real.Location.Y, 2);
        }

        // Rule 5 across nesting: a float moved to page 2 from inside an earlier block holds a later float in the
        // same formatting context, which is not its sibling, on page 2 as well. The sibling scan missed it, and
        // the later float stayed on page 1, above the earlier one.
        [Theory]
        [InlineData("<div id='b' style='float:right;width:60pt'>B1</div>")]
        [InlineData("<div><div id='b' style='float:right;width:60pt'>B1</div></div>")]
        public async Task FloatMovedFromInsideAnEarlierBlock_HoldsALaterFloatBelowIt(string laterFloat)
        {
            var (root, _) = await LayoutFloats(
                $"<div>{Lines("C", 10)}</div><div><div id='a' style='float:left;width:60pt'>{Lines("A", 4)}</div></div>" +
                $"{laterFloat}<div>{Lines("W", 15)}</div>");
            var moved = LayoutHarness.FindById(root, "a")!;
            var later = LayoutHarness.FindById(root, "b")!;

            Assert.True(later.Location.Y >= moved.Location.Y - 0.01,
                $"the later float's top {later.Location.Y:F2} is above the moved float's {moved.Location.Y:F2}");
        }

        // Rule 5 is enforced in page space only. Every column spans the same Y range, so a float low in column
        // 1 pushed a later float at the top of column 2 down to its own height.
        [Fact]
        public async Task FloatInALaterColumn_IsNotHeldBelowAFloatInAnEarlierOne()
        {
            var (root, _) = await LayoutFloats(
                $"<div style='columns:2;column-fill:auto;height:150pt'><div>{Lines("C", 9)}</div>" +
                $"<div id='a' style='float:left;width:40pt'>A1</div><div>{Lines("D", 5)}</div>" +
                $"<div id='b' style='float:left;width:40pt'>B1</div><div>{Lines("E", 6)}</div></div>");
            var earlier = LayoutHarness.FindById(root, "a")!;
            var later = LayoutHarness.FindById(root, "b")!;

            Assert.True(later.Location.X > earlier.ActualRight, "the later float must be in the second column");
            Assert.True(later.Location.Y < earlier.Location.Y,
                $"the later float at {later.Location.Y:F2} was held down to the earlier one's {earlier.Location.Y:F2}");
        }

        // A multi-column container establishes a formatting context of its own (css-multicol-1 §2), so a float
        // inside it is not held below a float moved to the next page before the container: the container starts
        // on the page before, and its float stays with its column there.
        [Fact]
        public async Task FloatInAMultiColumnContainer_IsNotHeldBelowAFloatMovedBeforeIt()
        {
            var (root, container) = await LayoutFloats(
                $"<div>{Lines("C", 11)}</div><div id='f' style='float:left;width:60pt'>{Lines("F", 4)}</div>" +
                $"<div style='columns:2;column-gap:10pt'><div id='g' style='float:left;width:30pt'>G1</div>{Lines("D", 4)}</div>");
            var moved = LayoutHarness.FindById(root, "f")!;
            var inColumn = LayoutHarness.FindById(root, "g")!;

            Assert.Equal(1, container.SlotStartingAt(moved.Location.Y));
            Assert.Equal(0, container.SlotStartingAt(inColumn.Location.Y));
        }

        // A float inside a flex or grid item, or a table cell, straddling a page boundary: the item's size is
        // fixed by its engine before its content is committed, so a float moved inside it hung out of the item
        // and over the paragraph after the container. It stays inside its item and clear of what follows.
        [Theory]
        [InlineData("<div style='display:flex'><div id='item' style='width:200pt'>{0}</div></div>")]
        [InlineData("<div style='display:grid;grid-template-columns:200pt'><div id='item'>{0}</div></div>")]
        [InlineData("<table><tr><td id='item' style='width:200pt'>{0}</td></tr></table>")]
        public async Task FloatInsideAnEngineItem_StaysInsideItAndClearOfWhatFollows(string shape)
        {
            var inner = $"<div>{Lines("I", 2)}</div><div id='f' style='float:left;width:60pt'>{Lines("F", 4)}</div><div>{Lines("J", 2)}</div>";
            var (root, _) = await LayoutFloats($"<div>{Lines("C", 9)}</div>" + string.Format(shape, inner) + "<p id='after'>AFTER</p>");
            var box = LayoutHarness.FindById(root, "f")!;
            var item = LayoutHarness.FindById(root, "item")!;
            var after = LayoutHarness.FindById(root, "after")!;

            Assert.True(box.ActualBottom <= item.ActualBottom + 0.01, $"the float ends at {box.ActualBottom:F2}, past its item's {item.ActualBottom:F2}");
            Assert.True(box.ActualBottom <= after.Location.Y + 0.01, $"the float ends at {box.ActualBottom:F2}, over the paragraph at {after.Location.Y:F2}");
        }

        // A float moves onto the next page's usable band: below a float: top figure there, not on top of it.
        [Fact]
        public async Task MovedFloat_StartsBelowATopPageFloatOnItsNewPage()
        {
            var (root, container) = await LayoutFloats(
                $"<div>{Lines("C", 11)}</div><div id='f' style='float:left;width:80pt'>{Lines("F", 4)}</div>" +
                $"<div>{Lines("W", 8)}<div id='fig' style='float:top;height:40pt'>FIG</div>{Lines("V", 12)}</div>");
            var moved = LayoutHarness.FindById(root, "f")!;
            var figure = LayoutHarness.FindById(root, "fig")!;

            Assert.Equal(container.SlotStartingAt(figure.Location.Y), container.SlotStartingAt(moved.Location.Y));
            Assert.True(moved.Location.Y >= figure.ActualBottom - 0.01,
                $"the moved float's top {moved.Location.Y:F2} is inside the figure's strip, which ends at {figure.ActualBottom:F2}");
        }

        // A float taller than a page cannot be moved whole, so it stays where it was placed and is sliced.
        [Fact]
        public async Task FloatTallerThanAPage_IsNotMoved()
        {
            var (root, container) = await LayoutFloats(
                $"<div>{Lines("C", 2)}</div><div id='f' style='float:left;width:60pt'>{Lines("F", 20)}</div><div>{Lines("W", 5)}</div>");
            var tall = LayoutHarness.FindById(root, "f")!;

            Assert.Equal(container.PageTopOf(0) + 24, tall.Location.Y, 2);
        }

        // float: outside resolves to the page's outer side, which is the right on the first (right) page and
        // the left on the second. Moved to the second, the float takes that page's side; it used to keep the
        // first page's and sat on the inner side.
        [Fact]
        public async Task MovedOutsideFloat_TakesTheSideOfItsNewPage()
        {
            var (root, container) = await LayoutFloats(
                $"<div>{Lines("C", 11)}</div><div id='f' style='float:outside;width:60pt'>{Lines("F", 4)}</div><div>{Lines("W", 20)}</div>");
            var moved = LayoutHarness.FindById(root, "f")!;

            Assert.Equal(container.PageTopOf(1), moved.Location.Y, 2);
            Assert.Equal(moved.ContainingBlock.ClientLeft, moved.Location.X, 2);
        }

        // A relative offset does not take part in layout (CSS 2.1 §9.4.3), so it neither decides whether the
        // float moves nor is lost when it does. The first float fits where it is placed and stays, though its
        // offset carries it across the boundary; the second straddles, moves, and keeps its offset.
        [Theory]
        [InlineData(8, 30, false)]
        [InlineData(11, 10, true)]
        public async Task RelativeFloat_IsMovedByItsStaticPosition_AndKeepsItsOffset(int lines, int offset, bool moves)
        {
            var (root, container) = await LayoutFloats(
                $"<div>{Lines("C", lines)}</div><div id='f' style='float:left;width:60pt;position:relative;top:{offset}pt'>{Lines("F", 4)}</div>" +
                $"<div>{Lines("W", 20)}</div>");
            var box = LayoutHarness.FindById(root, "f")!;

            var staticTop = moves ? container.PageTopOf(1) : container.PageTopOf(0) + lines * 12;
            Assert.Equal(staticTop, box.StaticTop, 2);
            Assert.Equal(staticTop + offset, box.Location.Y, 2);
        }

        // A float inside a column breaks into the next column, as before. Laid out unbroken, its lines past the
        // column's foot were drawn below the column, outside the page band.
        [Fact]
        public async Task FloatInAColumn_ContinuesInTheNextColumn()
        {
            var placed = await WordFragments(
                $"<div style='columns:2;column-fill:auto;height:150pt;column-gap:10pt'><div>{Lines("C", 10)}</div>" +
                $"<div style='float:left;width:50pt'>{Lines("W", 4)}</div><div>{Lines("D", 14)}</div></div>");

            AssertEachDrawnOnceInsideABand(placed, 4);
        }

        // A float holding a multi-column container keeps the breaking path, because the columns engine needs
        // the fragmentainer that laying the float out unbroken detaches.
        [Fact]
        public async Task FloatHoldingAMultiColumnContainer_PlacesEveryWordInsideAPageBand()
        {
            var placed = await WordFragments(
                $"<div>{Lines("C", 10)}</div><div style='float:left;width:200pt'>" +
                $"<div style='display:none'><div style='columns:2'>x</div></div><div style='columns:2'>{Lines("W", 20)}</div></div>");

            AssertEachDrawnOnceInsideABand(placed, 20);
        }

        // The same holds for a float that is itself the multi-column container. Only its descendants were
        // checked, so it was laid out unbroken and its last lines were lost.
        [Fact]
        public async Task FloatThatIsAMultiColumnContainer_PlacesEveryWordInsideAPageBand()
        {
            var placed = await WordFragments(
                $"<div>{Lines("C", 10)}</div><div style='float:left;width:200pt;columns:2'>{Lines("W", 20)}</div>");

            AssertEachDrawnOnceInsideABand(placed, 20);
        }

        // A float that crosses a page boundary near an auto-height overflow: hidden wrapper: text beside a
        // float inside it, a float's boundary line inside it, and a wrapper beside a floated sibling. Once such
        // a wrapper could break while a float's own break still ended the pass, each lost content. A float is
        // now laid out unbroken, and one that fits on a page moves whole, so these hold whether or not the
        // wrapper breaks. Each W word is drawn once, inside a page band.
        [Theory]
        [InlineData("<div style='overflow:hidden'><div style='float:left;width:100pt'>{0}</div><p>W1</p></div><p>W2</p>", 20, 2)]
        [InlineData("<div style='overflow:hidden'><div style='float:left;width:100pt'>{0}</div>{1}</div>", 45, 10)]
        [InlineData("{2}<div style='overflow:hidden'><div style='float:right;width:80pt'>{3}</div></div><p>W11</p>", 0, 11)]
        [InlineData("<p>x</p><div style='overflow:hidden'>{0}</div><div style='float:right;width:80pt'>F1<br>F2<br>F3<br>F4<br>F5</div><div style='overflow:hidden'>{3}</div>", 9, 4)]
        public async Task WrapperNearAFloat_DrawsEveryWordInsideAPageBand(string shape, int fillerLines, int count)
        {
            var filler = string.Join("<br>", Enumerable.Range(1, fillerLines).Select(i => $"F{i}"));
            var paragraphs = string.Concat(Enumerable.Range(1, count).Select(i => $"<p>W{i}</p>"));
            var nine = string.Concat(Enumerable.Range(1, 9).Select(i => $"<p>P{i}</p>"));
            var lines = string.Join("<br>", Enumerable.Range(1, Math.Min(count, 10)).Select(i => $"W{i}"));
            var html = "<!DOCTYPE html><html><head><style>body{margin:0;font:10pt/12pt Arial} p{margin:0}</style>" +
                       $"</head><body>{string.Format(shape, filler, paragraphs, nine, lines)}</body></html>";

            var (_, container) = await LayoutHarness.LayoutAsync(html, pageWidth: 300, pageHeight: PageHeight, margin: Margin);

            var placed = container.FragmentTree!.Fragmentainers
                .SelectMany(page => Flatten(page.Root).SelectMany(f => f.Words))
                .Where(w => w.Word.Text is { Length: > 1 } text && text[0] == 'W' && char.IsDigit(text[1]))
                .ToList();

            Assert.Equal(Enumerable.Range(1, count).Select(i => $"W{i}"),
                placed.Select(w => w.Word.Text!).Order(WordNumber.Instance));
            Assert.All(placed, w => Assert.True(
                w.Rect.Top >= Margin - 0.01 && w.Rect.Bottom <= PageHeight - Margin + 0.01,
                $"{w.Word.Text} lies outside its page band ({w.Rect.Top:F2}-{w.Rect.Bottom:F2})"));
        }

        private static Task<(CssBox Root, HtmlContainerInt Container)> LayoutFloats(string body) =>
            LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><head><style>body{margin:0;font:10pt/12pt Arial} p{margin:0}</style>" +
                $"</head><body>{body}</body></html>", pageWidth: 300, pageHeight: PageHeight, margin: Margin);

        private static string Lines(string prefix, int count) =>
            string.Join("<br>", Enumerable.Range(1, count).Select(i => $"{prefix}{i}"));

        private static async Task<List<(int Page, string Text, double Top, double Bottom)>> WordFragments(string body)
        {
            var html = "<!DOCTYPE html><html><head><style>body{margin:0;font:10pt/12pt Arial} p{margin:0}</style>" +
                       $"</head><body>{body}</body></html>";
            var (_, container) = await LayoutHarness.LayoutAsync(html, pageWidth: 300, pageHeight: PageHeight, margin: Margin);

            return container.FragmentTree!.Fragmentainers
                .SelectMany((page, index) => Flatten(page.Root).SelectMany(f => f.Words)
                    .Select(w => (index, w.Word.Text ?? "", w.Rect.Top, w.Rect.Bottom)))
                .Where(w => w.Item2.Length > 1 && w.Item2[0] == 'W' && char.IsDigit(w.Item2[1]))
                .ToList();
        }

        private static void AssertEachDrawnOnceInsideABand(List<(int Page, string Text, double Top, double Bottom)> placed, int count)
        {
            Assert.Equal(Enumerable.Range(1, count).Select(i => $"W{i}"), placed.Select(w => w.Text).Order(WordNumber.Instance));
            Assert.All(placed, w => Assert.True(
                w.Top >= Margin - 0.01 && w.Bottom <= PageHeight - Margin + 0.01,
                $"{w.Text} lies outside its page band ({w.Top:F2}-{w.Bottom:F2})"));
        }

        private sealed class WordNumber : IComparer<string>
        {
            public static readonly WordNumber Instance = new();

            public int Compare(string? x, string? y) =>
                int.Parse(x.AsSpan(1)).CompareTo(int.Parse(y.AsSpan(1)));
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
