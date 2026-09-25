using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// What <see href="https://www.w3.org/TR/css-break-3/#monolithic">css-break-3 §2</see>'s monolithic set
    /// does to pagination: a box the spec forbids breaking inside is moved whole to the next fragmentainer
    /// rather than being sliced across the boundary.
    /// </summary>
    /// <remarks>
    /// The fixtures use a 200pt page with 20pt margins, so page <c>k</c>'s band is
    /// <c>[20 + 160k, 180 + 160k)</c>, and set <c>orphans</c>/<c>widows</c> to 1 throughout. Without that
    /// the default of 2 pushes a straddling two-line box wholesale on its own, and every assertion here
    /// would pass whatever the monolithic rule did.
    /// </remarks>
    public class MonolithicContentLayoutIntegrationTests
    {
        private const double PageHeight = 200;
        private const double Margin = 20;

        // The headline case: a card with overflow: auto and a fixed block size is monolithic, so it may
        // not be split. The height is the card's own natural height, so it changes nothing else.
        [Fact]
        public async Task StraddlingScrollContainer_MovesWholeToTheNextPage()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                StraddleDocument("overflow: auto; height: 60pt"), pageHeight: PageHeight, margin: Margin);

            var card = LayoutHarness.FindById(root, "card")!;

            var top = container.PageIndexOf(card.Location.Y + HtmlContainerInt.PageBoundaryEpsilon);
            var bottom = container.PageIndexOf(card.ActualBottom - HtmlContainerInt.PageBoundaryEpsilon);

            Assert.Equal(top, bottom);
            Assert.Equal(container.PageTopOf(top), card.Location.Y, 6);
        }

        // The control: the identical box without the declaration still straddles, so the test above is
        // measuring the rule rather than the fixture.
        [Fact]
        public async Task StraddlingVisibleBox_IsStillSplit()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                StraddleDocument(""), pageHeight: PageHeight, margin: Margin);

            var card = LayoutHarness.FindById(root, "card")!;

            var top = container.PageIndexOf(card.Location.Y + HtmlContainerInt.PageBoundaryEpsilon);
            var bottom = container.PageIndexOf(card.ActualBottom - HtmlContainerInt.PageBoundaryEpsilon);

            Assert.True(bottom > top, "fixture must straddle a page boundary when nothing forbids it");
        }

        // §2 lets a UA treat a scroll container as monolithic only if its block size can be capped
        // (a non-auto height, or a max-height). An auto-height one grows with its content, so it has
        // nothing to clip in the block axis and fragments like any block, as it does in a browser.
        [Theory]
        [InlineData("overflow: hidden")]
        [InlineData("overflow: auto")]
        [InlineData("overflow: scroll")]
        public async Task StraddlingAutoHeightScrollContainer_IsSplit(string css)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                StraddleDocument(css), pageHeight: PageHeight, margin: Margin);

            var card = LayoutHarness.FindById(root, "card")!;

            var top = container.PageIndexOf(card.Location.Y + HtmlContainerInt.PageBoundaryEpsilon);
            var bottom = container.PageIndexOf(card.ActualBottom - HtmlContainerInt.PageBoundaryEpsilon);

            Assert.True(bottom > top, "an auto-height scroll container must split like any block");
            Assert.Equal(140 + Margin, card.Location.Y, 6);
        }

        // A flex or grid item is not a block in block flow, so its auto-height scroll container stays
        // monolithic and its line moves whole, rather than being cut with a line lost at the boundary.
        [Theory]
        [InlineData("display:flex")]
        [InlineData("display:grid;grid-template-columns:60pt")]
        public async Task StraddlingAutoHeightScrollContainerItem_MovesWholeWithItsLine(string containerCss)
        {
            var html = LayoutHarness.Wrap(
                "<div style='height:140pt'>filler</div>" +
                $"<div style='{containerCss}'>" +
                "<div id='card' style='overflow:hidden;orphans:1;widows:1;line-height:20pt;font-size:10pt;width:60pt'>" +
                "Aaa Bbb Ccc Ddd Eee Fff Ggg Hhh</div></div>");

            var (root, container) = await LayoutHarness.LayoutAsync(html, pageHeight: PageHeight, margin: Margin);

            var card = LayoutHarness.FindById(root, "card")!;

            var top = container.PageIndexOf(card.Location.Y + HtmlContainerInt.PageBoundaryEpsilon);
            var bottom = container.PageIndexOf(card.ActualBottom - HtmlContainerInt.PageBoundaryEpsilon);

            Assert.Equal(top, bottom);
            Assert.Equal(container.PageTopOf(top), card.Location.Y, 6);
        }

        // An inline-block is not fragmented inside its line either: its content stays whole, rather than
        // being clipped to nothing when it straddles the boundary.
        [Fact]
        public async Task StraddlingAutoHeightScrollContainerInlineBlock_KeepsItsContent()
        {
            var html = LayoutHarness.Wrap(
                "<div style='height:140pt'>filler</div>" +
                "<div>before <span id='card' style='display:inline-block;overflow:hidden;width:60pt;line-height:20pt;font-size:10pt'>" +
                "Aaa Bbb Ccc Ddd Eee Fff Ggg Hhh</span> after</div>");

            var (root, container) = await LayoutHarness.LayoutAsync(html, pageHeight: PageHeight, margin: Margin);

            var card = LayoutHarness.FindById(root, "card")!;
            Assert.True(card.ActualBottom - card.Location.Y >= 40, "the inline-block must keep its own content height");

            var placed = container.FragmentTree!.Fragmentainers
                .SelectMany(page => Flatten(page.Root))
                .Where(f => f.Box == card || f.Box.ParentBox == card)
                .SelectMany(f => f.Words)
                .Select(w => w.Word.Text)
                .ToList();

            Assert.Contains("Aaa", placed);
            Assert.Contains("Hhh", placed);
        }

        // A float is placed at its assigned position, which cannot carry a break into the next
        // fragmentainer, so an auto-height scroll container float stays monolithic. Letting its content
        // break silently dropped every line after the boundary. Both placement paths are covered: a float
        // among inline content, and a parent holding nothing but floats.
        private const string FloatAmongInlineContent = "<div>before <div id='card' style='float:left;{0}'>{1}</div> after</div>";
        private const string FloatAlone = "<div><div id='card' style='float:left;{0}'>{1}</div></div>";

        // Kept monolithic, its content lays out unbroken and every line is placed, whether the float fits a
        // page (5 lines) or not (12). Where one lands on the boundary it is drawn past the band, as on main:
        // a float's content cannot continue onto the next page at all (the float-pagination gap), which
        // is also why a plain overflow: visible float in the same place drops every later line.
        [Theory]
        [InlineData(FloatAmongInlineContent, 5)]
        [InlineData(FloatAlone, 5)]
        [InlineData(FloatAmongInlineContent, 12)]
        [InlineData(FloatAlone, 12)]
        public async Task StraddlingAutoHeightScrollContainerFloat_PlacesEveryLine(string shape, int count)
        {
            var placed = await FloatLinesPlaced(shape, count);

            Assert.Equal(Enumerable.Range(1, count).Select(i => $"F{i}"), placed.Select(w => w.Word.Text).Distinct());
        }

        // The same holds one level down: a clearfix block inside an inline-block, a float or a vertical block is laid out
        // through that ancestor's assigned position, so it stays monolithic too, and keeps every line.
        [Theory]
        [InlineData("<div>x <div style='display:inline-block;width:60pt'><div id='card' style='{0}'>{1}</div></div></div>")]
        [InlineData("<div><div style='float:left;width:60pt'><div id='card' style='{0}'>{1}</div></div></div>")]
        [InlineData("<div style='writing-mode:vertical-rl;height:150pt'><div id='card' style='writing-mode:horizontal-tb;{0}'>{1}</div></div>")]
        public async Task AutoHeightScrollContainerInsideAnInlineBlockOrFloat_PlacesEveryLine(string shape)
        {
            var placed = await FloatLinesPlaced(shape, count: 5);

            Assert.Equal(Enumerable.Range(1, 5).Select(i => $"F{i}"), placed.Select(w => w.Word.Text).Distinct());
        }

        // A page float (float: top/bottom/snap) is moved to a page edge whole, so an auto-height scroll
        // container that is one, or sits inside one, stays monolithic too.
        [Theory]
        [InlineData("<div><div id='card' style='float:top;{0}'>{1}</div>after</div>")]
        [InlineData("<div><div id='card' style='float:bottom;{0}'>{1}</div>after</div>")]
        [InlineData("<div><div style='float:bottom;width:60pt'><div id='card' style='{0}'>{1}</div></div>after</div>")]
        public async Task AutoHeightScrollContainerPageFloat_KeepsEveryLineInsideAPageBand(string shape)
        {
            var placed = await FloatLinesPlaced(shape, count: 5);

            Assert.Equal(Enumerable.Range(1, 5).Select(i => $"F{i}"), placed.Select(w => w.Word.Text).Distinct());
            Assert.All(placed, w => Assert.True(w.Rect.Top >= Margin - 0.01 && w.Rect.Bottom <= PageHeight - Margin + 0.01,
                $"{w.Word.Text} lies outside its page band ({w.Rect.Top:F2}-{w.Rect.Bottom:F2})"));
        }

        // Whatever ancestor it sits under, a clearfix block straddling a boundary keeps every line: it either
        // breaks under an ancestor that carries the break on, or stays monolithic under one that doesn't.
        // The caption and inline-table rows lost F4-F5 when only named placements were excluded.
        [Theory]
        [InlineData("<div>x <div style='display:inline-flex'><div style='width:60pt'><div id='card' style='{0}'>{1}</div></div></div></div>")]
        [InlineData("<div>x <div style='display:inline-grid'><div style='width:60pt'><div id='card' style='{0}'>{1}</div></div></div></div>")]
        [InlineData("<div>x <table style='display:inline-table'><tr><td><div id='card' style='{0}'>{1}</div></td></tr></table></div>")]
        [InlineData("<table><caption><div id='card' style='{0}'>{1}</div></caption><tr><td>c</td></tr></table>")]
        [InlineData("<div style='display:flex'><div style='width:60pt'><div id='card' style='{0}'>{1}</div></div></div>")]
        [InlineData("<div style='display:grid'><div style='width:60pt'><div id='card' style='{0}'>{1}</div></div></div>")]
        [InlineData("<table><tr><td><div id='card' style='{0}'>{1}</div></td></tr></table>")]
        [InlineData("<div style='columns:2'><div id='card' style='{0}'>{1}</div></div>")]
        [InlineData("<ul><li><div id='card' style='{0}'>{1}</div></li></ul>")]
        public async Task AutoHeightScrollContainerUnderAnyAncestor_PlacesEveryLine(string shape)
        {
            var placed = await FloatLinesPlaced(shape, count: 5);
            Assert.Equal(Enumerable.Range(1, 5).Select(i => $"F{i}"), placed.Select(w => w.Word.Text).Distinct());
        }

        private static async Task<List<TextFragment>> FloatLinesPlaced(string shape, int count)
        {
            var lines = string.Concat(Enumerable.Range(1, count).Select(i => $"<div>F{i}</div>"));
            var html = LayoutHarness.Wrap(
                "<div style='height:95pt'>filler</div>" +
                string.Format(shape, "overflow:hidden;width:60pt;line-height:20pt;font-size:10pt;orphans:1;widows:1", lines));

            var (_, container) = await LayoutHarness.LayoutAsync(html, pageHeight: PageHeight, margin: Margin);

            return container.FragmentTree!.Fragmentainers
                .SelectMany(page => Flatten(page.Root).SelectMany(f => f.Words))
                .Where(w => w.Word.Text?.StartsWith('F') == true)
                .ToList();
        }

        // The case the issue reported: a tall auto-height overflow: hidden wrapper with some top padding.
        // Sliced as monolithic content, the line straddling each page boundary was drawn only on the page
        // its top fell on, in the bottom margin where the page clip hid it. Fragmented, every line lies
        // wholly inside the band of the page that draws it.
        [Theory]
        [InlineData("overflow: hidden")]
        [InlineData("overflow: auto")]
        public async Task TallAutoHeightScrollContainer_DrawsEveryLineInsideAPageBand(string css)
        {
            const int count = 30;
            var html = LayoutHarness.Wrap(
                $"<div id='card' style='{css};padding-top:13pt;line-height:1.6;font-size:10.5pt'>" +
                string.Concat(Enumerable.Range(1, count).Select(i => $"<p style='margin:0'>L{i}</p>")) +
                "</div>");

            var (_, container) = await LayoutHarness.LayoutAsync(html, pageHeight: PageHeight, margin: Margin);

            var placed = container.FragmentTree!.Fragmentainers
                .SelectMany(page => Flatten(page.Root).SelectMany(f => f.Words))
                .Where(w => w.Word.Text?.StartsWith('L') == true)
                .ToList();

            Assert.Equal(
                Enumerable.Range(1, count).Select(i => $"L{i}"),
                placed.Select(w => w.Word.Text).Distinct());

            // Fragment rectangles are page-local, so a page's band is [Margin, PageHeight - Margin).
            Assert.All(placed, w =>
            {
                Assert.True(w.Rect.Top >= Margin - 0.01, $"{w.Word.Text} starts above its page band ({w.Rect.Top:F2})");
                Assert.True(w.Rect.Bottom <= PageHeight - Margin + 0.01,
                    $"{w.Word.Text} ends past its page band ({w.Rect.Bottom:F2})");
            });
        }

        // A wrapper that fragments has to carry the break through everything inside it, and some content
        // is laid out by paths that drop it. Each shape here lost every line past the first boundary (the
        // clearfix float, the .row of floats, the inline-block), the absolutely positioned badge, or the
        // lines of a scroll container inside a multi-column container, once the wrapper was allowed to break
        // around it. Kept monolithic, all of it is laid out and placed.
        private static readonly string ThirtyLines = string.Join("<br>", Enumerable.Range(1, 30).Select(i => $"W{i}"));

        [Theory]
        [InlineData("<div style='overflow:hidden'><div style='float:left;width:100pt'>{0}</div></div><p>AFTER</p>", 30)]
        [InlineData("<div style='overflow:hidden'><div style='float:left;width:45%'>{0}</div><div style='float:right;width:45%'>R</div></div>", 30)]
        [InlineData("<div style='overflow:hidden;position:relative'>{0}<div style='position:absolute;top:5pt;right:0'>W31</div></div>", 31)]
        public async Task AutoHeightScrollContainerAroundContentThatDropsABreak_PlacesEveryLine(string shape, int count)
        {
            var placed = await WordsPlaced(string.Format(shape, ThirtyLines), "W", pageWidth: 300);

            Assert.Equal(Enumerable.Range(1, count).Select(i => $"W{i}"), placed.Order(WordNumber.Instance));
        }

        // An inline-block's content is laid out at its assigned position in the line, which drops the break
        // token too: every line of one that reached the end of the page was lost.
        [Fact]
        public async Task AutoHeightScrollContainerAroundAnInlineBlock_PlacesEveryLine()
        {
            var lines = string.Join("<br>", Enumerable.Range(1, 14).Select(i => $"W{i}"));
            var placed = await WordsPlaced(
                "<div style='overflow:hidden'><p>" + string.Join("<br>", Enumerable.Range(1, 10).Select(i => $"P{i}")) +
                $"</p><div style='display:inline-block;width:45%'><p>{lines}</p></div></div>", "W", pageWidth: 300);

            Assert.Equal(Enumerable.Range(1, 14).Select(i => $"W{i}"), placed.Order(WordNumber.Instance));
        }

        // A float that crosses a page boundary near an auto-height wrapper. While a float's own break ended
        // the pass, the wrapper lost the text beside a float inside it (the text was placed back on the page
        // already emitted), clipped a float's boundary line at the page foot instead of moving it, and,
        // placed beside a floated sibling, lost its first lines and drew the next above the page's top
        // margin. The same held for in-flow content after a tall absolutely positioned first child. Floats
        // and absolutely positioned boxes are now laid out unbroken, and a float that fits on a page moves
        // whole. Each W word is drawn once, inside a page band.
        [Theory]
        [InlineData("<div style='overflow:hidden'><div style='float:left;width:100pt'>{0}</div><p>W1</p></div><p>W2</p>", 20, 2)]
        [InlineData("<div style='overflow:hidden'><div style='float:left;width:100pt'>{0}</div>{1}</div>", 45, 10)]
        [InlineData("{2}<div style='overflow:hidden'><div style='float:right;width:80pt'>{3}</div></div><p>W11</p>", 0, 11)]
        [InlineData("<p>x</p><div style='overflow:hidden'>{0}</div><div style='float:right;width:80pt'>F1<br>F2<br>F3<br>F4<br>F5</div><div style='overflow:hidden'>{3}</div>", 9, 4)]
        [InlineData("<div style='position:absolute;top:5pt;left:0;width:120pt'>{0}</div>{1}", 14, 10)]
        public async Task WrapperNearAFloatOrTallAbsoluteBox_DrawsEveryWordInsideAPageBand(string shape, int fillerLines, int count)
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

        // The clearfix page layout: an auto-height overflow: hidden wrapper holding a short floated menu and a
        // long column of text. The float is laid out unbroken, so the wrapper fragments, and the text line at
        // each page boundary moves to the next page instead of being sliced off it. Kept monolithic for the
        // float, the wrapper lost one line per boundary.
        [Fact]
        public async Task ClearfixWrapperAroundAShortFloat_DrawsEveryTextLineInsideAPageBand()
        {
            var placed = await WordFragments(
                "<div style='overflow:hidden;padding-top:13pt'><div style='float:left;width:60pt'>menu</div>" +
                "<div style='margin-left:70pt'>" + string.Concat(Enumerable.Range(1, 30).Select(i => $"<p>W{i}</p>")) +
                "</div></div>");

            AssertEachDrawnOnceInsideABand(placed, 30);
        }

        // A block beside a float taller than a page, with and without a formatting context of its own. The
        // float's break used to end the pass, and every line of the block laid out beside it was placed back
        // on a page already emitted: only the lines after the float ended were drawn. Laid out unbroken, the
        // float is sliced across the pages and the block's lines flow beside it onto each page.
        [Theory]
        [InlineData("")]
        [InlineData("overflow:hidden")]
        public async Task BlockBesideAFloatTallerThanAPage_DrawsEveryLine(string blockStyle)
        {
            var placed = await WordFragments(
                $"<div style='float:right;width:80pt'>{Lines("A", 30)}</div><div style='{blockStyle}'>{Lines("W", 30)}</div>");

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

        // The absolutely positioned box itself is laid out unbroken: every one of its lines is placed, on
        // the page its slice falls in, rather than the lines after its first page boundary being lost.
        [Fact]
        public async Task TallAbsoluteBox_PlacesEveryOneOfItsOwnLines()
        {
            var lines = string.Join("<br>", Enumerable.Range(1, 37).Select(i => $"W{i}"));
            var placed = await WordsPlaced(
                "<p>P1</p><p>P2</p><p>P3</p>" +
                $"<div style='position:absolute;top:0;right:0;width:60pt'>{lines}</div>" +
                string.Concat(Enumerable.Range(1, 20).Select(i => $"<p>Q{i}</p>")), "W", pageWidth: 300);

            Assert.Equal(Enumerable.Range(1, 37).Select(i => $"W{i}"), placed.Order(WordNumber.Instance));
        }

        [Fact]
        public async Task AutoHeightScrollContainerInsideAMultiColumnContainer_PlacesEveryWord()
        {
            // Lost on a single page, with no page break at all: the break was taken at a column boundary.
            var words = string.Join(' ', Enumerable.Range(1, 31).Select(i => $"W{i}"));
            var tail = string.Join(' ', Enumerable.Range(1, 20).Select(i => $"t{i}"));
            var placed = await WordsPlaced(
                "<p style='margin:0 0 4pt'>a1 a2 a3 a4</p><div style='column-count:2;column-gap:10pt'>" +
                $"<div style='overflow:hidden'><div>{words}</div></div><p style='margin:0 0 4pt'>{tail}</p></div>" +
                "<p>end</p>", "W", pageWidth: 240, pageHeight: 180, margin: 10);

            Assert.Equal(Enumerable.Range(1, 31).Select(i => $"W{i}"), placed.Order(WordNumber.Instance));
        }

        private static async Task<List<string>> WordsPlaced(
            string body, string prefix, double pageWidth = 595, double pageHeight = PageHeight, double margin = Margin)
        {
            var html = "<!DOCTYPE html><html><head><style>body{margin:0;font:10pt/12pt Arial} p{margin:0}</style>" +
                       $"</head><body>{body}</body></html>";
            var (_, container) = await LayoutHarness.LayoutAsync(
                html, pageWidth: pageWidth, pageHeight: pageHeight, margin: margin);

            // Painted, not merely present in the fragment tree: a fragmented scroll container in a column
            // kept every word in its fragments but clipped the first column's to nothing at paint time.
            var painted = new List<string>();
            for (var page = 0; page < container.FragmentTree!.Fragmentainers.Count; page++)
            {
                var recording = new RecordingGraphics(new PeachPDF.Adapters.PdfSharpAdapter());
                FragmentPaintHarness.PaintPage(container, recording, page);
                painted.AddRange(VisiblyDrawnStrings(recording.Log));
            }

            return painted
                .Where(t => t.StartsWith(prefix, StringComparison.Ordinal) && t.Length > prefix.Length
                            && char.IsDigit(t[prefix.Length]))
                .Distinct()
                .ToList();
        }

        // Every string drawn with some part of it inside all the rectangle clips in force when it was drawn.
        // A path clip is popped like a rectangle one, so it holds a place on the stack but tests nothing.
        private static IEnumerable<string> VisiblyDrawnStrings(IEnumerable<PaintOp> log)
        {
            var clips = new Stack<PeachPDF.Html.Adapters.Entities.RRect?>();
            foreach (var op in log)
            {
                switch (op.Kind)
                {
                    case PaintOpKind.PushClip:
                        clips.Push(op.Bounds);
                        break;
                    case PaintOpKind.PushClipPath:
                        clips.Push(null);
                        break;
                    case PaintOpKind.PopClip when clips.Count > 0:
                        clips.Pop();
                        break;
                    case PaintOpKind.DrawString when op.Text is { } text
                                                    && clips.All(c => c is not { } clip || clip.IntersectsWith(op.Bounds)):
                        yield return text;
                        break;
                }
            }
        }

        private sealed class WordNumber : IComparer<string>
        {
            public static readonly WordNumber Instance = new();

            public int Compare(string? x, string? y) =>
                int.Parse(x.AsSpan(1)).CompareTo(int.Parse(y.AsSpan(1)));
        }

        // The two arms share a mover, so they must agree exactly however the box came to straddle -
        // including where it was already split by a *word-level* break before the epilogue ran, which
        // the headline test's own fixture hides. 140pt of filler is the one alignment in this range
        // where the two ways of relocating a box happened to agree anyway, which is why it must not be
        // the only case here.
        //
        // This was a characterization while the mover translated the box: the shift carried the
        // fragmentainer gap along inside it. The box is now laid out again at its new position
        // (EarlyBreak), so the equality below is the invariant rather than a shared defect - see
        // EarlyBreakLayoutIntegrationTests for what each arm now produces.
        [Theory]
        [InlineData(130)]
        [InlineData(140)]
        [InlineData(150)]
        public async Task RelocatedBox_MatchesWhatBreakInsideAvoidAlreadyDoes(double fillerHeight)
        {
            var (monolithic, _) = await LayoutHarness.LayoutAsync(
                GapDocument(fillerHeight, "overflow:auto;height:60pt"), pageHeight: PageHeight, margin: Margin);
            var (avoid, _) = await LayoutHarness.LayoutAsync(
                GapDocument(fillerHeight, "break-inside:avoid"), pageHeight: PageHeight, margin: Margin);

            var a = LayoutHarness.FindById(monolithic, "card")!;
            var b = LayoutHarness.FindById(avoid, "card")!;

            Assert.Equal(b.Location.Y, a.Location.Y, 6);
            Assert.Equal(b.ActualBottom, a.ActualBottom, 6);
            Assert.Equal(
                b.LineBoxes.SelectMany(l => l.Words).Select(w => w.Top),
                a.LineBoxes.SelectMany(l => l.Words).Select(w => w.Top));

            // Both arms relocate the box whole, and neither carries a page gap into it: every line sits
            // one line-height below the last, and the box is exactly as tall as its own content.
            var lineTops = b.LineBoxes.SelectMany(l => l.Words).Select(w => w.Top).Distinct().Order().ToList();

            Assert.All(
                lineTops.Zip(lineTops.Skip(1), (previous, next) => next - previous),
                spacing => Assert.True(spacing <= 20 + 0.5, $"a {spacing:F1}pt step between lines exceeds the 20pt line height"));
        }

        private static string GapDocument(double fillerHeight, string cardCss) =>
            LayoutHarness.Wrap(
                $"<div style='height:{fillerHeight}pt'>filler</div>" +
                $"<div id='card' style='{cardCss};orphans:1;widows:1;line-height:20pt;font-size:10pt;width:60pt'>" +
                "Aaa Bbb Ccc Ddd Eee Fff Ggg Hhh</div>");

        // Every overflow value, on the same card as the headline test, with its own natural height fixed: a
        // fixed height and no max-height is the case §2 names for hidden, and allows for auto and scroll.
        [Theory]
        [InlineData("overflow: scroll; height: 60pt")]
        [InlineData("overflow: auto; height: 60pt")]
        [InlineData("overflow: hidden; height: 60pt")]
        public async Task EveryScrollContainerValue_MovesWhole(string css)
        {
            Assert.False(await CardStaysOnOnePage("height: 60pt"), "the card must straddle without overflow");
            Assert.True(await CardStaysOnOnePage(css));
        }

        // Capped by max-height alone, with content that fits under the cap, a scroll container breaks like a
        // plain block for every overflow value, as Chrome prints it. §2 names only a fixed height with no
        // max-height for hidden, and makes auto and scroll optional. An overflow: hidden box capped by an
        // aspect-ratio breaks too.
        [Theory]
        [InlineData("overflow: scroll; max-height: 1000pt")]
        [InlineData("overflow: auto; max-height: 1000pt")]
        [InlineData("overflow: hidden; max-height: 1000pt")]
        [InlineData("overflow: hidden; height: 1000pt; max-height: 1000pt")]
        [InlineData("overflow: hidden; aspect-ratio: 1")]
        public async Task ScrollContainerWithoutAFixedHeight_Breaks(string css)
        {
            Assert.False(await CardStaysOnOnePage(css));
        }

        // A scroll container that may break but whose content overflows its max-height is kept whole:
        // its clipped lines lie past its end, and a break among them lost the content after the box. Layout
        // notices the clip and lays the document out again with the box monolithic.
        [Theory]
        [InlineData("hidden", 10, 96)]
        [InlineData("hidden", 2, 60)]
        [InlineData("auto", 10, 96)]
        public async Task ScrollContainerWhoseContentOverflowsItsMaxHeight_StaysWhole_AndLosesNothingAfterIt(string overflow, int lines, int cap)
        {
            var (root, container) = await LayoutFloats(
                $"<div>{Lines("C", lines)}</div><div id='card' style='overflow:{overflow};max-height:{cap}pt'>{Lines("X", 30)}</div>" +
                $"<div>{Lines("W", 10)}</div>");
            var card = LayoutHarness.FindById(root, "card")!;

            Assert.Equal(container.SlotStartingAt(card.Location.Y), container.SlotEndingAt(card.ActualBottom));
            Assert.Contains(card, container.ScrollContainersThatClip);

            var placed = container.FragmentTree!.Fragmentainers
                .SelectMany((page, index) => Flatten(page.Root).SelectMany(f => f.Words)
                    .Select(w => (index, w.Word.Text ?? "", w.Rect.Top, w.Rect.Bottom)))
                .Where(w => w.Item2.Length > 1 && w.Item2[0] == 'W' && char.IsDigit(w.Item2[1]))
                .ToList();
            AssertEachDrawnOnceInsideABand(placed, 10);
        }

        // A block size fixed some other way than by max-height caps the box too. These do change the card's
        // size, so the control checks that the resized card still straddles once it is not a scroll
        // container: what moves it whole is the rule, not the new size.
        [Theory]
        [InlineData("height: 60pt")]
        [InlineData("aspect-ratio: 1")]
        public async Task ScrollContainerWithAFixedBlockSize_MovesWhole(string sizeCss)
        {
            Assert.False(await CardStaysOnOnePage(sizeCss), "the resized card must straddle without overflow");
            Assert.True(await CardStaysOnOnePage($"overflow: auto; {sizeCss}"));
        }

        private static async Task<bool> CardStaysOnOnePage(string css)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                StraddleDocument(css), pageHeight: PageHeight, margin: Margin);

            var card = LayoutHarness.FindById(root, "card")!;

            return container.PageIndexOf(card.Location.Y + HtmlContainerInt.PageBoundaryEpsilon)
                   == container.PageIndexOf(card.ActualBottom - HtmlContainerInt.PageBoundaryEpsilon);
        }

        // §2 would have content that fits in no fragmentainer overflow rather than be sliced. Overflowing
        // discards every fragmentainer past the first, so PeachPDF keeps fragmenting instead - a deliberate
        // deviation, pinned here so it reads as a decision rather than an oversight.
        [Fact]
        public async Task ScrollContainerTallerThanTheBand_KeepsFragmentingRatherThanOverflowing()
        {
            var lines = string.Join("", Enumerable.Range(0, 40).Select(i => $"Line{i}<br>"));
            var html = LayoutHarness.Wrap(
                "<div style='height:100pt'>filler</div>" +
                "<div id='card' style='overflow:hidden;orphans:1;widows:1;line-height:20pt;font-size:10pt'>" +
                lines + "</div>");

            var (root, container) = await LayoutHarness.LayoutAsync(html, pageHeight: PageHeight, margin: Margin);
            var card = LayoutHarness.FindById(root, "card")!;

            var top = container.PageIndexOf(card.Location.Y + HtmlContainerInt.PageBoundaryEpsilon);
            var bottom = container.PageIndexOf(card.ActualBottom - HtmlContainerInt.PageBoundaryEpsilon);

            Assert.True(bottom > top, "a box with nowhere to fit must keep fragmenting, not overflow");

            // And nothing was dropped: every line still has a fragment somewhere in the tree.
            var placed = container.FragmentTree!.Fragmentainers
                .SelectMany(f => Flatten(f.Root))
                .SelectMany(f => f.Words)
                .Select(w => w.Word.Text)
                .Where(t => t?.StartsWith("Line") == true)
                .Distinct()
                .Count();

            Assert.Equal(40, placed);
        }

        // A fixed box is emitted in every fragmentainer at identical coordinates, so "move it to the next
        // page" names nothing for it - the mover has to leave it alone however it is styled.
        [Fact]
        public async Task FixedScrollContainer_IsNotRelocated()
        {
            var (plain, plainContainer) = await LayoutHarness.LayoutAsync(
                FixedDocument(""), pageHeight: PageHeight, margin: Margin);
            var (clipped, _) = await LayoutHarness.LayoutAsync(
                FixedDocument("overflow:hidden"), pageHeight: PageHeight, margin: Margin);

            var plainCard = LayoutHarness.FindById(plain, "card")!;
            var clippedCard = LayoutHarness.FindById(clipped, "card")!;

            Assert.True(clippedCard.IsFixed);
            Assert.True(plainContainer.FragmentTree!.Fragmentainers.Count > 1, "fixture must paginate");

            // The fixture straddles the first band's bottom edge, so an in-flow box in this position would
            // be moved. A fixed one is not: it has no next page to be moved to.
            Assert.Equal(plainCard.Location.Y, clippedCard.Location.Y, 6);
        }

        // The trailing block carries the printable content that materializes a later page: a slot no
        // fragment lands in is never materialized (CSS Paged Media 3 §3.2), so a tall but empty filler
        // paginates to nothing.
        private static string FixedDocument(string cardCss) =>
            LayoutHarness.Wrap(
                "<div style='height:400pt'>filler</div><div>tail</div>" +
                $"<div id='card' style='position:fixed;top:140pt;left:0;width:60pt;height:60pt;{cardCss}'>x</div>");

        // A display:none box is never placed: LayoutContents copies its *previous sibling's* Location and
        // ActualBottom instead. Measuring "its" height therefore measures the sibling, and moving it moves
        // coordinates that belong to something else - which inflated the document by a whole page, since
        // ActualSize.Height bounds the fragment builder's slot walk. A hidden panel with overflow: hidden
        // is an ordinary modal or accordion body, not an exotic shape.
        [Fact]
        public async Task HiddenScrollContainer_IsNotRelocated()
        {
            var html = LayoutHarness.Wrap(
                "<div style='height:20pt'>s</div>" +
                "<div id='tall' style='height:150pt'>tall</div>" +
                "<div id='ghost' style='display:none;overflow:hidden'></div>");

            var (root, container) = await LayoutHarness.LayoutAsync(html, pageHeight: PageHeight, margin: Margin);

            var tall = LayoutHarness.FindById(root, "tall")!;
            var ghost = LayoutHarness.FindById(root, "ghost")!;

            // The fixture's point: #tall really does straddle, so the mover is live on this document.
            Assert.True(
                container.PageIndexOf(tall.ActualBottom - HtmlContainerInt.PageBoundaryEpsilon)
                > container.PageIndexOf(tall.Location.Y + HtmlContainerInt.PageBoundaryEpsilon));

            // Untouched: the else branch's copy of its previous sibling's coordinates, exactly as before.
            Assert.Equal(tall.Location.Y, ghost.Location.Y, 6);
            Assert.Equal(tall.ActualBottom, ghost.ActualBottom, 6);

            // And the document is not a page taller than its content, which is what the relocation cost.
            Assert.Equal(tall.ActualBottom - Margin, container.ActualSize.Height, 6);
        }

        // A box exactly as tall as the content band fits a page perfectly, so there is somewhere to move it
        // to. The fits-nowhere exclusion asked ">= the nominal page height" against the wrong band and
        // wrong boundary, and left such a box straddling.
        [Fact]
        public async Task ScrollContainerExactlyAsTallAsTheBand_StillMovesWhole()
        {
            var band = PageHeight - 2 * Margin;
            var html = LayoutHarness.Wrap(
                "<div style='height:60pt'>filler</div>" +
                $"<div id='card' style='overflow:hidden;height:{band}pt'>card</div>");

            var (root, container) = await LayoutHarness.LayoutAsync(html, pageHeight: PageHeight, margin: Margin);
            var card = LayoutHarness.FindById(root, "card")!;

            Assert.Equal(
                container.PageIndexOf(card.Location.Y + HtmlContainerInt.PageBoundaryEpsilon),
                container.PageIndexOf(card.ActualBottom - HtmlContainerInt.PageBoundaryEpsilon));
        }

        /// <summary>
        /// A scroll container too tall for any band, with nothing inside it to fragment, overflows in
        /// place exactly as the word-level case does (<c>MonolithicContent.FitsNoFragmentainer</c>'s
        /// block-axis counterpart) - and content after it has to see a truthful pass cursor, or a later
        /// fragmentation question is answered against a stale band (issue #435). Measured as
        /// <see cref="HtmlContainerInt.CursorSpills"/> staying zero: <c>CssBox.LayoutBlockChildren</c>
        /// steps the cursor once such a child finishes, gated on
        /// <see cref="MonolithicContent.IsMonolithic"/> specifically - an earlier, broader version that
        /// matched every finished block child regressed
        /// <c>FragmentEmitterTests.MaterializedPages_MatchThePrePagedFragmentTreeBehaviour</c>'s
        /// margin-truncation fixture, since an ordinary container (html/body/div) can land deep in the
        /// document through no decision of its own.
        /// </summary>
        [Fact]
        public async Task ContentAfterAScrollContainerTallerThanTheBand_SeesATruthfulCursor()
        {
            var html = LayoutHarness.Wrap(
                "<div style='height:100pt'>filler</div>" +
                "<div id='card' style='overflow:hidden;height:900pt'></div>" +
                "<p>content after the oversized scroll container</p>");

            var (root, container) = await LayoutHarness.LayoutAsync(html, pageHeight: PageHeight, margin: Margin);
            var card = LayoutHarness.FindById(root, "card")!;

            Assert.True(card.ActualBottom - card.Location.Y > PageHeight - 2 * Margin,
                "the fixture must be taller than a whole band, or this asserts nothing");
            Assert.Equal(0, container.CursorSpills);
        }

        // The replaced half of §2 reaches the same outcome by a different route: an <img> is forced inline
        // (DomParser.CorrectReplacedElementBoxes), so it never runs the epilogue's mover at all - its whole
        // word moves through the ordinary per-word fragmentainer check instead
        // (CssRect.WouldStraddleFragmentainer/InlineBreakToken, in CssLayoutEngine.FlowBox), the same path
        // any other word takes. Worth pinning, because the predicate's CssBoxImage/CssBoxSvg arms are
        // unreachable from the mover and it would be easy to read that as the rule not being delivered.
        [Fact]
        public async Task StraddlingImage_MovesWholeThroughTheWordPath()
        {
            var html = LayoutHarness.Wrap(
                "<div style='height:140pt'>filler</div>" +
                $"<p id='p' style='margin:0;orphans:1;widows:1'><img src='{RasterPngFixture.OnePixelDataUri}' " +
                "style='width:20pt;height:60pt'></p>");

            var (root, container) = await LayoutHarness.LayoutAsync(html, pageHeight: PageHeight, margin: Margin);

            // The word belongs to the <img>'s own box, not to the paragraph that flows it.
            var word = LayoutHarness.Descendants(root).SelectMany(b => b.Words).Single(w => w.IsImage);

            Assert.Equal(
                container.PageIndexOf(word.Top + HtmlContainerInt.PageBoundaryEpsilon),
                container.PageIndexOf(word.Bottom - HtmlContainerInt.PageBoundaryEpsilon));
        }

        // ── the fact on the fragment ──────────────────────────────────────────

        [Theory]
        [InlineData("<div id='t' style='overflow:hidden;height:20pt'>text</div>", true)]
        [InlineData("<div id='t' style='overflow:hidden'>text</div>", false)]
        [InlineData("<img id='t' src='" + RasterPngFixture.OnePixelDataUri + "' style='width:10pt;height:10pt'>", true)]
        [InlineData("<div id='t'>text</div>", false)]
        [InlineData("<div id='t' style='display:flex'><span>text</span></div>", false)]
        public async Task Fragment_CarriesWhetherItsBoxIsMonolithic(string markup, bool expected)
        {
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(markup));

            var fragments = container.FragmentTree!.Fragmentainers
                .SelectMany(f => Flatten(f.Root))
                .Where(f => f.Box.HtmlTag?.TryGetAttribute("id") == "t")
                .ToList();

            Assert.NotEmpty(fragments);
            Assert.All(fragments, f => Assert.Equal(expected, f.IsMonolithic));
        }

        // Every fragment of one box agrees, since this is a property of the box rather than of the piece.
        [Fact]
        public async Task EveryFragmentOfASplitBox_AgreesOnTheFact()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                StraddleDocument(""), pageHeight: PageHeight, margin: Margin);

            var fragments = container.FragmentTree!.Fragmentainers
                .SelectMany(f => Flatten(f.Root))
                .Where(f => f.Box.HtmlTag?.TryGetAttribute("id") == "card")
                .ToList();

            Assert.True(fragments.Count > 1, "fixture must produce more than one fragment");
            Assert.All(fragments, f => Assert.False(f.IsMonolithic));
        }

        // css-break-3 §2: a scroll container taller than any single fragmentainer cannot be moved whole
        // anywhere (there is nowhere it fits), so it must overflow instead of being sliced (#350) - a
        // forced break inside it is one form of slicing, so it must not take effect there either.
        [Fact]
        public async Task ScrollContainerTallerThanAnyPage_IgnoresAForcedBreakInsideIt_AndSpansSeveralPagesInstead()
        {
            const int linesEachSide = 10;
            var html = LayoutHarness.Wrap(
                "<div id='card' style='overflow:auto;height:462pt;margin:0;line-height:22pt;font-size:10pt'>" +
                string.Join("<br>", Enumerable.Range(0, linesEachSide).Select(i => $"Before{i}")) +
                "<p id='afterBreak' style='break-before:page;margin:0'>After</p>" +
                string.Join("<br>", Enumerable.Range(0, linesEachSide).Select(i => $"After{i}")) +
                "</div>");

            var (root, container) = await LayoutHarness.LayoutAsync(html, pageHeight: PageHeight, margin: Margin);

            var card = LayoutHarness.FindById(root, "card")!;
            var afterBreak = LayoutHarness.FindById(root, "afterBreak")!;

            // The card must genuinely be too tall for any one page, or this isn't exercising #350 at all.
            Assert.True(card.ActualBottom - card.Location.Y > PageHeight - 2 * Margin,
                "fixture must be taller than a single page's own band");

            // No blank page/gap from an honored forced break: "After" starts immediately where the
            // preceding line content naturally ends, not at the top of a fresh page.
            Assert.Equal(card.Location.Y + linesEachSide * 22, afterBreak.Location.Y, 2);

            // ...and the card really does span more than one page as a result.
            var top = container.PageIndexOf(card.Location.Y + HtmlContainerInt.PageBoundaryEpsilon);
            var bottom = container.PageIndexOf(card.ActualBottom - HtmlContainerInt.PageBoundaryEpsilon);
            Assert.True(bottom > top, "fixture must actually span more than one page");
        }

        // The control: a cell is excluded from the scroll-container rule by its display rather than by
        // its overflow value, because a cell's own fragmentation across pages is
        // CssLayoutEngineTable's own long-standing feature and must not be suppressed by that rule.
        [Fact]
        public async Task TableCellTallerThanAnyPage_StillFragmentsNormally_NotTreatedAsMonolithic()
        {
            const int lines = 20;
            var html = LayoutHarness.Wrap(
                "<table style='width:100%'><tr><td id='cell' style='line-height:22pt;font-size:10pt'>" +
                string.Join("<br>", Enumerable.Range(0, lines).Select(i => $"Line{i}")) +
                "</td></tr></table>");

            var (root, container) = await LayoutHarness.LayoutAsync(html, pageHeight: PageHeight, margin: Margin);

            var cell = LayoutHarness.FindById(root, "cell")!;
            var top = container.PageIndexOf(cell.Location.Y + HtmlContainerInt.PageBoundaryEpsilon);
            var bottom = container.PageIndexOf(cell.ActualBottom - HtmlContainerInt.PageBoundaryEpsilon);

            Assert.True(bottom > top, "fixture must span more than one page, fragmenting normally");
            Assert.True(container.FragmentainerPasses > 1,
                $"a cell straddling pages must still resume across real passes, got {container.FragmentainerPasses}");
        }

        // A monolithic box holding only inline content dispatches to CreateLineBoxes (ContainsInlinesOnly
        // is checked before EstablishesMultiColumnContext in CssBox.LayoutContents), never reaching
        // CssLayoutEngineColumns at all - so excluding every multi-column box from suppression, rather
        // than only one that will actually dispatch to that engine, wrongly left this shape unsuppressed.
        [Fact]
        public async Task ScrollContainerEstablishingColumnsButHoldingOnlyInlineContent_IsStillSuppressed()
        {
            var html = LayoutHarness.Wrap(
                "<div id='card' style='overflow:auto;height:660pt;columns:2;margin:0;line-height:22pt;font-size:10pt'>" +
                string.Join("<br>", Enumerable.Range(0, 30).Select(i => $"Line{i}")) +
                "</div>");

            var (root, container) = await LayoutHarness.LayoutAsync(html, pageHeight: PageHeight, margin: Margin);

            var card = LayoutHarness.FindById(root, "card")!;
            Assert.True(card.EstablishesMultiColumnContext, "fixture must establish a multi-column context");

            // A single, unbroken pass - the same signature genuinely monolithic content gets - rather than
            // the several real per-fragmentainer passes ordinary (suppressed-or-not) inline content would
            // otherwise resume across.
            Assert.Equal(1, container.FragmentainerPasses);
            Assert.Equal(card.Location.Y + 30 * 22, card.ActualBottom, 2);

            var top = container.PageIndexOf(card.Location.Y + HtmlContainerInt.PageBoundaryEpsilon);
            var bottom = container.PageIndexOf(card.ActualBottom - HtmlContainerInt.PageBoundaryEpsilon);
            Assert.True(bottom > top, "fixture must actually span more than one page");
        }

        // ── helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// A 60pt three-line card starting at 160pt — 20pt above the first band's 180pt bottom edge — so it
        /// straddles the boundary unless something forbids it.
        /// </summary>
        private static string StraddleDocument(string cardCss) =>
            LayoutHarness.Wrap(
                "<div style='height:140pt'>filler</div>" +
                $"<div id='card' style='{cardCss};orphans:1;widows:1;line-height:20pt;font-size:10pt;width:60pt'>" +
                "Aaa Bbb Ccc Ddd Eee Fff Ggg Hhh</div>");

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
