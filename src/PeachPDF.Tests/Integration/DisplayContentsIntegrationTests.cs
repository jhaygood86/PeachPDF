using PeachPDF.CSS;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Pdf.Structure;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <c>display: contents</c> (<see href="https://www.w3.org/TR/css-display-3/#valdef-display-contents">CSS
    /// Display 3 §2.5</see>): the element generates no box, and is treated for box generation and layout as if
    /// it had been replaced by its contents - while everything that is not about the box tree (its id, its
    /// links, its structure, its <c>string-set</c>) still belongs to the element.
    /// </summary>
    public class DisplayContentsIntegrationTests
    {
        private const string Blue = "rgb(0, 0, 255)";

        private static string Box(string id, double width = 50, double height = 10) =>
            $"<div id='{id}' style='width:{width}pt;height:{height}pt'></div>";

        // ─── Layout: the element is replaced by its contents ─────────────────────────────────────

        [Fact]
        public async Task FlexContainer_ChildrenOfAContentsWrapper_AreItsFlexItems()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='f' style='display:flex;width:300pt'>" +
                $"<div id='w' style='display:contents'>{Box("a")}{Box("b")}</div>{Box("c")}</div>"));

            var a = LayoutHarness.FindById(root, "a")!;
            var b = LayoutHarness.FindById(root, "b")!;
            var c = LayoutHarness.FindById(root, "c")!;

            Assert.Equal(a.Location.Y, b.Location.Y, 0.5);
            Assert.Equal(a.Location.X + 50, b.Location.X, 0.5);
            Assert.Equal(b.Location.X + 50, c.Location.X, 0.5);

            // The wrapper is in no box's children: it generated none.
            Assert.Null(LayoutHarness.FindById(root, "w"));
            Assert.Same(LayoutHarness.FindById(root, "f"), a.ParentBox);
        }

        [Fact]
        public async Task FlexLayout_WithAContentsWrapper_EqualsTheSameLayoutWithoutIt()
        {
            var style = "<style>#f{display:flex;width:300pt;justify-content:space-between}</style>";
            var (withWrapper, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                style + $"<div id='f'><div style='display:contents'>{Box("a")}{Box("b")}</div>{Box("c")}</div>"));
            var (without, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                style + $"<div id='f'>{Box("a")}{Box("b")}{Box("c")}</div>"));

            foreach (var id in new[] { "a", "b", "c" })
            {
                var x = LayoutHarness.FindById(withWrapper, id)!;
                var y = LayoutHarness.FindById(without, id)!;
                Assert.Equal(y.Location.X, x.Location.X, 0.5);
                Assert.Equal(y.Location.Y, x.Location.Y, 0.5);
            }
        }

        [Fact]
        public async Task FlexContainer_InlineLevelChildrenOfAContentsWrapper_AreItems()
        {
            // Only a flex item takes its own width, so this is what tells an item from inline content.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div style='display:flex;width:300pt'><div style='display:contents'>" +
                "<span id='a' style='width:60pt;height:10pt'>a</span><span id='b' style='width:60pt;height:10pt'>b</span>" +
                "</div></div>"));

            var a = LayoutHarness.FindById(root, "a")!;
            var b = LayoutHarness.FindById(root, "b")!;

            Assert.Equal(a.Location.Y, b.Location.Y, 0.5);
            Assert.Equal(a.Location.X + 60, b.Location.X, 0.5);
        }

        [Fact]
        public async Task GridContainer_ChildrenOfAContentsWrapper_AreItsGridItems()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div style='display:grid;grid-template-columns:100pt 100pt;width:300pt'>" +
                $"<div style='display:contents'>{Box("a", 30)}{Box("b", 30)}</div></div>"));

            var a = LayoutHarness.FindById(root, "a")!;
            var b = LayoutHarness.FindById(root, "b")!;

            Assert.Equal(a.Location.Y, b.Location.Y, 0.5);
            Assert.Equal(a.Location.X + 100, b.Location.X, 0.5);
        }

        [Fact]
        public async Task Table_CellsOfAContentsRow_BecomeCellsOfARealRow()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<table><tbody><tr id='r' style='display:contents'><td id='a'>one</td><td id='b'>two</td></tr></tbody></table>"));

            var a = LayoutHarness.FindById(root, "a")!;
            var b = LayoutHarness.FindById(root, "b")!;

            Assert.Equal(a.Location.Y, b.Location.Y, 0.5);
            Assert.True(b.Location.X > a.Location.X);
            Assert.Equal(DisplayMode.TableCell, a.Display.Value);
            Assert.Equal(DisplayMode.TableRow, a.ParentBox!.Display.Value);
            Assert.Same(a.ParentBox, b.ParentBox);
        }

        [Fact]
        public async Task Table_TwoContentsRows_ShareOneAnonymousRow()
        {
            // Box generation "ignores the elided elements entirely", so the cells are just cells with no
            // row of theirs, and CSS 2.1 §17.2.1 wraps them in one anonymous row together.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<table><tbody>" +
                "<tr style='display:contents'><td id='a'>one</td></tr>" +
                "<tr style='display:contents'><td id='b'>two</td></tr>" +
                "</tbody></table>"));

            var a = LayoutHarness.FindById(root, "a")!;
            var b = LayoutHarness.FindById(root, "b")!;

            Assert.Same(a.ParentBox, b.ParentBox);
            Assert.Equal(a.Location.Y, b.Location.Y, 0.5);
        }

        [Fact]
        public async Task InlineContentsSpan_LaysOutLikeItsContentsWithoutTheSpan()
        {
            var text = "alpha <SPAN>beta <b>gamma</b></SPAN> delta epsilon zeta eta theta iota kappa lambda mu nu xi omicron pi rho sigma tau";
            var (withSpan, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<p id='p' style='width:150pt'>" + text.Replace("<SPAN>", "<span style='display:contents'>").Replace("</SPAN>", "</span>") + "</p>"));
            var (without, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<p id='p' style='width:150pt'>" + text.Replace("<SPAN>", "").Replace("</SPAN>", "") + "</p>"));

            var p1 = LayoutHarness.FindById(withSpan, "p")!;
            var p2 = LayoutHarness.FindById(without, "p")!;

            Assert.Equal(p2.ActualBottom, p1.ActualBottom, 0.5);
            Assert.Equal(p2.LineBoxes.Count, p1.LineBoxes.Count);
            Assert.True(p1.LineBoxes.Count > 1);
        }

        [Fact]
        public async Task TheElementsOwnBoxProperties_HaveNoEffect()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='o' style='width:100pt'>" +
                "<div id='w' style='display:contents;border:5pt solid red;padding:10pt;margin:20pt;background:red;" +
                "position:absolute;left:30pt;top:30pt;float:right;opacity:.5;transform:rotate(30deg)'>" +
                "<div id='k' style='height:10pt'></div></div></div>"));

            var o = LayoutHarness.FindById(root, "o")!;
            var k = LayoutHarness.FindById(root, "k")!;

            Assert.Equal(o.Location.X, k.Location.X, 0.5);
            Assert.Equal(o.Location.Y, k.Location.Y, 0.5);
            Assert.Equal(100, k.ActualRight - k.Location.X, 0.5);
            Assert.Equal(o.Location.Y + 10, o.ActualBottom, 0.5);
        }

        [Fact]
        public async Task TheBodyShell_HasNoBorderPaddingOrRadius_ForTheCanvasBackgroundToBeMeasuredFrom()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>body{display:contents;border:5pt solid red;padding:10pt;border-radius:8pt}</style><p>x</p>"));

            var shell = Assert.Single(container.DisplayContentsShells);

            Assert.Equal(0, shell.ActualBorderLeftWidth);
            Assert.Equal(0, shell.ActualBorderTopWidth);
            Assert.Equal(0, shell.ActualPaddingLeft);
            Assert.Equal(0, shell.ActualPaddingBottom);
            Assert.False(shell.IsRounded);
        }

        [Fact]
        public async Task Children_InheritFromTheContentsElement()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div style='display:contents;color:rgb(0,0,255);direction:rtl;font-size:20pt'>" +
                "<span id='s'>x</span></div>"));

            var s = LayoutHarness.FindById(root, "s")!;

            Assert.Equal(Blue, s.Color);
            Assert.Equal(DirectionMode.Rtl, s.Direction.Value);
            Assert.Equal(20, s.ActualFont.Size, 0.5);
        }

        [Fact]
        public async Task NestedContentsElements_AreAllReplacedByTheirContents()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='f' style='display:flex;width:300pt'>" +
                $"<div id='w1' style='display:contents'><div id='w2' style='display:contents'>{Box("a")}</div>{Box("b")}</div></div>"));

            var f = LayoutHarness.FindById(root, "f")!;
            var a = LayoutHarness.FindById(root, "a")!;
            var b = LayoutHarness.FindById(root, "b")!;

            Assert.Equal(new[] { a, b }, f.Boxes);
            Assert.Equal(a.Location.X + 50, b.Location.X, 0.5);

            // Outermost first, and both remain addressable.
            Assert.Equal(new[] { "w1", "w2" }, a.DisplayContentsAncestors!.Select(s => s.HtmlTag!.TryGetAttribute("id", "")));
            Assert.Equal(new[] { "w1" }, b.DisplayContentsAncestors!.Select(s => s.HtmlTag!.TryGetAttribute("id", "")));
            Assert.Equal(2, container.DisplayContentsShells.Count);
            Assert.Equal("w1", container.DisplayContentsShells[0].HtmlTag!.TryGetAttribute("id", ""));
        }

        [Theory]
        [InlineData("img", "src='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII='")]
        [InlineData("input", "type='text'")]
        [InlineData("select", "")]
        [InlineData("textarea", "")]
        [InlineData("canvas", "")]
        [InlineData("progress", "")]
        [InlineData("meter", "")]
        public async Task ReplacedElementsAndFormControls_ComputeContentsToNone(string tag, string attributes)
        {
            var element = tag is "input" or "img" ? $"<{tag} id='e' {attributes} style='display:contents'>" : $"<{tag} id='e' {attributes} style='display:contents'></{tag}>";
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap($"<div id='p'>{element}</div>"));

            var e = LayoutHarness.FindById(root, "e")!;

            Assert.Equal(DisplayMode.None, e.Display.Value);
            Assert.Empty(container.DisplayContentsShells);
        }

        [Fact]
        public async Task ContentsInsideSvg_IsNotSplicedOutOfTheSvgTree()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<svg id='s' width='40' height='40'><g id='g' style='display:contents' transform='translate(5 5)'>" +
                "<rect width='10' height='10'/></g></svg>"));

            var svg = Assert.IsType<CssBoxSvg>(LayoutHarness.FindById(root, "s"));

            // Nothing inside the SVG was recorded, so nothing was lifted out from under its renderer: the
            // <g> still has its <rect>, and the SVG document was built from it.
            Assert.Empty(container.DisplayContentsShells);
            Assert.NotNull(svg.Document);
        }

        [Fact]
        public async Task StringSet_AWrapperAndTheHeadingItWraps_KeepDocumentOrder()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>#w{display:contents;string-set:t 'outer'} h1{string-set:t 'inner'}</style>" +
                "<div id='w'><h1>Heading</h1></div>"));

            Assert.Equal(new[] { "outer", "inner" }, container.NamedStrings.Where(n => n.Name == "t").Select(n => n.Value));
        }

        [Fact]
        public async Task Bookmarks_NestedContentsElements_KeepDocumentOrder()
        {
            var html = "<html><body><section style='display:contents;bookmark-level:1;bookmark-label:\"Outer\"'>" +
                       "<div style='display:contents;bookmark-level:2;bookmark-label:\"Inner\"'><p>text</p></div>" +
                       "</section></body></html>";
            var result = await new PdfGenerator().GeneratePdf(html, PageSize.A4);

            var outer = Assert.Single(result.PdfDocument.Outlines);
            Assert.Equal("Outer", outer.Title);
            Assert.Equal("Inner", Assert.Single(outer.Outlines).Title);
        }

        [Fact]
        public async Task LineBreak_ComputesContentsToNone()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<p id='p'>one<br id='e' style='display:contents'>two</p>"));

            Assert.Equal(DisplayMode.None, LayoutHarness.FindById(root, "e")!.Display.Value);
            Assert.Empty(container.DisplayContentsShells);
        }

        [Theory]
        [InlineData("<button id='e' style='display:contents'>go</button>")]
        [InlineData("<fieldset id='e' style='display:contents'>x</fieldset>")]
        [InlineData("<details id='e' style='display:contents' open>x</details>")]
        [InlineData("<label id='e' style='display:contents'>x</label>")]
        public async Task ButtonsFieldsetsAndOtherElements_JustLoseTheirBox(string markup)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap($"<div id='p'>{markup}</div>"));

            var shell = Assert.Single(container.DisplayContentsShells);
            Assert.Equal("e", shell.HtmlTag!.TryGetAttribute("id", ""));
            Assert.Null(LayoutHarness.FindById(root, "e"));

            // Its content was laid out in the parent, not dropped.
            var content = LayoutHarness.Descendants(LayoutHarness.FindById(root, "p")!).First(b => b.Text is { Length: > 0 });
            Assert.True(content.Words.Count > 0 && content.Words[0].Width > 0);
        }

        [Fact]
        public async Task BeforeAndAfter_AreLiftedWithTheChildren_InOrder()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>#w::before{content:'B'} #w::after{content:'A'}</style>" +
                "<div id='p'><div id='w' style='display:contents'><span id='m'>m</span></div></div>"));

            var p = LayoutHarness.FindById(root, "p")!;

            Assert.Equal(3, p.Boxes.Count);
            Assert.True(p.Boxes[0].IsBeforePseudoElement);
            Assert.Equal("m", LayoutHarness.FindById(root, "m")!.Boxes.Single().Text);
            Assert.Same(LayoutHarness.FindById(root, "m"), p.Boxes[1]);
            Assert.True(p.Boxes[2].IsAfterPseudoElement);
            Assert.Equal("B", p.Boxes[0].Text);
            Assert.Equal("A", p.Boxes[2].Text);
        }

        [Fact]
        public async Task APseudoElementWithContents_KeepsItsGeneratedTextAsAnInlineBox()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>#p::before{content:'X';display:contents}</style><p id='p'>text</p>"));

            var before = Assert.Single(LayoutHarness.FindById(root, "p")!.Boxes, b => b.IsBeforePseudoElement);

            Assert.Equal(DisplayMode.Inline, before.Display.Value);
            Assert.Equal("X", before.Text);
        }

        [Fact]
        public async Task ContentsList_ItemsStillGetTheirMarkers()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div><ul style='display:contents'><li id='i'>one</li></ul></div>"));

            var i = LayoutHarness.FindById(root, "i")!;

            Assert.Contains(i.Boxes, b => b.IsMarkerPseudoElement);
        }

        // ─── Counters: scope follows the element tree, not the flattened box tree ────────────────

        private static string MarkerText(CssBox root, string listItemId) =>
            ((CssBoxMarker)LayoutHarness.FindById(root, listItemId)!.Boxes.Single(b => b.IsMarkerPseudoElement)).Text!;

        private static int CounterValue(CssBox root, string id, string counter) =>
            CssCounterEngine.GetCounter(LayoutHarness.FindById(root, id)!, counter)!.Value;

        // Every expectation below was read off Chrome 152 rather than assumed.

        [Fact]
        public async Task ContentsLists_SeparatedByAParagraph_EachNumberFromOne()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<ol style='display:contents'><li id='a'>a<li id='b'>b</ol><p>x</p>" +
                "<ol style='display:contents'><li id='c'>c</li></ol>"));

            Assert.Equal(["1.", "2.", "1."], [MarkerText(root, "a"), MarkerText(root, "b"), MarkerText(root, "c")]);
        }

        [Fact]
        public async Task ContentsLists_DirectlyAdjacent_EachNumberFromOne()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<ol style='display:contents'><li id='a'>a</ol><ol style='display:contents'><li id='b'>b</ol>"));

            Assert.Equal("1.", MarkerText(root, "a"));
            Assert.Equal("1.", MarkerText(root, "b"));
        }

        [Fact]
        public async Task ContentsLists_InsideAContentsWrapper_StillEndAtTheirOwnElement()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div style='display:contents'><ol style='display:contents'><li id='a'>a<li id='b'>b</ol></div>" +
                "<p>x</p><ol style='display:contents'><li id='c'>c</li></ol>"));

            Assert.Equal(["1.", "2.", "1."], [MarkerText(root, "a"), MarkerText(root, "b"), MarkerText(root, "c")]);
        }

        [Fact]
        public async Task ContentsList_AfterAContentsUnorderedList_NumbersFromOne()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<ul style='display:contents'><li>a</li><li>b</li></ul><ol style='display:contents'><li id='c'>c</li></ol>"));

            Assert.Equal("1.", MarkerText(root, "c"));
        }

        [Fact]
        public async Task ContentsWrapperBetweenTwoItemsOfAContentsList_DoesNotRestartTheNumbering()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<ol style='display:contents'><li id='a'>a</li><div style='display:contents'><li id='b'>b</li></div><li id='c'>c</li></ol>"));

            Assert.Equal(["1.", "2.", "3."], [MarkerText(root, "a"), MarkerText(root, "b"), MarkerText(root, "c")]);
        }

        [Fact]
        public async Task BareTextBetweenTwoItemsOfAContentsList_DoesNotRestartTheNumbering()
        {
            // The text run gets an anonymous wrapper after the display: contents boxes were lifted, so the
            // wrapper carries no record of the list it sits in; the counter has to pass through it.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<ol style='display:contents'><li id='a'>a</li>text<li id='b'>b</li></ol>"));

            // The fixture only proves anything if the run really was wrapped in an anonymous box between the
            // two items - the box a counter has to pass through.
            var b = LayoutHarness.FindById(root, "b")!;
            var between = b.ParentBox!.Boxes[b.ParentBox.Boxes.IndexOf(b) - 1];
            Assert.Null(between.HtmlTag);
            Assert.Contains(LayoutHarness.Descendants(between), d => d.Words.Any(w => w.Text?.Trim() == "text"));

            Assert.Equal(["1.", "2."], [MarkerText(root, "a"), MarkerText(root, "b")]);
        }

        [Fact]
        public async Task CounterFunctionOnAnItemAfterBareText_ContinuesTheContentsList()
        {
            // `content: counter(list-item)` is resolved from the same counters but on a different path from the
            // default marker's, and it is where an anonymous box between two items could matter.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>li{list-style:none} li::before{content:counter(list-item) '|'}</style>" +
                "<ol style='display:contents'><li id='a'>a</li>text<li id='b'>b</li></ol>"));

            string Before(string id) => LayoutHarness.FindById(root, id)!.Boxes.Single(b => b.IsBeforePseudoElement).Text!;

            Assert.Equal(["1|", "2|"], [Before("a"), Before("b")]);
        }

        [Fact]
        public async Task ItemCounters_ResolvedAgainOnTheFinalTree_StillPassThroughAnAnonymousBox()
        {
            // Markers and counter() content resolve before the passes that wrap an inline run in an anonymous
            // box (CorrectTextBoxes runs ahead of CorrectInlineBoxesParent), so the ordinary tests above never
            // ask the question on the final tree. A counter first asked for later - a target-counter(), say -
            // does. Resolve every counter afresh over the finished tree, where the wrapper between the two
            // items exists and carries no record of the list it sits in.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<ol style='display:contents'><li id='a'>a</li>text<li id='b'>b</li></ol><p>x</p>" +
                "<ol style='display:contents'><li id='c'>c</li></ol>"));

            foreach (var box in LayoutHarness.Descendants(root))
            {
                box.Counters.Clear();
                box.FinalizedCounterNames.Clear();
            }

            Assert.Equal([1, 2, 1],
                [CounterValue(root, "a", "list-item"), CounterValue(root, "b", "list-item"), CounterValue(root, "c", "list-item")]);
        }

        [Fact]
        public async Task ItemCounters_ResolvedAgainOnTheFinalTree_StillPassThroughAContentsWrapper()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<ol style='display:contents'><li id='a'>a</li><div style='display:contents'><li id='b'>b</li></div><li id='c'>c</li></ol>"));

            foreach (var box in LayoutHarness.Descendants(root))
            {
                box.Counters.Clear();
                box.FinalizedCounterNames.Clear();
            }

            Assert.Equal([1, 2, 3],
                [CounterValue(root, "a", "list-item"), CounterValue(root, "b", "list-item"), CounterValue(root, "c", "list-item")]);
        }

        [Fact]
        public async Task BareTextAfterAContentsList_StillEndsItsNumbering()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<ol style='display:contents'><li id='a'>a</li></ol>text<ol style='display:contents'><li id='b'>b</li></ol>"));

            Assert.Equal(["1.", "1."], [MarkerText(root, "a"), MarkerText(root, "b")]);
        }

        [Fact]
        public async Task ContentsList_NestedInAnItem_ContinuesTheOuterList()
        {
            // A contents list generates no box, so it cannot reset the counter (CSS Lists 3 §4.5): the item
            // inside it is one more item of the enclosing list, as in a browser.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<ol><li id='one'>one<ol style='display:contents'><li id='two'>two</li></ol></li><li id='three'>three</li></ol>"));

            Assert.Equal(["1.", "2.", "3."], [MarkerText(root, "one"), MarkerText(root, "two"), MarkerText(root, "three")]);
        }

        [Fact]
        public async Task ContentsList_AfterAWrappedOrdinaryList_NumbersFromOne()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div style='display:contents'><ol><li id='a'>a<li id='b'>b</ol></div><ol><li id='c'>c</li></ol>"));

            Assert.Equal(["1.", "2.", "1."], [MarkerText(root, "a"), MarkerText(root, "b"), MarkerText(root, "c")]);
        }

        [Fact]
        public async Task CounterResetOutside_ContinuesAcrossAContentsWrapper()
        {
            // The counter is created by `.sec`, an ordinary element, so its scope spans the whole element
            // and the contents wrapper in the middle of it must not cut it. This is what a scope check keyed
            // on "was lifted out of a contents element" would break.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>.sec{counter-reset:sec} h2{counter-increment:sec}</style>" +
                "<div class='sec'><h2 id='a'>a</h2><div style='display:contents'><h2 id='b'>b</h2><h2 id='c'>c</h2></div>" +
                "<h2 id='d'>d</h2></div>"));

            Assert.Equal([1, 2, 3, 4],
                [CounterValue(root, "a", "sec"), CounterValue(root, "b", "sec"), CounterValue(root, "c", "sec"), CounterValue(root, "d", "sec")]);
        }

        [Fact]
        public async Task UnresetCounterIncrementedInsideAContentsElement_LeaksLikeThroughAPlainWrapper()
        {
            // Read off Chrome 152: a counter that is only ever incremented, never reset, is instantiated at its
            // first increment and stays in scope for the rest of that element's parent - through a plain `div`
            // as much as a `display: contents` one. Only the list-item counter is scoped to the list element
            // (see ScopeParentOf), so an author counter must number exactly as it does with a plain wrapper.
            const string Style = "<style>h2{counter-increment:n}</style>";

            var (contents, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                Style + "<div style='display:contents'><h2 id='a'>a</h2><h2 id='b'>b</h2></div><h2 id='c'>c</h2>"));
            var (plain, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                Style + "<div><h2 id='a'>a</h2><h2 id='b'>b</h2></div><h2 id='c'>c</h2>"));

            Assert.Equal([1, 2, 3], [CounterValue(contents, "a", "n"), CounterValue(contents, "b", "n"), CounterValue(contents, "c", "n")]);
            Assert.Equal([1, 2, 3], [CounterValue(plain, "a", "n"), CounterValue(plain, "b", "n"), CounterValue(plain, "c", "n")]);
        }

        [Fact]
        public async Task RootElement_ComputesContentsToBlock()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html style='display:contents'><body style='margin:0'><p id='p'>x</p></body></html>");

            var html = DomUtils.GetBoxByTagName(root, "html")!;

            Assert.Equal(DisplayMode.Block, html.Display.Value);
            Assert.Empty(container.DisplayContentsShells);
        }

        [Fact]
        public async Task FirstLetter_OfTheContentsElementDoesNothing_ButItsParentsReachesIn()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>#p::first-letter{color:rgb(255,0,0)} #w::first-letter{color:rgb(0,0,255)}</style>" +
                "<p id='p'><span id='w' style='display:contents'>Hello</span></p>"));

            var firstLetter = Assert.Single(LayoutHarness.Descendants(root), b => b.IsFirstLetterPseudoElement);

            Assert.Equal("rgb(255, 0, 0)", firstLetter.Color);
            Assert.Equal("H", firstLetter.Text);
        }

        [Fact]
        public async Task TextDecoration_OnAContentsElement_DoesNotReachItsContent()
        {
            // Line decorations propagate through the box tree, and there is no box to propagate from
            // (CSS Text Decoration 4): the control proves this fixture would draw an underline.
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<p>a<span style='display:contents;text-decoration:underline'>Hxg</span>b</p>"));
            var (_, control) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<p>a<span style='text-decoration:underline'>Hxg</span>b</p>"));

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintPage(container, g);
            var controlGraphics = new TestRecordingGraphics();
            FragmentPaintHarness.PaintPage(control, controlGraphics);

            Assert.Empty(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            Assert.Single(controlGraphics.Log.OfType<TestRecordingGraphics.DrawLineCall>());
        }

        [Fact]
        public async Task CounterIncrement_OnAContentsElement_HasNoEffect()
        {
            // Lists 3 §4.5: an element that generates no box cannot set, reset or increment a counter.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>body{counter-reset:c} p::before{content:counter(c)}</style>" +
                "<div style='display:contents;counter-increment:c'></div><p id='p'>x</p>" +
                "<div style='counter-increment:c'></div><p id='q'>y</p>"));

            Assert.Equal("0", LayoutHarness.FindById(root, "p")!.Boxes.First(b => b.IsBeforePseudoElement).Text);
            Assert.Equal("1", LayoutHarness.FindById(root, "q")!.Boxes.First(b => b.IsBeforePseudoElement).Text);
        }

        [Fact]
        public async Task WordsUnderAContentsWrapper_AreStillPlacedAndEmitted()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<p id='p'>before <span style='display:contents'>hello</span> after</p>"));

            var p = LayoutHarness.FindById(root, "p")!;
            var fragment = FragmentPaintHarness.FragmentOf(container, p);

            var words = Flatten(fragment).SelectMany(f => f.Words).Select(w => w.Word.Text?.Trim()).ToList();
            Assert.Contains("hello", words);
            Assert.Contains("before", words);
            Assert.Contains("after", words);
        }

        private static IEnumerable<PeachPDF.Html.Core.Fragments.BoxFragment> Flatten(PeachPDF.Html.Core.Fragments.BoxFragment fragment)
        {
            yield return fragment;
            foreach (var child in fragment.Children)
                foreach (var f in Flatten(child)) yield return f;
        }

        [Fact]
        public async Task SupportsQuery_ReportsContentsAsSupported()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>#a{color:rgb(255,0,0)} @supports (display: contents){#a{color:rgb(0,0,255)}}</style><span id='a'>x</span>"));

            Assert.Equal(Blue, LayoutHarness.FindById(root, "a")!.Color);
        }

        // ─── The element is still the element: ids, links, cross-references ───────────────────────

        [Fact]
        public async Task ContentsElement_StaysAddressableById()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='p'>{Box("pre")}<div id='w' style='display:contents'>{Box("a")}</div></div>"));

            var shell = DomUtils.GetBoxById(root, "w");
            Assert.NotNull(shell);
            Assert.True(shell!.IsDisplayContentsShell);
            Assert.Same(shell, container.GetBoxById(LayoutHarness.FindById(root, "a")!, "w"));

            // Its rectangle is where its content begins.
            var rect = container.GetElementRectangle("w");
            var a = LayoutHarness.FindById(root, "a")!;
            Assert.NotNull(rect);
            Assert.Equal(a.Location.Y, rect!.Value.Y, 0.5);
        }

        [Fact]
        public async Task ContentsElementWithNoContent_ResolvesToTheBoxThatReceivedItsChildren()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='p'>{Box("pre", height: 30)}<div id='w' style='display:contents'></div></div>"));

            var rect = container.GetElementRectangle("w");
            var p = LayoutHarness.FindById(root, "p")!;

            Assert.NotNull(rect);
            Assert.Equal(p.Location.Y, rect!.Value.Y, 0.5);
        }

        [Fact]
        public async Task AnchorLink_ToAContentsElement_LandsWhereItsContentBegins()
        {
            var html = "<html><body><a href='#w'>go</a>" +
                       "<div style='height:1200px'></div>" +
                       "<div id='w' style='display:contents'><div style='break-before:page'>Target</div></div></body></html>";
            var result = await new PdfGenerator().GeneratePdf(html, PageSize.A4);

            var array = Assert.IsType<PdfArray>(result.PdfDocument.Catalog.Names.NameTree!.GetValue("w"));
            var literal = Assert.IsType<PdfLiteral>(array.Elements[0]);
            var match = Regex.Match(literal.Value, @"/FitH\s+([\d.]+)");

            Assert.True(match.Success, literal.Value);
            var pageHeight = (double)result.PdfDocument.Pages[result.PdfDocument.Pages.Count - 1].Height;
            Assert.True(double.Parse(match.Groups[1].Value) > pageHeight * 0.8);
        }

        [Fact]
        public async Task LinkOnAContentsAnchor_IsStillALink_OverItsContent()
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A4 };
            var result = await new PdfGenerator().GeneratePdf(
                "<html><body><p>see <a style='display:contents' href='https://example.com/'>the <b>docs</b></a> now</p></body></html>", config);

            Assert.True(result.PdfDocument.Pages[0].Annotations.Count > 0);
        }

        [Fact]
        public async Task TargetCounterPage_AndTargetText_ResolveAgainstAContentsElement()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>a.p::after{content:target-counter(attr(href),page)} a.t::after{content:target-text(attr(href))}</style>" +
                "<p><a id='ap' class='p' href='#w'></a> <a id='at' class='t' href='#w'></a></p>" +
                "<div style='height:1500pt'></div>" +
                "<div id='w' style='display:contents'><h1 id='h'>Chapter Title</h1></div>"));

            var h = LayoutHarness.FindById(root, "h")!;
            var page = int.Parse(LayoutHarness.FindById(root, "ap")!.Boxes.First(b => b.IsAfterPseudoElement).Text!);

            Assert.True(page >= 2, $"the target is well past the first page (resolved {page})");
            Assert.True(h.Location.Y > 842);
            Assert.Equal("Chapter Title", LayoutHarness.FindById(root, "at")!.Boxes.First(b => b.IsAfterPseudoElement).Text);
        }

        [Fact]
        public async Task ContentAttr_OnALiftedBefore_ReadsTheElementItWasGeneratedFor()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>#w::before{content:attr(data-x)}</style>" +
                "<div id='p' data-x='parent'><span id='w' data-x='hello' style='display:contents'>z</span></div>"));

            var before = Assert.Single(LayoutHarness.FindById(root, "p")!.Boxes, b => b.IsBeforePseudoElement);

            Assert.Equal("hello", before.Text);
            Assert.Equal("w", before.OriginatingElement.HtmlTag!.TryGetAttribute("id", ""));
        }

        [Fact]
        public async Task BookmarkOnAContentsHeading_IsPlacedInDocumentOrder()
        {
            var html = "<html><body><h1>First</h1>" +
                       "<h1 style='display:contents'>Second</h1>" +
                       "<h1>Third</h1></body></html>";
            var result = await new PdfGenerator().GeneratePdf(html, PageSize.A4);

            Assert.Equal(new[] { "First", "Second", "Third" }, result.PdfDocument.Outlines.Select(o => o.Title));
            Assert.All(result.PdfDocument.Outlines, o => Assert.NotNull(o.DestinationPage));
        }

        [Fact]
        public async Task LiftingAShellThatIsNoLongerInItsParent_IsANoOp()
        {
            // The defensive arm: a shell whose parent no longer lists it has nothing to splice.
            var shell = CssBox.CreateBlock();

            Assert.Empty(shell.LiftDisplayContentsChildren());
            Assert.Empty(shell.DisplayContentsLiftedChildren!);
            await Task.CompletedTask;
        }

        [Fact]
        public async Task LookupOfAnIdNoElementHas_StillFindsNothing_WhenShellsExist()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='w' style='display:contents'>{Box("a")}</div>"));

            Assert.Single(container.DisplayContentsShells);
            Assert.Null(DomUtils.GetBoxById(root, "nothing"));
            Assert.Null(container.GetElementRectangle("nothing"));
        }

        [Fact]
        public async Task ContentsElement_WhoseFirstChildHasNoGeometryOfItsOwn_ResolvesToTheFirstLaidOutDescendant()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='p'><div id='pre' style='height:30pt'></div>" +
                "<div id='w' style='display:contents'><span><span></span><b id='b'>text</b></span></div></div>"));

            var b = LayoutHarness.FindById(root, "b")!;
            var rect = container.GetElementRectangle("w");

            Assert.NotNull(rect);
            Assert.Equal(b.Rectangles.Values.First().Top, rect!.Value.Top, 0.5);
        }

        [Fact]
        public async Task TargetCounter_OfAContentsElement_ReadsTheCountersWhereItsContentBegins()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>body{counter-reset:n} div.item{counter-increment:n} a::after{content:target-counter(attr(href), n)}</style>" +
                "<div class='item'>1</div><div class='item'>2</div>" +
                "<div id='w' style='display:contents'><div class='item'>3</div></div>" +
                "<p><a id='ref' href='#w'></a></p>"));

            // The counter as it stands where the element's content begins: after items 1 and 2, before 3.
            Assert.Equal("2", LayoutHarness.FindById(root, "ref")!.Boxes.First(b => b.IsAfterPseudoElement).Text);
        }

        // Counters follow the document tree whatever the positioning (CSS Lists 3 §4), so an out-of-flow item
        // just before the element still counts, including one that is its parent's first child.
        [Theory]
        [InlineData("position:absolute")]
        [InlineData("float:left")]
        [InlineData("position:fixed")]
        public async Task TargetCounter_OfAContentsElement_CountsAnOutOfFlowItemBeforeIt(string css)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>body{counter-reset:n} div.item{counter-increment:n} a::after{content:target-counter(attr(href), n)}</style>" +
                $"<div><div class='item' style='{css}'>1</div>" +
                "<div id='w' style='display:contents'><div class='item'>2</div></div></div>" +
                "<p><a id='ref' href='#w'></a></p>"));

            Assert.Equal("1", LayoutHarness.FindById(root, "ref")!.Boxes.First(b => b.IsAfterPseudoElement).Text);
        }

        // A sibling that generates no box is no anchor: with nothing else before it, the element's counters are
        // its parent's, as they stand after the item before that parent.
        [Fact]
        public async Task TargetCounter_OfAContentsElement_AfterOnlyAnUndisplayedSibling_ReadsItsParentsCounters()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>body{counter-reset:n} div.item{counter-increment:n} a::after{content:target-counter(attr(href), n)}</style>" +
                "<div class='item'>1</div><div><div class='item' style='display:none'>x</div>" +
                "<div id='w' style='display:contents'><div class='item'>2</div></div></div>" +
                "<p><a id='ref' href='#w'></a></p>"));

            Assert.Equal("1", LayoutHarness.FindById(root, "ref")!.Boxes.First(b => b.IsAfterPseudoElement).Text);
        }

        [Fact]
        public async Task BookmarkLabel_OnAContentsHeading_UsesItsBeforeAndItsOwnText()
        {
            var html = "<html><head><style>" +
                       "h1{display:contents;bookmark-label:content(before) content(text)}" +
                       "h1::before{content:attr(data-n) '. '}</style></head>" +
                       "<body><h1 data-n='7'>  <span>Title</span></h1></body></html>";
            var result = await new PdfGenerator().GeneratePdf(html, PageSize.A4);

            Assert.Equal("7. Title", result.PdfDocument.Outlines.Single().Title);
        }

        // ─── string-set (GCPM 3 §1.1.1) ──────────────────────────────────────────────────────────

        [Fact]
        public async Task StringSet_OnAContentsElement_IsAssignedWhereItsContentBegins_InDocumentOrder()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>h1{string-set:hdr content()} h2{string-set:hdr content()}</style>" +
                "<h1>Alpha</h1>" + Box("gap", height: 40) +
                "<h2 id='w' style='display:contents'>Beta</h2>" + Box("gap2", height: 40) +
                "<h1>Gamma</h1>"));

            var values = container.NamedStrings.Where(n => n.Name == "hdr").Select(n => n.Value).ToList();

            Assert.Equal(new[] { "Alpha", "Beta", "Gamma" }, values);

            var beta = container.NamedStrings.Single(n => n.Value == "Beta");
            var text = LayoutHarness.Descendants(root).First(b => b.Text == "Beta");
            Assert.Equal(text.Rectangles.Values.First().Top, beta.Y, 1.0);
        }

        [Fact]
        public async Task StringSet_OnAContentsElement_IsAssignedOnceAcrossRelayouts()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>h2{display:contents;string-set:hdr content()}</style><p>x</p><h2>Beta</h2>"),
                after: async (_, c, g) => await c.PerformLayout(g));

            Assert.Equal("Beta", Assert.Single(container.NamedStrings, n => n.Name == "hdr").Value);
        }

        [Fact]
        public async Task StringSet_CounterOperand_OnAContentsElement_ReadsWhereItsContentBegins()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>body{counter-reset:c} .i{counter-increment:c} #w{display:contents;string-set:n counter(c)}</style>" +
                "<div class='i'>1</div><div id='w'><div class='i'>2</div></div>"));

            Assert.Equal("1", Assert.Single(container.NamedStrings, n => n.Name == "n").Value);
        }

        [Fact]
        public async Task StringSet_OnAnEmptyContentsElement_StillAssigns()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>#w{display:contents;string-set:tag 'set'}</style><p>before</p><div id='w'></div><p>after</p>"));

            Assert.Equal("set", Assert.Single(container.NamedStrings, n => n.Name == "tag").Value);
        }

        [Fact]
        public async Task StringSet_ContentBeforeAndAttr_OnAContentsElement()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>#w{display:contents;string-set:a attr(data-t), b content(before) content(text)} #w::before{content:'>'}</style>" +
                "<div><span id='w' data-t='attr-value'>inner</span></div>"));

            Assert.Equal("attr-value", container.NamedStrings.Single(n => n.Name == "a").Value);
            Assert.Equal(">inner", container.NamedStrings.Single(n => n.Name == "b").Value);
        }

        // ─── <body style="display:contents"> keeps its canvas background ──────────────────────────

        [Fact]
        public async Task ContentsBody_StillPaintsItsBackgroundOnTheCanvas()
        {
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            config.SetMargins(20);
            var doc = await generator.GeneratePdf(
                "<!DOCTYPE html><html><head><style>body { display: contents; background-color: rgb(255,0,0); }</style></head>" +
                "<body><p>short</p></body></html>", config);
            using var ms = new MemoryStream();
            doc.Save(ms);
            var pdfText = Encoding.Latin1.GetString(ms.ToArray());

            Assert.Matches(new Regex(@"1 0 0 rg[\s\S]{0,40}0 0 595(\.\d+)? 842(\.\d+)? re\s*\nf"), pdfText);
        }

        [Fact]
        public async Task ContentsBody_BackgroundImageLoads()
        {
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            var png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";
            var doc = await generator.GeneratePdf(
                $"<html><head><style>body{{display:contents;background-image:url({png})}}</style></head><body><p>x</p></body></html>", config);

            using var ms = new MemoryStream();
            doc.Save(ms);

            Assert.Contains("/Subtype /Image", Encoding.Latin1.GetString(ms.ToArray()));
        }

        // ─── The declarative API ─────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Declarative_HtmlFragmentWithAContentsElement_KeepsItAddressable()
        {
            var document = await new PdfGenerator().CreateDocument(doc => doc.Page(page => page.Content(c => c.Html(
                "<a href='#w'>go</a><div style='height:1200px'></div>" +
                "<div id='w' style='display:contents'><div style='break-before:page'>Target</div></div>"))));

            Assert.NotNull(document.PdfDocument.Catalog.Names.NameTree!.GetValue("w"));
        }

        [Fact]
        public async Task Declarative_StylesheetAssignedContents_IsSplicedAndKeepsItsBookmark()
        {
            var stylesheet = await new PdfGenerator().ParseStyleSheet("#w { display: contents }");

            var document = await new PdfGenerator().CreateDocument(doc =>
            {
                doc.Stylesheet(stylesheet);
                doc.Page(page => page.Content(c => c.Column(col =>
                {
                    col.Item().Text("before");
                    var wrapper = (PeachPDF.Layout.ContainerBuilder)col.Item().Id("w").Bookmark("Wrapped");
                    wrapper.Text("inside");
                })));
            });

            Assert.Equal("Wrapped", Assert.Single(document.PdfDocument.Outlines).Title);
        }

        [Fact]
        public async Task Declarative_ContentsOnThePageRoot_ComputesToBlock()
        {
            var stylesheet = await new PdfGenerator().ParseStyleSheet("#root { display: contents }");

            var document = await new PdfGenerator().CreateDocument(doc =>
            {
                doc.Stylesheet(stylesheet);
                doc.Page(page => page.Content(c => c.Id("root").Text("still laid out")));
            });

            Assert.Equal(1, document.PdfDocument.Pages.Count);
        }

        [Fact]
        public async Task Declarative_ContentsOnTheRoot_IsBlockAndNotDropped()
        {
            var stylesheet = await new PdfGenerator().ParseStyleSheet("#root { display: contents }");
            var adapter = new PeachPDF.Adapters.PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var properties = new PeachPDF.Html.Core.Utils.CssPropertyFactory(adapter);
            var page = PeachPDF.Layout.DocumentBuilder.BuildPage(
                p => p.Content(c => c.Id("root").Text("hello")), properties);

            using var container = new PeachPDF.HtmlContainer(adapter);
            await container.SetDeclarativeRoot(page.RootBox, null, stylesheet);

            Assert.Equal(DisplayMode.Block, page.RootBox.Display.Value);
            Assert.Empty(container.HtmlContainerInt.DisplayContentsShells);
        }

        [Fact]
        public async Task Declarative_EachPageGetsOnlyItsOwnShells()
        {
            var document = await new PdfGenerator().CreateDocument(doc =>
            {
                doc.Page(page => page.Content(c => c.Html(
                    "<div id='one' style='display:contents'><div style='bookmark-level:1'>First</div></div>")));
                doc.Page(page => page.Content(c => c.Html(
                    "<div id='two' style='display:contents'><div style='bookmark-level:1'>Second</div></div>")));
            });

            // Page 1's shells belong to a container that was disposed; they must not be handed to page 2's.
            Assert.Equal(new[] { "First", "Second" }, document.PdfDocument.Outlines.Select(o => o.Title));
        }

        [Fact]
        public async Task Declarative_ATopLevelFragmentShell_StillReachesTheRealTree()
        {
            var adapter = new PeachPDF.Adapters.PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var properties = new PeachPDF.Html.Core.Utils.CssPropertyFactory(adapter);
            var page = PeachPDF.Layout.DocumentBuilder.BuildPage(
                p => p.Content(c => c.Html("<p>before</p><div id='empty' style='display:contents'></div>")), properties);

            using var container = new PeachPDF.HtmlContainer(adapter);
            await container.SetDeclarativeRoot(page.RootBox, null, null, properties.DisplayContentsShells);

            var shell = Assert.Single(container.HtmlContainerInt.DisplayContentsShells);

            // Not the discarded synthetic root of the fragment: a parent chain that ends in the document.
            Assert.Same(container.HtmlContainerInt, shell.HtmlContainer);
            Assert.NotNull(shell.ParentBox);
            Assert.Contains(shell.ParentBox!, LayoutHarness.Descendants(page.RootBox));
        }

        // ─── Bidi ────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Bidi_ATextRunInAContentsSpan_JoinsTheParentsParagraph()
        {
            const string hebrew = "אבג";
            var (withSpan, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<p dir='ltr'>abc <span style='display:contents'>{hebrew}</span> def</p>"));
            var (without, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<p dir='ltr'>abc <span>{hebrew}</span> def</p>"));

            static byte[]? LevelsOf(CssBox root) =>
                LayoutHarness.Descendants(root).First(b => b.Text is { } t && t.Contains('א')).BidiLevels;

            Assert.Equal(LevelsOf(without), LevelsOf(withSpan));
        }

        // ─── Tagged PDF ──────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task TaggedPdf_ContentsSectionAndHeading_KeepTheirStructureElements()
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, EnableTaggedPdf = true };
            var result = await new PdfGenerator().GeneratePdf(
                "<html><body><section style='display:contents'><h1>Title</h1><p>Body</p></section></body></html>", config);

            var documentElement = RootKids(result.PdfDocument.Catalog.StructureTreeRoot).Single();
            var section = Assert.Single(Kids(documentElement));

            Assert.Equal("/Sect", section.StructureType);
            Assert.Equal(new[] { "/H1", "/P" }, Kids(section).Select(e => e.StructureType));
        }

        [Fact]
        public async Task TaggedPdf_AContentsList_KeepsListItemBodyStructure()
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, EnableTaggedPdf = true };
            var result = await new PdfGenerator().GeneratePdf(
                "<html><body><ul style='display:contents'><li>one</li><li>two</li></ul></body></html>", config);

            var documentElement = RootKids(result.PdfDocument.Catalog.StructureTreeRoot).Single();
            var list = Assert.Single(Kids(documentElement));

            Assert.Equal("/L", list.StructureType);
            Assert.Equal(new[] { "/LI", "/LI" }, Kids(list).Select(e => e.StructureType));
        }

        [Fact]
        public async Task TaggedPdf_AContentsTableRow_IsNotWrappedInASecondAnonymousRow()
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, EnableTaggedPdf = true };
            var result = await new PdfGenerator().GeneratePdf(
                "<html><body><table><tbody><tr style='display:contents'><td>a</td><td>b</td></tr></tbody></table></body></html>", config);

            var documentElement = RootKids(result.PdfDocument.Catalog.StructureTreeRoot).Single();
            var table = Assert.Single(Kids(documentElement));
            Assert.Equal("/Table", table.StructureType);

            var row = Descendant(table, "/TR");
            Assert.NotNull(row);
            Assert.Equal(new[] { "/TD", "/TD" }, Kids(row!).Select(e => e.StructureType));
            Assert.Null(Kids(row!).SelectMany(Kids).FirstOrDefault(e => e.StructureType == "/TR"));
            Assert.Equal(1, Count(table, "/TR"));
        }

        [Fact]
        public async Task TaggedPdf_AContentsListItem_KeepsItsBodyElement()
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, EnableTaggedPdf = true };
            var result = await new PdfGenerator().GeneratePdf(
                "<html><body><ul><li style='display:contents'>one</li></ul></body></html>", config);

            var documentElement = RootKids(result.PdfDocument.Catalog.StructureTreeRoot).Single();
            var item = Descendant(documentElement, "/LI");

            Assert.NotNull(item);
            Assert.Contains("/LBody", Kids(item!).Select(e => e.StructureType));
        }

        [Fact]
        public async Task TaggedPdf_APlainCellBesideAContentsRow_KeepsTheAnonymousRowItSharesWithIt()
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, EnableTaggedPdf = true };
            var result = await new PdfGenerator().GeneratePdf(
                "<html><body><table><tbody><td>plain</td><tr style='display:contents'><td>inside</td></tr></tbody></table></body></html>", config);

            var documentElement = RootKids(result.PdfDocument.Catalog.StructureTreeRoot).Single();
            var table = Assert.Single(Kids(documentElement));

            // The plain cell has no row of its own, so the synthesized one stays around it; the contents
            // row's own element sits beside it rather than replacing it.
            Assert.True(Count(table, "/TR") >= 2);
        }

        [Fact]
        public async Task TaggedPdf_ALinkOnAContentsAnchor_HasItsLinkElementAndAnnotation()
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, EnableTaggedPdf = true };
            var result = await new PdfGenerator().GeneratePdf(
                "<html><body><p><a style='display:contents' href='https://example.com/'>go</a></p></body></html>", config);

            var documentElement = RootKids(result.PdfDocument.Catalog.StructureTreeRoot).Single();
            var link = Descendant(documentElement, "/Link");

            Assert.NotNull(link);
            var objectReference = Assert.Single(PdfStructureElement.GetKids(link!.Elements).OfType<PdfObjectReference>());
            Assert.Same(result.PdfDocument.Pages[0], objectReference.Page);
        }

        private static PdfStructureElement? Descendant(PdfStructureElement element, string type) =>
            Kids(element).Select(k => k.StructureType == type ? k : Descendant(k, type)).FirstOrDefault(k => k is not null);

        private static int Count(PdfStructureElement element, string type) =>
            Kids(element).Sum(k => (k.StructureType == type ? 1 : 0) + Count(k, type));

        private static IEnumerable<PdfStructureElement> RootKids(PdfStructureTreeRoot root) =>
            PdfStructureElement.GetKids(root.Elements).Cast<PdfStructureElement>();

        private static IEnumerable<PdfStructureElement> Kids(PdfStructureElement element) =>
            PdfStructureElement.GetKids(element.Elements).OfType<PdfStructureElement>();
    }
}
