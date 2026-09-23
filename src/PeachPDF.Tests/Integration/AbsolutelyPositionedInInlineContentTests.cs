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
    /// block (CSS 2.1 §10.1, 4.1).
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
