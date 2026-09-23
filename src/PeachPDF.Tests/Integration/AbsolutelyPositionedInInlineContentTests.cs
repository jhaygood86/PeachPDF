using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// An absolutely positioned box among inline content stays where it is in the box tree. It is out of
    /// flow, so CSS 2.1 §9.2.1.1 does not split the inline around it, and it does not end the line it sits
    /// on. When its nearest positioned ancestor is an inline, that inline's fragments form its containing
    /// block (CSS 2.1 §10.1 (4.1) for one line, CSS Positioned Layout 3 §2.1 for a wrapped inline).
    /// </summary>
    public class AbsolutelyPositionedInInlineContentTests
    {
        private const double Origin = 20;
        private static readonly RColor Ring = RColor.FromArgb(217, 74, 74);

        [Fact]
        public async Task AbsolutelyPositionedChild_DoesNotEndTheLineItSitsOn()
        {
            var (root, _) = await LayoutAsync(
                "<div id='d' style='line-height: 20pt'>aaa<span id='a' style='position: absolute'>x</span><span id='b'>bbb</span></div>");

            var div = Find(root, "d");
            var abs = Find(root, "a");
            var after = Find(root, "b");

            Assert.Same(div, abs.ParentBox);
            Assert.Single(div.LineBoxes);
            Assert.Equal(Origin + 20, div.ActualBottom, 3);
            var beforeRect = Assert.Single(div.Boxes[0].Rectangles.Values);
            var afterRect = Assert.Single(after.Rectangles.Values);
            Assert.Equal(beforeRect.Top, afterRect.Top, 3);
            Assert.Equal(beforeRect.Right, afterRect.Left, 3);
        }

        [Fact]
        public async Task InlineHoldingAnAbsolutelyPositionedChild_IsNotSplitAroundIt()
        {
            var (root, _) = await LayoutAsync(
                "<div><span id='s'>aaa<span id='a' style='position: absolute'>x</span>bbb</span></div>");

            var span = Find(root, "s");

            Assert.Same(span, Find(root, "a").ParentBox);
            Assert.Single(span.Rectangles);
            Assert.Single(Descendants(root), b => b.HtmlTag?.TryGetAttribute("id") == "s");
        }

        [Fact]
        public async Task PositionedInline_IsTheContainingBlock_ForLeftAndTop()
        {
            var (root, _) = await LayoutAsync(
                "<div style='margin-left: 100pt'>before " +
                "<span id='s' style='position: relative; border: 2pt solid; padding: 0 3pt'>inline" +
                "<span id='a' style='position: absolute; left: 10pt; top: 4pt; width: 5pt; height: 5pt'></span></span></div>");

            var span = Find(root, "s");
            var abs = Find(root, "a");
            var fragment = Assert.Single(span.Rectangles.Values);

            Assert.Equal(fragment.Left + 2 + 10, abs.Location.X, 3);
            Assert.Equal(fragment.Top + 2 + 4, abs.Location.Y, 3);
        }

        [Fact]
        public async Task PositionedInline_IsTheContainingBlock_ForRightBottomAndPercentages()
        {
            var (root, _) = await LayoutAsync(
                "<div style='margin-left: 100pt'>before " +
                "<span id='s' style='position: relative; border: 2pt solid'>an inline box" +
                "<span id='a' style='position: absolute; right: 0; bottom: 0; width: 50%; height: 5pt'></span></span></div>");

            var span = Find(root, "s");
            var abs = Find(root, "a");
            var fragment = Assert.Single(span.Rectangles.Values);
            var containingBlockWidth = fragment.Width - 4;

            Assert.Equal(containingBlockWidth / 2, abs.ActualRight - abs.Location.X, 3);
            Assert.Equal(fragment.Right - 2, abs.ActualRight, 3);
            Assert.Equal(fragment.Bottom - 2, abs.ActualBottom, 3);
        }

        [Fact]
        public async Task PositionedInline_ResolvesPercentageLeftAndTopAgainstItsOwnFragment()
        {
            var (root, _) = await LayoutAsync(
                "<div style='margin-left: 100pt; line-height: 30pt'>before " +
                "<span id='s' style='position: relative; font-size: 20pt'>an inline box" +
                "<span id='a' style='position: absolute; left: 50%; top: 50%; width: 4pt; height: 4pt'></span></span></div>");

            var fragment = Assert.Single(Find(root, "s").Rectangles.Values);
            var abs = Find(root, "a");

            Assert.Equal(fragment.Left + fragment.Width / 2, abs.Location.X, 3);
            Assert.Equal(fragment.Top + fragment.Height / 2, abs.Location.Y, 3);
        }

        [Fact]
        public async Task PositionedInline_ResolvesPercentageRightAndBottomAgainstItsOwnFragment()
        {
            var (root, _) = await LayoutAsync(
                "<div style='margin-left: 100pt; line-height: 30pt'>before " +
                "<span id='s' style='position: relative; font-size: 20pt'>an inline box" +
                "<span id='a' style='position: absolute; right: 25%; bottom: 50%; width: 4pt; height: 4pt'></span></span></div>");

            var fragment = Assert.Single(Find(root, "s").Rectangles.Values);
            var abs = Find(root, "a");

            Assert.Equal(fragment.Right - fragment.Width / 4, abs.ActualRight, 3);
            Assert.Equal(fragment.Bottom - fragment.Height / 2, abs.ActualBottom, 3);
        }

        [Fact]
        public async Task PositionedInline_FillsBetweenLeftAndRight()
        {
            var (root, _) = await LayoutAsync(
                "<div>before <span id='s' style='position: relative'>an inline box" +
                "<span id='a' style='position: absolute; left: 2pt; right: 3pt; top: 0; bottom: 0'></span></span></div>");

            var fragment = Assert.Single(Find(root, "s").Rectangles.Values);
            var abs = Find(root, "a");

            Assert.Equal(fragment.Left + 2, abs.Location.X, 3);
            Assert.Equal(fragment.Width - 5, abs.ActualRight - abs.Location.X, 3);
            Assert.Equal(fragment.Height, abs.ActualBottom - abs.Location.Y, 3);
        }

        [Fact]
        public async Task PositionedInline_CentresAutoBlockMarginsInItsOwnHeight()
        {
            var (root, _) = await LayoutAsync(
                "<div style='line-height: 30pt'>before <span id='s' style='position: relative; font-size: 20pt'>tall" +
                "<span id='a' style='position: absolute; left: 0; top: 0; bottom: 0; height: 4pt; margin: auto 0'></span></span></div>");

            var fragment = Assert.Single(Find(root, "s").Rectangles.Values);
            var abs = Find(root, "a");

            Assert.Equal(fragment.Top + (fragment.Height - 4) / 2, abs.Location.Y, 3);
        }

        [Fact]
        public async Task WrappedPositionedInline_TakesItsTopFromTheFirstFragment_AndItsBottomFromTheLast()
        {
            var (root, _) = await LayoutAsync(
                "<div style='line-height: 20pt'>" +
                "<span id='s' style='position: relative'>first line<br>second line" +
                "<span id='t' style='position: absolute; left: 0; top: 0; width: 4pt; height: 4pt'></span>" +
                "<span id='b' style='position: absolute; left: 0; bottom: 0; width: 4pt; height: 4pt'></span></span></div>");

            var fragments = Find(root, "s").Rectangles.Values.OrderBy(r => r.Top).ToList();
            Assert.Equal(2, fragments.Count);

            Assert.Equal(fragments[0].Top, Find(root, "t").Location.Y, 3);
            Assert.Equal(fragments[0].Left, Find(root, "t").Location.X, 3);
            Assert.Equal(fragments[1].Bottom, Find(root, "b").ActualBottom, 3);
        }

        [Fact]
        public async Task RightToLeftPositionedInline_TakesItsLeftFromTheLastFragment()
        {
            var (root, _) = await LayoutAsync(
                "<div dir='rtl' style='line-height: 20pt; width: 200pt'>" +
                "<span id='s' style='position: relative'>a long first line<br>short" +
                "<span id='a' style='position: absolute; left: 0; top: 0; width: 4pt; height: 4pt'></span></span></div>");

            var fragments = Find(root, "s").Rectangles.Values.OrderBy(r => r.Top).ToList();
            Assert.Equal(2, fragments.Count);

            Assert.Equal(fragments[1].Left, Find(root, "a").Location.X, 3);
        }

        [Fact]
        public async Task TallAbsolutelyPositionedChild_OfAParagraph_KeepsAllOfItsContent()
        {
            var lines = string.Join("", Enumerable.Range(0, 20).Select(i => $"Line{i}<br>"));
            var (root, container) = await LayoutAsync(
                "<div style='position: relative'>text " +
                $"<div id='a' style='position: absolute; top: 0; left: 0; width: 120pt; line-height: 20pt'>{lines}</div> more</div>",
                pageHeight: 200);

            var abs = Find(root, "a");

            Assert.Null(abs.PendingBreakToken);
            var placed = container.FragmentTree!.Fragmentainers
                .SelectMany(f => Flatten(f.Root))
                .SelectMany(f => f.Words)
                .Select(w => w.Word.Text)
                .Where(t => t?.StartsWith("Line") == true)
                .Distinct()
                .Count();
            Assert.Equal(20, placed);
        }

        [Fact]
        public async Task AbsolutelyPositionedChild_OnALineThatMovesToTheNextPage_IsLaidOutOnce()
        {
            // Five 20pt lines fit in the 100pt page area. The sixth, holding the positioned child after its
            // first word, is discarded at the break and rebuilt on the next page, where the child is laid
            // out rather than on the page the line left.
            var lines = string.Join("", Enumerable.Range(0, 5).Select(i => $"Line{i}<br>"));
            var (root, _) = await LayoutAsync(
                $"<div style='line-height: 20pt'>{lines}six<span id='a' style='position: absolute; width: 8pt; height: 8pt'>" +
                "</span> seven</div>",
                pageHeight: 140);

            var abs = Find(root, "a");

            Assert.Null(abs.PendingBreakToken);
            Assert.Equal(8, abs.ActualRight - abs.Location.X, 3);
            Assert.Equal(8, abs.ActualBottom - abs.Location.Y, 3);
        }

        [Fact]
        public async Task AbsolutelyPositionedChild_OnALineBeforeAPageBreak_IsLaidOutOnThatPage()
        {
            // The child sits on the first line; the paragraph breaks after five lines, and the resumed pass
            // never reaches the child again, so the pass that stops is the one that has to lay it out.
            var lines = string.Join("", Enumerable.Range(0, 10).Select(i => $"Line{i}<br>"));
            var (root, _) = await LayoutAsync(
                "<div id='d' style='position: relative; line-height: 20pt'>first<span id='a' style='position: absolute; " +
                $"left: 5pt; top: 6pt; width: 8pt; height: 8pt'></span><br>{lines}</div>",
                pageHeight: 140);

            var div = Find(root, "d");
            var abs = Find(root, "a");

            Assert.True(div.ActualBottom - div.Location.Y > 100);
            Assert.Equal(div.Location.X + 5, abs.Location.X, 3);
            Assert.Equal(div.Location.Y + 6, abs.Location.Y, 3);
            Assert.Equal(8, abs.ActualRight - abs.Location.X, 3);
        }

        [Fact]
        public async Task AbsolutelyPositionedChild_BesideABlock_KeepsTheInlineRunInOneAnonymousBlock()
        {
            // The block sibling makes the div's content mixed, so its inline run is wrapped. The positioned
            // child joins that run rather than standing as a block between the two halves of the text.
            var (root, _) = await LayoutAsync(
                "<div style='line-height: 20pt'><b id='x'>aaa</b><span id='a' style='position: absolute'>x</span>" +
                "<b id='y'>bbb</b><div>block</div></div>");

            var wrapper = Find(root, "x").ParentBox!;

            Assert.Null(wrapper.HtmlTag);
            Assert.Same(wrapper, Find(root, "a").ParentBox);
            Assert.Same(wrapper, Find(root, "y").ParentBox);
            Assert.Single(wrapper.LineBoxes);
        }

        [Fact]
        public async Task AbsolutelyPositionedChild_DoesNotWidenAShrinkToFitFloat()
        {
            var (root, _) = await LayoutAsync(
                "<div><div id='f' style='float: left'><b id='w'>short</b> <span style='position: absolute; white-space: nowrap'>" +
                "a rather long piece of text that goes on and on</span></div></div>");

            var f = Find(root, "f");
            var word = Assert.Single(Find(root, "w").Rectangles.Values);

            Assert.True(f.ActualRight - f.Location.X < word.Width + 10,
                $"float is {f.ActualRight - f.Location.X}pt wide for a {word.Width}pt word");
        }

        [Fact]
        public async Task AbsolutelyPositionedChild_BelowAMiddleAlignedCell_DoesNotMoveTheCellText()
        {
            var (root, _) = await LayoutAsync(
                "<table><tr><td id='c' style='vertical-align: middle'><b id='w'>Name</b> <span id='s' style='position: relative'>i" +
                "<span style='position: absolute; left: 0; top: 100%; height: 40pt'>v</span></span></td>" +
                "<td style='height: 60pt'>x</td></tr></table>");

            var cell = Find(root, "c");
            var first = Assert.Single(Find(root, "w").Rectangles.Values);
            var last = Assert.Single(Find(root, "s").Rectangles.Values);

            // Centred on the text alone, which the badge hanging below it does not extend: as much room
            // above the first line as below the last.
            Assert.True(cell.ClientBottom - cell.ClientTop > last.Bottom - first.Top + 10);
            Assert.Equal(first.Top - cell.ClientTop, cell.ClientBottom - last.Bottom, 0.5);
        }

        [Fact]
        public async Task AbsolutelyPositionedDirectChild_OfAMiddleAlignedCell_IsNeitherMeasuredNorMoved()
        {
            static string Table(string align) =>
                $"<table><tr><td id='c' style='vertical-align: {align}; position: relative'><b id='w'>Name</b>" +
                "<div id='a' style='position: absolute; left: 0; top: 100%; height: 40pt'>v</div></td>" +
                "<td style='height: 60pt'>x</td></tr></table>";

            var (top, _) = await LayoutAsync(Table("top"));
            var (middle, _) = await LayoutAsync(Table("middle"));

            var cell = Find(middle, "c");
            var word = Assert.Single(Find(middle, "w").Rectangles.Values);
            var wordAtTop = Assert.Single(Find(top, "w").Rectangles.Values);

            // Centred on the word alone: half the leftover room above it, as much below.
            Assert.Equal(cell.ClientTop + (cell.ClientBottom - cell.ClientTop - wordAtTop.Height) / 2, word.Top, 0.5);
            // Its containing block is the cell, which the alignment does not move.
            Assert.Equal(Find(top, "a").Location.Y, Find(middle, "a").Location.Y, 3);
        }

        [Fact]
        public async Task AbsolutelyPositionedDirectChild_WithAutoOffsets_MovesWithTheAlignedContent()
        {
            // Its static position is in the content, so it goes where the content goes.
            static string Table(string align) =>
                $"<table><tr><td style='vertical-align: {align}; position: relative'>" +
                "<div id='a' style='position: absolute; width: 10pt; height: 5pt'></div><b id='w'>Name</b></td>" +
                "<td style='height: 60pt'>x</td></tr></table>";

            var (top, _) = await LayoutAsync(Table("top"));
            var (middle, _) = await LayoutAsync(Table("middle"));

            var shift = Assert.Single(Find(middle, "w").Rectangles.Values).Top - Assert.Single(Find(top, "w").Rectangles.Values).Top;

            Assert.True(shift > 10, $"the content moved only {shift}pt");
            Assert.Equal(Find(top, "a").Location.Y + shift, Find(middle, "a").Location.Y, 3);
        }

        [Fact]
        public async Task EmptyPositionedInline_AfterATrailingSpaceOfRightToLeftText_SitsAfterTheSpace()
        {
            // Between two rtl words the preserved space resolves rtl, but the line wraps right after it, and
            // a space ending a line is reset to the paragraph's own ltr level (UAX #9 L1). So it is not
            // reflected with the rtl word before it, and the place after it is its right edge.
            var (root, _) = await LayoutAsync(
                "<p id='p' style='white-space: pre-wrap; width: 60pt'>abc אבג <span style='position: relative'>" +
                "<span id='a' style='position: absolute; left: 0; top: 0; width: 4pt; height: 4pt'></span></span>" +
                "דהוזחטיכלמנסעפצ</p>");

            var line = Find(root, "p").LineBoxes[0];
            var space = line.Words[^1];

            Assert.True(space.IsSpaces);
            Assert.Equal(1, space.BidiLevel % 2);
            Assert.Equal(space.Left + space.Width, Find(root, "a").Location.X, 3);
        }

        [Fact]
        public async Task AbsolutelyPositionedChild_StillGetsItsOwnContentNormalized()
        {
            // The child's own mixed inline and block content still gets its anonymous block wrappers, and
            // an inline split around a real block inside it, though the child now sits among inline content.
            var (root, _) = await LayoutAsync(
                "<div>text<div id='a' style='position: absolute'>mixed <div>block</div> tail</div>" +
                "<span><div id='b' style='position: absolute'><span id='i'>x<div>block</div>y</span></div></span></div>");

            Assert.All(Find(root, "a").Boxes, child => Assert.False(child.IsInline));
            Assert.All(Find(root, "b").Boxes, child => Assert.False(child.IsInline));
        }

        [Fact]
        public async Task MultiColumnContainer_StillWrapsTheInlineRunBesideAnAbsolutelyPositionedChild()
        {
            // The columns engine takes each child as block-level column content, so the words beside the
            // positioned child keep their anonymous block and share one line.
            var (root, _) = await LayoutAsync(
                "<div id='m' style='columns: 2; line-height: 20pt'>Some <b id='b'>bold</b> text" +
                "<span style='position: absolute; top: 0'>x</span></div>");

            var multicol = Find(root, "m");
            var wrapper = Find(root, "b").ParentBox!;

            Assert.NotSame(multicol, wrapper);
            Assert.Null(wrapper.HtmlTag);
            Assert.Single(wrapper.LineBoxes);
        }

        [Fact]
        public async Task UndisplayedAbsolutelyPositionedChild_IsNotLaidOut()
        {
            var (root, _) = await LayoutAsync(
                "<p><span>a<span id='a' style='position: absolute; display: none; width: 30pt; height: 30pt'>X</span>b</span></p>");

            var abs = Find(root, "a");

            Assert.Equal(0, abs.ActualRight - abs.Location.X, 3);
        }

        [Fact]
        public async Task AbsolutelyPositionedChild_BeforeABlockInsideAnInline_StaysInsideTheInline()
        {
            var (root, _) = await LayoutAsync(
                "<div><span id='s' style='position: relative'>a<b id='a' style='position: absolute; left: 5pt; top: 0; width: 4pt; height: 4pt'></b>" +
                "<div>block</div>c</span></div>");

            var abs = Find(root, "a");
            var owner = abs.ParentBox!;

            Assert.Equal("s", owner.HtmlTag?.TryGetAttribute("id"));
            var fragment = Assert.Single(owner.Rectangles.Values);
            Assert.Equal(fragment.Left + 5, abs.Location.X, 3);
        }

        [Fact]
        public async Task RepeatedHeaderWithAnOutline_DrawsOneRingPerPage()
        {
            var rows = string.Concat(Enumerable.Range(0, 30).Select(i => $"<tr><td>row {i}</td></tr>"));
            var (_, container) = await LayoutAsync(
                "<table><thead style='display: table-header-group'><tr><th style='outline: 2pt solid rgb(217,74,74)'>head</th></tr></thead>" +
                $"<tbody>{rows}</tbody></table>",
                pageHeight: 200);

            Assert.True(container.FragmentTree!.Fragmentainers.Count > 1);
            for (var page = 0; page < container.FragmentTree.Fragmentainers.Count; page++)
            {
                var g = new TestRecordingGraphics();
                FragmentPaintHarness.PaintPage(container, g, page);
                Assert.Single(g.Log, IsRing);
            }
        }

        [Theory]
        [InlineData("<th style='text-align: left'>Name <span style='position: relative'>i" +
                    "<span style='position: absolute; left: 0; top: 100%'>v</span></span></th><th>Other</th>")]
        [InlineData("<th style='position: relative; background: #fe9'>Name <span style='position: relative'>inl" +
                    "<span style='position: absolute; left: 0; top: 100%; width: 30px'>H</span></span> tail</th>")]
        public async Task RepeatedHeaderHoldingAnAbsolutelyPositionedChild_KeepsItsTextOnEveryPage(string headerCells)
        {
            var rows = string.Concat(Enumerable.Range(0, 30).Select(i => $"<tr><td>row {i}</td><td>x</td></tr>"));
            var (_, container) = await LayoutAsync(
                $"<table style='width: 100%'><thead><tr>{headerCells}</tr></thead><tbody>{rows}</tbody></table>",
                pageHeight: 200);

            var pages = container.FragmentTree!.Fragmentainers;
            Assert.True(pages.Count > 1);
            Assert.All(pages, page =>
                Assert.Contains(Flatten(page.Root).SelectMany(f => f.Words), w => w.Word.Text == "Name"));
        }

        [Theory]
        [InlineData("left")]
        [InlineData("center")]
        public async Task EmptyPositionedInline_IsTheContainingBlock_AtItsPlaceInTheLine(string textAlign)
        {
            // The inline holds only the badge, so it has no line fragment. It still sits on the line
            // between the two words, a zero-width box there, and `text-align` moves it with them.
            var (root, _) = await LayoutAsync(
                $"<div style='position: relative; margin: 60pt 0 0 100pt; padding: 10pt'><p style='text-align: {textAlign}'>" +
                "<b id='before'>text</b><span style='position: relative'><span id='a' style='position: absolute; " +
                "left: 20pt; top: 40pt; width: 40pt; height: 14pt'></span></span><b id='after'>more</b></p></div>");

            var before = Assert.Single(Find(root, "before").Rectangles.Values);
            var after = Assert.Single(Find(root, "after").Rectangles.Values);
            var abs = Find(root, "a");

            Assert.Equal(before.Right, after.Left, 3);
            Assert.Equal(before.Right + 20, abs.Location.X, 3);
            Assert.Equal(before.Top + 40, abs.Location.Y, 3);
            Assert.Equal(40, abs.ActualRight - abs.Location.X, 3);
        }

        [Theory]
        [InlineData("center")]
        [InlineData("right")]
        public async Task EmptyPositionedInline_AtTheStartOfAnAlignedLine_MovesWithTheWordAfterIt(string textAlign)
        {
            var (root, _) = await LayoutAsync(
                $"<p style='text-align: {textAlign}'><span style='position: relative'><span id='a' style='position: absolute; " +
                "left: 0; top: 0; width: 4pt; height: 4pt'></span></span><b id='after'>word</b></p>");

            var after = Assert.Single(Find(root, "after").Rectangles.Values);

            Assert.Equal(after.Left, Find(root, "a").Location.X, 3);
        }

        [Fact]
        public async Task EmptyPositionedInline_AfterASpaceOnAJustifiedLine_SitsAgainstTheNextWord()
        {
            // Justification widens the space before the inline, so the place moves with the word after it.
            var (root, _) = await LayoutAsync(
                "<p style='text-align: justify; width: 150pt'><b id='first'>aa</b> bb <span style='position: relative'>" +
                "<span id='a' style='position: absolute; left: 0; top: 0; width: 4pt; height: 4pt'></span></span>" +
                "<b id='next'>cc</b> dd ee ff gg hh ii jj kk ll mm nn oo pp qq rr ss tt uu vv ww xx yy zz</p>");

            var first = Assert.Single(Find(root, "first").Rectangles.Values);
            var next = Assert.Single(Find(root, "next").Rectangles.Values);

            Assert.Equal(first.Top, next.Top, 3);
            Assert.Equal(next.Left, Find(root, "a").Location.X, 3);
        }

        [Theory]
        [InlineData("dir='rtl'", 1.0)]
        [InlineData("style='text-align: center'", 0.5)]
        [InlineData("style='text-align: right'", 1.0)]
        [InlineData("dir='rtl' style='text-align: left'", 0.0)]
        public async Task EmptyPositionedInline_OnALineWithNoWords_IsAlignedAsTheLineIs(string attributes, double fraction)
        {
            var (root, _) = await LayoutAsync(
                $"<div id='d' {attributes}><span style='position: relative'><span id='a' style='position: absolute; " +
                "left: 0; top: 0; width: 4pt; height: 4pt'></span></span></div>");

            var d = Find(root, "d");

            Assert.Equal(d.ClientLeft + (d.ClientRight - d.ClientLeft) * fraction, Find(root, "a").Location.X, 3);
        }

        [Fact]
        public async Task EmptyPositionedInline_OnALineWithNoWords_StartsAtTheLinesTop()
        {
            var (root, _) = await LayoutAsync(
                "<p id='p' style='margin: 300pt 0 0 50pt'><span style='position: relative'><span id='a' style='position: absolute; " +
                "left: 0; top: 0; width: 4pt; height: 4pt'></span></span></p>");

            var p = Find(root, "p");
            var abs = Find(root, "a");

            Assert.Equal(p.ClientLeft, abs.Location.X, 3);
            Assert.Equal(p.ClientTop, abs.Location.Y, 3);
        }

        [Fact]
        public async Task EmptyPositionedInline_InRightToLeftText_SitsWhereTheNextWordInReadingOrderBegins()
        {
            // In an rtl run the first word in reading order is the rightmost, so the place just after it is
            // its left edge, which is the right edge of the word after it.
            var (root, _) = await LayoutAsync(
                "<p dir='rtl' style='text-align: left'><b id='before'>אבג</b><span style='position: relative'>" +
                "<span id='a' style='position: absolute; left: 0; top: 0; width: 4pt; height: 4pt'></span></span>" +
                "<b id='after'>דהו</b></p>");

            var before = Assert.Single(Find(root, "before").Rectangles.Values);
            var after = Assert.Single(Find(root, "after").Rectangles.Values);

            Assert.True(after.Right <= before.Left + 0.001, "the rtl run is not reordered");
            Assert.Equal(before.Left, Find(root, "a").Location.X, 3);
        }

        [Fact]
        public async Task EmptyPositionedInline_AtTheStartOfTheLine_AnchorsAtTheLineStart()
        {
            var (root, _) = await LayoutAsync(
                "<div style='margin-left: 100pt'><p id='p'><span style='position: relative; padding-left: 3pt'>" +
                "<span id='a' style='position: absolute; left: 0; top: 0; width: 4pt; height: 4pt'></span></span>" +
                "<b id='after'>word</b></p></div>");

            var p = Find(root, "p");
            var after = Assert.Single(Find(root, "after").Rectangles.Values);
            var abs = Find(root, "a");

            // The padding box's left edge is the inline's own left edge, at the paragraph's content edge.
            Assert.Equal(p.ClientLeft, abs.Location.X, 3);
            Assert.Equal(after.Top, abs.Location.Y, 3);
        }

        [Theory]
        [InlineData("<span style='outline: 2pt solid rgb(217,74,74)'>text<span style='position: absolute'>x</span></span>")]
        [InlineData("<span style='position: relative; z-index: 0; outline: 2pt solid rgb(217,74,74)'>text" +
                    "<span style='position: absolute; z-index: 1'>x</span></span>")]
        public async Task InlineHoldingAnAbsolutelyPositionedChild_DrawsItsOutline(string markup)
        {
            var (_, container) = await LayoutAsync($"<div>{markup}</div>");

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintPage(container, g);

            Assert.Contains(g.Log, IsRing);
        }

        private static bool IsRing(object e) =>
            e is TestRecordingGraphics.DrawPathCall { Stroked: false } p && p.Color == Ring
            || e is TestRecordingGraphics.DrawRectCall r && r.Color == Ring
            || e is TestRecordingGraphics.DrawPolygonCall q && q.Color == Ring;

        [Fact]
        public async Task InlineSplitAroundABlock_KeepsItsOutlineOnEveryPiece()
        {
            var (root, _) = await LayoutAsync(
                "<div><span id='s' style='outline: 2pt dashed rgb(217,74,74); outline-offset: 1pt'>a<div>b</div>c</span></div>");

            var pieces = Descendants(root).Where(b => b.HtmlTag?.TryGetAttribute("id") == "s").ToList();

            Assert.Equal(2, pieces.Count);
            Assert.All(pieces, piece =>
            {
                Assert.Equal(Ring, piece.ActualOutlineColor);
                Assert.Equal(2, piece.ActualOutlineWidth, 3);
                Assert.Equal(1, piece.ActualOutlineOffset, 3);
            });
        }

        private static Task<(CssBox Root, HtmlContainerInt Container)> LayoutAsync(string body, double pageHeight = 842) =>
            LayoutHarness.LayoutAsync(LayoutHarness.Wrap(body), pageHeight: pageHeight);

        private static CssBox Find(CssBox root, string id) =>
            LayoutHarness.FindById(root, id) ?? throw new Xunit.Sdk.XunitException($"no box #{id}");

        private static System.Collections.Generic.IEnumerable<CssBox> Descendants(CssBox root) =>
            LayoutHarness.Descendants(root);

        private static System.Collections.Generic.IEnumerable<BoxFragment> Flatten(
            BoxFragment fragment) =>
            fragment.Children.SelectMany(Flatten).Prepend(fragment);
    }
}
