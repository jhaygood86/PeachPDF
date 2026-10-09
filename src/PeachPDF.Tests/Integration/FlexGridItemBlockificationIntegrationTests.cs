using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// CSS Display 3 §2.7 / css-flexbox-1 §4 / css-grid-2 §6: every in-flow child of a flex or grid
    /// container has a blockified computed <c>display</c>. Asserts the computed value on the box, and
    /// that a replaced item stays the item itself rather than gaining the block wrapper a
    /// <c>display:block</c> image normally does.
    /// </summary>
    public class FlexGridItemBlockificationIntegrationTests
    {
        // A 40x20px PNG: 30x15pt, a 2:1 ratio that comes from the decoded bitmap rather than an SVG's own size.
        private const string RedPng = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACgAAAAUCAIAAABwJOjsAAAAOElEQVR4nO3NQQEAMAgDsa6S8C9gsvYdBo4HjYGcW6UJHlmVGGQy+yXGmKu6xBhzVZcYY67S8vgB2VcBVG4eWXEAAAAASUVORK5CYII=";

        private const string RedSvg = "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='96' height='48'%3E%3Crect width='96' height='48' fill='red'/%3E%3C/svg%3E";

        [Theory]
        [InlineData("flex", "inline", "Block")]
        [InlineData("flex", "inline-block", "Block")]
        [InlineData("flex", "inline-flex", "Flex")]
        [InlineData("flex", "inline-grid", "Grid")]
        [InlineData("flex", "inline-table", "Table")]
        [InlineData("grid", "inline", "Block")]
        [InlineData("grid", "inline-block", "Block")]
        [InlineData("grid", "inline-flex", "Flex")]
        [InlineData("inline-flex", "inline-block", "Block")]
        [InlineData("inline-grid", "inline", "Block")]
        public async Task InlineLevelItem_ComputesToItsBlockLevelEquivalent(
            string container, string item, string expected)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div style='display:{container}'><span id='item' style='display:{item}'>x</span></div>"));

            Assert.Equal(expected, LayoutHarness.FindById(root, "item")!.Display.Value.ToString());
        }

        [Fact]
        public async Task DefaultInlineElement_InFlexContainer_ComputesToBlock()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div style='display:flex'><span id='item'>x</span></div>"));

            Assert.Equal(DisplayMode.Block, LayoutHarness.FindById(root, "item")!.Display.Value);
        }

        [Fact]
        public async Task InlineLevelChild_OfNonFlexContainer_KeepsItsDisplay()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div><span id='a' style='display:inline-block'>x</span></div>"));

            Assert.Equal(DisplayMode.InlineBlock, LayoutHarness.FindById(root, "a")!.Display.Value);
        }

        [Theory]
        [InlineData("flex")]
        [InlineData("grid")]
        public async Task ReplacedItem_ComputesToBlock_AndIsTheItemItself(string container)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='c' style='display:{container}; width:300pt'><img id='img' src=\"{RedSvg}\" /></div>"));

            var c = LayoutHarness.FindById(root, "c")!;
            var img = LayoutHarness.FindById(root, "img")!;

            // The 96px source keeps its natural 72pt width: in a flex row it is a flex base size, in a
            // grid `justify-self: normal` lets a replaced item keep its natural size (css-grid-2 §6.2).
            Assert.Equal(72, img.ActualWidth, 1.0);

            Assert.Equal(DisplayMode.Block, img.Display.Value);
            Assert.Same(c, img.ParentBox);
            Assert.False(img.ParentBox!.IsReplacedBlockWrapper);
        }

        [Fact]
        public async Task InlineSvgItem_InFlexContainer_KeepsIntrinsicSize()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='c' style='display:flex; width:300pt'>" +
                "<svg id='svg' width='96' height='48'><rect width='96' height='48'/></svg></div>"));

            var c = LayoutHarness.FindById(root, "c")!;
            var svg = LayoutHarness.FindById(root, "svg")!;

            Assert.Equal(DisplayMode.Block, svg.Display.Value);
            Assert.Same(c, svg.ParentBox);
            Assert.Equal(72, svg.ActualWidth, 1.0);
        }

        [Fact]
        public async Task BlockImage_OutsideFlexContainer_StillGetsItsWrapper()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='c'><img id='img' style='display:block' src=\"{RedSvg}\" /></div>"));

            var img = LayoutHarness.FindById(root, "img")!;

            Assert.True(img.ParentBox!.IsReplacedBlockWrapper);
            Assert.Equal(DisplayMode.Inline, img.Display.Value);
        }

        // Before the cascade blockified these, a nested inline-flex/-grid/-table item reached layout as an
        // inline-level box: its own children laid out at (0,0) with no size, so its gap, alignment and
        // tracks were ignored (and an inline-table's cells did not paint at all).

        [Fact]
        public async Task NestedInlineFlexItem_LaysOutItsOwnChildrenAsAFlexContainer()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div style='display:flex; width:300pt'>" +
                "<div id='i' style='display:inline-flex; align-items:center; gap:5pt'>" +
                "<span id='a'>aa</span><span id='b' style='height:30pt'>bb</span></div>" +
                "<span id='z'>zz</span></div>"));

            var a = LayoutHarness.FindById(root, "a")!;
            var b = LayoutHarness.FindById(root, "b")!;
            var i = LayoutHarness.FindById(root, "i")!;
            var z = LayoutHarness.FindById(root, "z")!;

            Assert.Equal("Flex", i.Display.Value.ToString());
            Assert.True(a.ActualWidth > 0, "the container's own item is laid out, not left at 0x0");
            Assert.Equal(a.Location.X + a.ActualWidth + 5, b.Location.X, 1.0);        // gap: 5pt
            Assert.Equal(30, b.ActualHeight, 1.0);
            Assert.Equal(b.Location.Y + (b.ActualHeight - a.ActualHeight) / 2, a.Location.Y, 1.0); // align-items: center
            Assert.Equal(i.Location.X + i.ActualWidth, z.Location.X, 1.0);
        }

        [Fact]
        public async Task NestedInlineGridItem_HonoursItsTracks()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div style='display:flex; width:300pt'>" +
                "<div id='i' style='display:inline-grid; grid-template-columns:50pt 50pt'>" +
                "<span id='a'>aa</span><span id='b'>bb</span></div></div>"));

            var a = LayoutHarness.FindById(root, "a")!;
            var b = LayoutHarness.FindById(root, "b")!;

            Assert.Equal(50, a.ActualWidth, 1.0);
            Assert.Equal(50, b.Location.X - a.Location.X, 1.0);
        }

        [Fact]
        public async Task NestedInlineTableItem_LaysOutItsCells()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div style='display:flex; width:300pt'>" +
                "<div id='i' style='display:inline-table'><div style='display:table-row'>" +
                "<div id='a' style='display:table-cell'>aa</div>" +
                "<div id='b' style='display:table-cell'>bb</div></div></div></div>"));

            var a = LayoutHarness.FindById(root, "a")!;
            var b = LayoutHarness.FindById(root, "b")!;

            Assert.True(a.ActualWidth > 0 && b.ActualWidth > 0, "cells have a size");
            Assert.Equal(a.Location.X + a.ActualWidth, b.Location.X, 1.0);
        }

        [Theory]
        [InlineData("<iframe id='f' srcdoc='x' style='width:80pt;height:40pt'></iframe>")]
        [InlineData("<video id='f' style='width:80pt;height:40pt'></video>")]
        [InlineData("<object id='f' data='x.png' style='width:80pt;height:40pt'></object>")]
        public async Task OtherReplacedItems_KeepTheirDeclaredSize_AndTheirSiblingFollows(string replaced)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div style='display:flex; width:300pt'>{replaced}<span id='z'>zz</span></div>"));

            var f = LayoutHarness.FindById(root, "f")!;
            var z = LayoutHarness.FindById(root, "z")!;

            Assert.Equal(80, f.ActualWidth, 1.0);
            Assert.Equal(40, f.ActualHeight, 1.0);
            Assert.Equal(f.Location.X + 80, z.Location.X, 1.0);
        }

        // An out-of-flow replaced child of a flex container is not an item. It used to be wrapped in an
        // in-flow IsReplacedBlockWrapper, which then DID take part in the flex line: a fixed image pushed
        // its sibling 72pt along and made the container as tall as the image.

        [Theory]
        [InlineData("absolute")]
        [InlineData("fixed")]
        public async Task OutOfFlowImage_InFlexContainer_TakesNoPartInTheFlexLine(string position)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='c' style='display:flex; position:relative; width:300pt; height:100pt'>" +
                $"<img id='img' style='position:{position}; top:5pt; left:7pt' src=\"{RedSvg}\" />" +
                "<span id='z'>zz</span></div>"));

            var c = LayoutHarness.FindById(root, "c")!;
            var img = LayoutHarness.FindById(root, "img")!;
            var z = LayoutHarness.FindById(root, "z")!;

            Assert.Equal(c.Location.X, z.Location.X, 0.5);
            Assert.Equal(72, img.ActualWidth, 1.0);
            Assert.Equal(c.Location.X + 7, img.Location.X, 1.0);
            Assert.Equal(c.Location.Y + 5, img.Location.Y, 1.0);
        }

        [Fact]
        public async Task InlineItemHoldingABlock_StacksItsContentInsteadOfOverlappingIt()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div style='display:flex; width:300pt; font-size:20pt'>" +
                "<span id='s'>pre<div id='d'>b</div>post</span><span id='z'>zz</span></div>"));

            var s = LayoutHarness.FindById(root, "s")!;
            var d = LayoutHarness.FindById(root, "d")!;

            // pre / b / post: three lines, the block sitting on the second rather than over the first.
            Assert.Equal(3 * d.ActualHeight, s.ActualHeight, 1.0);
            Assert.Equal(s.Location.Y + d.ActualHeight, d.Location.Y, 1.0);
        }

        [Fact]
        public async Task FormControlItems_LaySideBySide()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div style='display:flex; width:300pt'><input id='f' type='text' value='hi'>" +
                "<button id='g'>go</button><span id='z'>zz</span></div>"));

            var f = LayoutHarness.FindById(root, "f")!;
            var g = LayoutHarness.FindById(root, "g")!;
            var z = LayoutHarness.FindById(root, "z")!;

            Assert.True(f.ActualWidth > 0 && g.ActualWidth > 0);
            Assert.Equal(f.Location.X + f.ActualWidth, g.Location.X, 1.0);
            Assert.Equal(g.Location.X + g.ActualWidth, z.Location.X, 1.0);
        }

        [Fact]
        public async Task FloatedItem_IsPlacedInTheFlexLine_NotFloated()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div style='display:flex; width:300pt'><span id='a' style='float:left'>a</span>" +
                "<span id='z'>zz</span></div>"));

            var a = LayoutHarness.FindById(root, "a")!;
            var z = LayoutHarness.FindById(root, "z")!;

            Assert.Equal(a.Location.Y, z.Location.Y, 0.5);
            Assert.Equal(a.Location.X + a.ActualWidth, z.Location.X, 1.0);
        }

        [Fact]
        public async Task DisplayContentsText_UnderAFlexContainer_KeepsInlineTextRunBoxes()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='c' style='display:flex; width:300pt'><div style='display:contents'>some text" +
                "<b id='b'>x</b> more</div><span id='z'>zz</span></div>"));

            var c = LayoutHarness.FindById(root, "c")!;
            var textRuns = new System.Collections.Generic.List<CssBox>();
            void Collect(CssBox box)
            {
                if (box.HtmlTag is null && !string.IsNullOrWhiteSpace(box.Text)) textRuns.Add(box);
                foreach (var child in box.Boxes) Collect(child);
            }
            Collect(c);

            Assert.NotEmpty(textRuns);
            Assert.All(textRuns, run => Assert.True(run.Words.Count > 0, $"text run '{run.Text}' was parsed to words"));
            Assert.All(textRuns, run => Assert.Equal("Inline", run.Display.Value.ToString()));
            Assert.True(LayoutHarness.FindById(root, "z")!.Location.X > c.Location.X);
        }

        [Theory]
        [InlineData("")]
        [InlineData("display:block;")]
        public async Task ReplacedGridItem_KeepsItsNaturalSize_UnderJustifySelfNormal(string imgStyle)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div style='display:grid; grid-template-columns:100pt 100pt; width:300pt'>" +
                $"<img id='img' style='{imgStyle}' src=\"{RedSvg}\" /><span id='z'>zz</span></div>"));

            var img = LayoutHarness.FindById(root, "img")!;
            var z = LayoutHarness.FindById(root, "z")!;

            Assert.Equal(72, img.ActualWidth, 1.0);
            Assert.True(img.ActualHeight < 60, $"natural height, not the row's: {img.ActualHeight}");
            Assert.Equal(100, z.ActualWidth, 1.0);   // a non-replaced item still stretches across its track
            Assert.Equal(img.Location.X + 100, z.Location.X, 1.0);
        }

        // A grid with two 100pt tracks: a 96x48px (72x36pt) image in the first, a tall sibling in the
        // same row so the block axis has room to align in.
        private static string GridWithImage(string containerStyle, string itemTag) =>
            "<div id='c' style='display:grid; grid-template-columns:100pt 100pt; width:300pt; " +
            containerStyle + "'>" + itemTag + "<span id='tall' style='height:100pt'>zz</span></div>";

        [Theory]
        // inline axis, from justify-items and from justify-self; 72pt image in a 100pt track
        [InlineData("justify-items:start", "", 0)]
        [InlineData("justify-items:center", "", 14)]
        [InlineData("justify-items:end", "", 28)]
        [InlineData("", "justify-self:start", 0)]
        [InlineData("", "justify-self:center", 14)]
        [InlineData("", "justify-self:end", 28)]
        [InlineData("justify-items:center", "display:block;", 14)]
        [InlineData("justify-items:end", "display:block;", 28)]
        [InlineData("", "display:block; justify-self:center", 14)]
        [InlineData("", "display:block; justify-self:start", 0)]
        // both axes aligned at once: the item used to be pinned to the track, stretched and overlapped
        [InlineData("align-items:center", "display:block; justify-self:center", 14)]
        [InlineData("align-items:center", "justify-self:center", 14)]
        [InlineData("align-items:end; justify-items:end", "display:block;", 28)]
        [InlineData("align-items:start; justify-items:center", "", 14)]
        public async Task ReplacedGridItem_UnderAStartCenterOrEndInlineAlignment_KeepsItsNaturalWidth(
            string containerStyle, string imgStyle, double expectedOffset)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                GridWithImage(containerStyle, $"<img id='img' style='{imgStyle}' src=\"{RedSvg}\" />")));

            var c = LayoutHarness.FindById(root, "c")!;
            var img = LayoutHarness.FindById(root, "img")!;

            Assert.Equal(72, img.ActualWidth, 1.0);
            Assert.Equal(expectedOffset, img.Location.X - c.Location.X, 1.0);
        }

        [Theory]
        [InlineData("align-items:start", "", 0)]
        [InlineData("align-items:center", "", 1)]
        [InlineData("align-items:end", "", 2)]
        [InlineData("", "align-self:center", 1)]
        [InlineData("", "align-self:end", 2)]
        [InlineData("align-items:center", "display:block;", 1)]
        [InlineData("", "display:block; align-self:end", 2)]
        [InlineData("", "", 0)]                               // align-items: normal
        public async Task ReplacedGridItem_AlongTheBlockAxis_KeepsItsNaturalHeightAndAligns(
            string containerStyle, string imgStyle, int where)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                GridWithImage(containerStyle, $"<img id='img' style='{imgStyle}' src=\"{RedSvg}\" />")));

            var c = LayoutHarness.FindById(root, "c")!;
            var img = LayoutHarness.FindById(root, "img")!;

            Assert.Equal(72, img.ActualWidth, 1.0);
            Assert.True(img.ActualHeight < 60, $"natural height, not the 100pt row's: {img.ActualHeight}");

            var free = 100 - img.ActualHeight;
            var expected = where switch { 0 => 0.0, 1 => free / 2, _ => free };
            Assert.Equal(expected, img.Location.Y - c.Location.Y, 1.5);
        }

        [Theory]
        [InlineData("")]
        [InlineData("justify-self:center")]
        public async Task RasterImageGridItem_KeepsItsNaturalSize(string style)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                GridWithImage("", $"<img id='img' style='{style}' src=\"{RedPng}\" />")));

            var img = LayoutHarness.FindById(root, "img")!;

            Assert.Equal(30, img.ActualWidth, 1.0);
        }

        [Theory]
        [InlineData("")]
        [InlineData("justify-self:center")]
        public async Task InlineSvgGridItem_KeepsItsNaturalSize(string style)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                GridWithImage("", $"<svg id='s' style='{style}' width='96' height='48'><rect width='96' height='48'/></svg>")));

            var svg = LayoutHarness.FindById(root, "s")!;

            Assert.Equal(72, svg.ActualWidth, 1.0);
            Assert.Equal(36, svg.ActualHeight, 1.0);
        }

        // A grid with an explicit row height never measures its items' heights up front, so the image's own
        // size has to be established when it is placed.
        [Theory]
        [InlineData("justify-items:center; align-items:center", "display:block;", 14, 22)]
        [InlineData("justify-items:center; align-items:center", "", 14, 22)]
        [InlineData("justify-items:end; align-items:end", "display:block;", 28, 44)]
        [InlineData("justify-items:start; align-items:start", "display:block;", 0, 0)]
        [InlineData("", "display:block; justify-self:center; align-self:center", 14, 22)]
        [InlineData("", "justify-self:center; align-self:center", 14, 22)]
        [InlineData("justify-items:center; align-items:center; place-content:center", "display:block;", 64, 22)]
        [InlineData("justify-items:center", "display:block;", 14, 0)]
        [InlineData("align-items:center", "display:block;", 0, 22)]
        public async Task ReplacedGridItem_InARowWithAnExplicitHeight_KeepsItsNaturalSizeAndAligns(
            string containerStyle, string imgStyle, double expectedX, double expectedY)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                GridWithImage("grid-template-rows:80pt; " + containerStyle, $"<img id='img' style='{imgStyle}' src=\"{RedSvg}\" />")));

            var c = LayoutHarness.FindById(root, "c")!;
            var img = LayoutHarness.FindById(root, "img")!;

            Assert.Equal(72, img.ActualWidth, 1.0);
            Assert.True(img.ActualHeight < 60, $"natural height, not the 80pt row's: {img.ActualHeight}");
            Assert.Equal(expectedX, img.Location.X - c.Location.X, 1.0);

            // block axis: the image is ~36pt tall (plus the known strut quirk), in an 80pt row
            var free = 80 - img.ActualHeight;
            var expected = expectedY switch { 0 => 0.0, 22 => free / 2, _ => free };
            Assert.Equal(expected, img.Location.Y - c.Location.Y, 1.5);
        }

        // The height of an auto row is measured with the item at its natural width. Pinned to the track
        // instead, a 2:1 image is scaled up to ~52.8pt tall and the whole row grows with it.
        [Theory]
        [InlineData("", "", true)]
        [InlineData("justify-items:center", "", true)]
        [InlineData("justify-items:start", "", true)]
        [InlineData("justify-items:end", "display:block;", true)]
        [InlineData("", "justify-self:center", true)]
        [InlineData("", "justify-self:stretch", false)]   // an explicit stretch really is scaled to the track
        [InlineData("justify-items:stretch", "", false)]
        public async Task ReplacedGridItem_AutoRowHeight_IsMeasuredAtTheNaturalWidthUnlessStretched(
            string containerStyle, string imgStyle, bool naturalHeight)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='c' style='display:grid; grid-template-columns:100pt 100pt; width:300pt; " + containerStyle + "'>" +
                $"<img id='img' style='{imgStyle}' src=\"{RedSvg}\" /><span>a</span><span id='next'>next</span></div>"));

            var c = LayoutHarness.FindById(root, "c")!;
            var next = LayoutHarness.FindById(root, "next")!;
            var firstRow = next.Location.Y - c.Location.Y;

            if (naturalHeight)
                Assert.True(firstRow < 45, $"first row is the natural ~38.8pt, not the scaled ~52.8pt: {firstRow}");
            else
                Assert.True(firstRow > 50, $"first row grew to the scaled image: {firstRow}");
        }

        // An svg with only one of width/height has a natural size in that axis alone (and no ratio to derive
        // the other from), so each axis is decided on its own. Known deviation: Chrome gives the missing axis
        // the 300x150px default (72 x 112.5pt for the width-only case below) instead of stretching it.
        [Fact]
        public async Task SvgImageWithOnlyAWidth_KeepsItsWidth_AndStretchesItsHeightInTheRow()
        {
            const string widthOnly = "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='96'%3E%3Crect width='96' height='48' fill='red'/%3E%3C/svg%3E";
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                GridWithImage("grid-template-rows:80pt", $"<img id='img' src=\"{widthOnly}\" />")));

            var img = LayoutHarness.FindById(root, "img")!;

            Assert.Equal(72, img.ActualWidth, 1.0);
            Assert.Equal(80, img.ActualHeight, 1.0);
        }

        [Fact]
        public async Task SvgImageWithOnlyAHeight_FillsItsWidth_AndKeepsItsHeightInTheRow()
        {
            const string heightOnly = "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' height='48'%3E%3Crect width='96' height='48' fill='red'/%3E%3C/svg%3E";
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                GridWithImage("grid-template-rows:80pt", $"<img id='img' src=\"{heightOnly}\" />")));

            var img = LayoutHarness.FindById(root, "img")!;

            Assert.Equal(100, img.ActualWidth, 1.0);
            Assert.True(img.ActualHeight < 60, $"natural height, not the 80pt row's: {img.ActualHeight}");
        }

        [Theory]
        [InlineData("", "align-self:stretch")]
        [InlineData("align-items:stretch", "")]
        [InlineData("align-items:stretch", "display:block;")]
        public async Task ReplacedGridItem_WithAnExplicitBlockAxisStretch_FillsTheHeightAndKeepsItsRatioWidth(
            string containerStyle, string imgStyle)
        {
            // 80pt tall at a 2:1 ratio would be 160pt wide, clamped to the 100pt track. Known deviation: Chrome
            // lets it overflow to 160pt unless the page sets `max-width: 100%`, which gives 100pt as here.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                GridWithImage("grid-template-rows:80pt; " + containerStyle, $"<img id='img' style='{imgStyle}' src=\"{RedSvg}\" />")));

            var img = LayoutHarness.FindById(root, "img")!;

            Assert.Equal(100, img.ActualWidth, 1.0);
            Assert.Equal(80, img.ActualHeight, 1.0);
        }

        [Fact]
        public async Task ReplacedGridItem_WithAnExplicitBlockAxisStretch_TakesItsWidthFromTheStretchedHeight()
        {
            // 30pt tall at 2:1 is 60pt wide, which fits the 100pt track, so it is not widened to it.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                GridWithImage("grid-template-rows:30pt; align-items:stretch", $"<img id='img' src=\"{RedSvg}\" />")));

            var img = LayoutHarness.FindById(root, "img")!;

            Assert.Equal(60, img.ActualWidth, 1.0);
            Assert.Equal(30, img.ActualHeight, 1.0);
        }

        [Theory]
        [InlineData(30, 60)]    // 2:1 ratio from the stretched height: 30pt tall is 60pt wide
        [InlineData(80, 100)]   // 80pt tall would be 160pt wide, clamped to the 100pt track
        public async Task RasterImageGridItem_WithAnExplicitBlockAxisStretch_TakesItsWidthFromTheStretchedHeight(
            int rowHeight, int expectedWidth)
        {
            // An inverted ratio (height / width) would give 15pt (and 40pt) instead.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                GridWithImage($"grid-template-rows:{rowHeight}pt; align-items:stretch", $"<img id='img' src=\"{RedPng}\" />")));

            var img = LayoutHarness.FindById(root, "img")!;

            Assert.Equal(expectedWidth, img.ActualWidth, 1.0);
            Assert.Equal(rowHeight, img.ActualHeight, 1.0);
        }

        [Theory]
        [InlineData("justify-items:center", "")]
        [InlineData("justify-items:start", "")]
        [InlineData("justify-items:end", "")]
        [InlineData("", "justify-self:center")]
        [InlineData("", "justify-self:end")]
        [InlineData("align-items:center", "")]
        [InlineData("justify-items:center; align-items:center", "")]
        [InlineData("grid-template-rows:80pt; justify-items:center; align-items:center", "")]
        public async Task ViewBoxOnlyGridItem_StretchesAcrossItsTrack_UnderAnyAlignment(string containerStyle, string style)
        {
            const string viewBoxOnly = "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 96 48'%3E%3Crect width='96' height='48' fill='red'/%3E%3C/svg%3E";

            foreach (var item in new[]
            {
                $"<svg id='s' style='{style}' viewBox='0 0 96 48'><rect width='96' height='48' fill='red'/></svg>",
                $"<img id='s' style='{style}' src=\"{viewBoxOnly}\" />"
            })
            {
                var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(GridWithImage(containerStyle, item)));

                var s = LayoutHarness.FindById(root, "s")!;
                var c = LayoutHarness.FindById(root, "c")!;

                Assert.Equal(100, s.ActualWidth, 1.0);
                Assert.Equal(c.Location.X, s.Location.X, 1.0);
            }
        }

        [Fact]
        public async Task ViewBoxOnlyInlineSvg_HasNoNaturalSize_SoItStillStretchesAcrossItsTrack()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                GridWithImage("", "<svg id='s' viewBox='0 0 96 48'><rect width='96' height='48'/></svg>")));

            Assert.Equal(100, LayoutHarness.FindById(root, "s")!.ActualWidth, 1.0);
        }

        [Fact]
        public async Task ViewBoxOnlySvgImage_HasNoNaturalSize_SoItStillStretchesAcrossItsTrack()
        {
            const string viewBoxOnly = "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 96 48'%3E%3Crect width='96' height='48' fill='red'/%3E%3C/svg%3E";
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                GridWithImage("", $"<img id='img' src=\"{viewBoxOnly}\" />")));

            Assert.Equal(100, LayoutHarness.FindById(root, "img")!.ActualWidth, 1.0);
        }

        [Fact]
        public async Task InlineSvgWithOnlyAWidthAndAViewBox_HasANaturalSize()
        {
            // A width plus a viewBox gives both a natural width and (through the ratio) a natural height.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                GridWithImage("", "<svg id='s' width='96' viewBox='0 0 96 48'><rect width='96' height='48'/></svg>")));

            Assert.Equal(72, LayoutHarness.FindById(root, "s")!.ActualWidth, 1.0);
        }

        [Fact]
        public async Task ReplacedGridItem_WithExplicitJustifySelfStretch_StillStretches()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div style='display:grid; grid-template-columns:100pt 100pt; width:300pt'>" +
                $"<img id='img' style='justify-self:stretch' src=\"{RedSvg}\" /></div>"));

            Assert.Equal(100, LayoutHarness.FindById(root, "img")!.ActualWidth, 1.0);
        }

        [Fact]
        public async Task TextRunsSplitByAnOutOfFlowChild_InAColumn_MeasureAsTwoLines()
        {
            // The two text runs are anonymous items the cascade never blockifies, so the intrinsic-width
            // walk has to recognise them as items of the column from the PARENT. Without that they were
            // summed onto one line: 60.24pt against the 30.94pt a column of two blocks gives.
            async Task<double> FloatWidth(string column)
            {
                var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                    "<div id='f' style='float:left'><div style='display:flex; flex-direction:column'>" +
                    column + "</div></div>"));
                return LayoutHarness.FindById(root, "f")!.ActualWidth;
            }

            var control = await FloatWidth("<div>AB CD</div><div>EF GH</div>");

            Assert.Equal(control, await FloatWidth("AB CD<span style='position:absolute'>x</span>EF GH"), 1.0);
        }

        [Fact]
        public async Task DisplayContentsWrapper_AroundABlockImage_StillLeavesTheImageAsTheItem()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='c' style='display:flex; width:300pt'><div style='display:contents'>" +
                $"<img id='img' style='display:block' src=\"{RedSvg}\" /></div><span id='z'>zz</span></div>"));

            var c = LayoutHarness.FindById(root, "c")!;
            var img = LayoutHarness.FindById(root, "img")!;
            var z = LayoutHarness.FindById(root, "z")!;

            Assert.Same(c, img.ParentBox);
            Assert.Equal(72, img.ActualWidth, 1.0);
            Assert.Equal(img.Location.X + 72, z.Location.X, 1.0);
        }

        [Fact]
        public async Task GridContainerItem_WithAnExplicitWidth_IsSizedToIt()
        {
            // The documented workaround for a grid container that is a flex item (its auto width is
            // currently taken from the flex container, not its tracks).
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div style='display:flex; width:300pt'>" +
                "<div id='i' style='display:grid; grid-template-columns:50pt 50pt; width:100pt'>" +
                "<span>aa</span><span>bb</span></div><span id='z'>zz</span></div>"));

            var i = LayoutHarness.FindById(root, "i")!;
            var z = LayoutHarness.FindById(root, "z")!;

            Assert.Equal(100, i.ActualWidth, 1.0);
            Assert.Equal(i.Location.X + 100, z.Location.X, 1.0);
        }

        [Fact]
        public async Task MathItem_KeepsItsIntrinsicSize_AndItsSiblingFollows()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div style='display:flex; width:300pt'><math id='m'><mi>x</mi><mo>+</mo><mn>1</mn></math>" +
                "<span id='z'>zz</span></div>"));

            var m = LayoutHarness.FindById(root, "m")!;
            var z = LayoutHarness.FindById(root, "z")!;

            Assert.InRange(m.ActualWidth, 1, 100);
            Assert.Equal(m.Location.X + m.ActualWidth, z.Location.X, 1.0);
        }

        [Fact]
        public async Task LineBreakBetweenFlexItems_AddsNoExtraLine()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='c' style='display:flex; width:300pt'><span id='a'>a</span><br><span id='b'>b</span></div>"));

            var c = LayoutHarness.FindById(root, "c")!;
            var a = LayoutHarness.FindById(root, "a")!;
            var b = LayoutHarness.FindById(root, "b")!;

            Assert.Equal(a.Location.Y, b.Location.Y, 0.5);
            Assert.Equal(a.ActualHeight, c.ActualHeight, 0.5);
        }

        [Fact]
        public async Task InlineBlockItem_LaysOutAsABlock_AtItsDeclaredSize()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='c' style='display:flex; flex-direction:column; width:300pt'>" +
                "<span id='a' style='display:inline-block; width:80pt; height:20pt'>x</span>" +
                "<span id='b' style='display:inline; width:60pt; height:10pt'>y</span></div>"));

            var a = LayoutHarness.FindById(root, "a")!;
            var b = LayoutHarness.FindById(root, "b")!;

            Assert.Equal(80, a.ActualWidth, 1.0);
            Assert.Equal(20, a.ActualHeight, 1.0);
            Assert.Equal(60, b.ActualWidth, 1.0);
            Assert.Equal(10, b.ActualHeight, 1.0);
            Assert.True(b.Location.Y >= a.Location.Y + 20 - 0.5, "stacked below the first item");
        }
    }
}
