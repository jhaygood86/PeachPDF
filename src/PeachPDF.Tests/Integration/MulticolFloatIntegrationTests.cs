using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// css-multicol-1 §2: a float inside a multi-column container is positioned with regard to the column
    /// box it appears in, and the container is an independent formatting context that contains its floats
    /// (issue #1203 - a container's floated children used to keep never-assigned, all-zero geometry, and a
    /// float in a column was resolved against the whole container).
    /// </summary>
    public class MulticolFloatIntegrationTests
    {
        // 200px = 150pt container, 10px = 7.5pt gap, so two columns of 71.25pt: the second starts at
        // 6 + 71.25 + 7.5 = 84.75 (the 8px = 6pt body margin puts the container at X = 6).
        private const double ContainerLeft = 6;
        private const double SecondColumnLeft = 84.75;

        [Fact]
        public async Task ContainerHoldingOnlyFloats_LaysThemOutInItsFirstColumn()
        {
            var (root, _) = await BuildAndLayout(@"
                <div id='mc' style='columns:2; column-gap:10px; width:200px'>
                    <div id='f1' style='float:left; width:200px; height:400px;'></div>
                    <div id='f2' style='float:left; width:50px; height:20px;'></div>
                    <div id='f3' style='float:left; width:50px; height:20px;'></div>
                </div>");

            var f1 = FindById(root, "f1")!;
            var f2 = FindById(root, "f2")!;
            var f3 = FindById(root, "f3")!;

            // The first float starts at the container's edge, and is not narrowed to the column it is in
            // (a float wider than its column overflows it).
            Assert.Equal(ContainerLeft, f1.Location.X, 2);
            Assert.Equal(ContainerLeft, f1.Location.Y, 2);
            Assert.Equal(150, f1.ActualRight - f1.Location.X, 2);
            Assert.Equal(300, f1.ActualBottom - f1.Location.Y, 2);

            // The next two find no room beside it and drop below it, stacked in the order they appear.
            Assert.Equal(ContainerLeft, f2.Location.X, 2);
            Assert.Equal(f1.ActualBottom, f2.Location.Y, 2);
            Assert.Equal(ContainerLeft, f3.Location.X, 2);
            Assert.Equal(f2.ActualBottom, f3.Location.Y, 2);
        }

        [Fact]
        public async Task ContainerHoldingOnlyFloats_ContainsThem()
        {
            // css-multicol-1 §2 makes the container an independent formatting context, so its auto height
            // grows to cover the floats (CSS 2.1 §10.6.7) instead of collapsing to nothing.
            var (root, _) = await BuildAndLayout(@"
                <div id='mc' style='columns:2; column-gap:10px; width:200px'>
                    <div id='f1' style='float:left; width:50px; height:80px;'></div>
                </div>");

            var mc = FindById(root, "mc")!;
            var f1 = FindById(root, "f1")!;

            Assert.Equal(60, f1.ActualBottom - f1.Location.Y, 2);
            Assert.True(mc.ActualBottom >= f1.ActualBottom - 0.01,
                $"the container's bottom {mc.ActualBottom} must cover its float's bottom {f1.ActualBottom}");
        }

        [Fact]
        public async Task FloatRight_InTheFirstColumn_SitsAtThatColumnsRightEdge()
        {
            var (root, _) = await BuildAndLayout(TwoParagraphsAndAFloat);

            var p1 = FindById(root, "p1")!;
            var f1 = FindById(root, "f1")!;

            // Not at the container's right edge (156), which is where it landed when the container laid
            // its out-of-flow children out again at its own full width.
            Assert.Equal(p1.ActualRight, f1.ActualRight, 2);
            Assert.True(f1.ActualRight < SecondColumnLeft, $"float right edge {f1.ActualRight} must be inside column 1");
            Assert.Equal(22.5, f1.ActualRight - f1.Location.X, 2);
        }

        [Fact]
        public async Task FloatInAnAutoHeightContainer_IsContainedByIt()
        {
            var (root, _) = await BuildAndLayout(TwoParagraphsAndAFloat);

            var mc = FindById(root, "mc")!;
            var f1 = FindById(root, "f1")!;

            Assert.True(mc.ActualBottom >= f1.ActualBottom - 0.01,
                $"the container's bottom {mc.ActualBottom} must cover its float's bottom {f1.ActualBottom}");
        }

        [Fact]
        public async Task FloatRight_AtTheTopOfColumn1_DoesNotNarrowTheLinesBesideItInColumn2()
        {
            // The float covers rows Y 6..21 of the first column. Column 2's first rows share those Y
            // coordinates but are not beside the float, so they keep their full four words per line - a
            // scan that tests only the block axis left them one word each.
            var (root, container) = await BuildAndLayout(@"
                <div id='mc' style='columns:2; column-gap:10px; width:200px; font:10px monospace'>
                    <p id='p1' style='margin:0'><span id='f1' style='float:right; width:30px; height:20px'></span>aaa bbb ccc ddd eee fff ggg hhh iii jjj kkk lll mmm nnn ooo ppp aaa bbb ccc ddd eee fff ggg hhh iii jjj kkk lll mmm nnn ooo ppp</p>
                </div>");

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, root, g);

            // Word counts per row are compared with each other, not with fixed numbers: how many words fit a
            // row depends on the metrics of whatever `monospace` resolves to on the machine running this.
            static List<int> WordsPerRow(IEnumerable<TestRecordingGraphics.DrawStringCall> words) =>
                words.GroupBy(w => Math.Round(w.Point.Y)).OrderBy(r => r.Key).Select(r => r.Count()).ToList();

            var inSecondColumn = WordsPerRow(g.DrawStringCalls.Where(w => w.Point.X >= SecondColumnLeft - 0.5));
            var inFirstColumn = WordsPerRow(g.DrawStringCalls.Where(w => w.Point.X < SecondColumnLeft - 0.5));

            // Column 2's rows are all as wide as a column allows: the ones at the float's height hold as many
            // words as the ones below it, and more than one each - a scan that tests only the block axis
            // left them a single word.
            Assert.True(inSecondColumn[0] > 1, $"column 2's first row holds {inSecondColumn[0]} word(s)");
            Assert.Equal(inSecondColumn[0], inSecondColumn[1]);

            // ...while the first column's rows beside the float are shortened by it.
            Assert.True(inFirstColumn[0] < inSecondColumn[0],
                $"column 1's first row ({inFirstColumn[0]}) is beside the float, column 2's ({inSecondColumn[0]}) is not");
        }

        [Fact]
        public async Task FloatBesideBareText_DoesNotDropTheText()
        {
            // Bare text has no block around it in a multi-column parent, so it is not column content; the
            // float beside it used to send the container through the columns engine, which laid out the
            // float and none of the text.
            var (root, container) = await BuildAndLayout(@"
                <div id='mc' style='columns:2; column-gap:10px; width:200px; font:10px monospace'>
                    <span id='f1' style='float:left; width:30px; height:20px;'></span>aaa bbb ccc ddd eee fff ggg hhh</div>");

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, root, g);

            Assert.Equal(8, g.DrawStringCalls.Count);
            var f1 = FindById(root, "f1")!;
            Assert.Equal(ContainerLeft, f1.Location.X, 2);
            Assert.All(g.DrawStringCalls.Where(w => w.Point.Y < f1.ActualBottom),
                w => Assert.True(w.Point.X >= f1.ActualRight - 0.01, $"'{w.Text}' at {w.Point.X} overlaps the float"));
        }

        [Fact]
        public async Task FloatInAColumn_IsPaintedWhereItWasPlaced()
        {
            var (root, container) = await BuildAndLayout(TwoParagraphsAndAFloat);
            var f1 = FindById(root, "f1")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, root, g);

            var rect = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawRectCall>(), r => r.Color.R == 255 && r.Color.G == 0);
            Assert.Equal(f1.Location.X, rect.X, 2);
            Assert.Equal(f1.Location.Y, rect.Y, 2);
            Assert.Equal(22.5, rect.Width, 2);
            Assert.Equal(15, rect.Height, 2);
        }

        [Theory]
        [InlineData("top")]
        [InlineData("bottom")]
        public async Task PageFloat_AsTheOnlyChildOfAContainer_IsLaidOut(string keyword)
        {
            var (root, _) = await BuildAndLayout($@"
                <div id='mc' style='columns:2; column-gap:10px; width:200px'>
                    <div id='f1' style='float:{keyword}; width:50px; height:20px;'></div>
                </div>");

            var f1 = FindById(root, "f1")!;

            Assert.Equal(15, f1.ActualBottom - f1.Location.Y, 2);
            Assert.True(f1.ActualRight > f1.Location.X, "the page float is laid out, not left at zero size");
        }

        [Fact]
        public async Task MulticolContainerAfterAFloat_StillWrapsItsFirstColumnBesideIt()
        {
            // A multi-column container is placed beside a preceding float by narrowing its lines, as a block
            // is, so making it contain its own floats must not stop it seeing floats outside it.
            var (root, container) = await BuildAndLayout(@"
                <div id='outer' style='width:200px; font:10px monospace'>
                    <div id='before' style='float:left; width:40px; height:60px'></div>
                    <div id='mc' style='columns:2; column-gap:10px'><p style='margin:0'>aaa bbb ccc ddd eee fff ggg hhh</p></div>
                </div>");

            var before = FindById(root, "before")!;
            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, root, g);

            Assert.NotEmpty(g.DrawStringCalls);
            Assert.All(g.DrawStringCalls.Where(w => w.Point.Y < before.ActualBottom),
                w => Assert.True(w.Point.X >= before.ActualRight - 0.01, $"'{w.Text}' at {w.Point.X} is under the float"));
        }

        [Fact]
        public async Task MulticolContainer_KeepsItsLastChildsBottomMarginInTheGapAfterIt()
        {
            var (root, _) = await BuildAndLayout(@"
                <div id='mc' style='columns:2; column-gap:10px; width:200px; font:10px monospace'><p id='p' style='margin:0 0 24pt'>aaa bbb ccc</p></div>
                <div id='next' style='margin:0'>next</div>");

            var p = FindById(root, "p")!;
            var next = FindById(root, "next")!;

            // Whole points: the paragraph's own bottom carries the font's fractional line height.
            Assert.Equal(24, next.Location.Y - p.ActualBottom, 0);
        }

        [Fact]
        public async Task ContainerHoldingOnlyAnAbsolutelyPositionedChild_LaysItOut()
        {
            var (root, _) = await BuildAndLayout(@"
                <div id='mc' style='columns:2; column-gap:10px; width:200px; position:relative'>
                    <div id='abs' style='position:absolute; right:0; top:0; width:20px; height:10px'></div>
                </div>");

            var mc = FindById(root, "mc")!;
            var abs = FindById(root, "abs")!;

            Assert.Equal(mc.ActualRight, abs.ActualRight, 2);
            Assert.Equal(7.5, abs.ActualBottom - abs.Location.Y, 2);
        }

        [Fact]
        public async Task AbsolutelyPositionedChild_IsStillResolvedAgainstTheContainerNotAColumn()
        {
            // Only floats keep the column's own geometry: an absolutely positioned child's containing block
            // is the multi-column container itself.
            var (root, _) = await BuildAndLayout(@"
                <div id='mc' style='columns:2; column-gap:10px; width:200px; position:relative; font:10px monospace'>
                    <p style='margin:0'>aaa bbb ccc ddd eee fff ggg hhh iii jjj</p>
                    <div id='abs' style='position:absolute; right:0; top:0; width:20px; height:10px'></div>
                </div>");

            var mc = FindById(root, "mc")!;
            var abs = FindById(root, "abs")!;

            Assert.Equal(mc.ActualRight, abs.ActualRight, 2);
        }

        private const string TwoParagraphsAndAFloat = @"
            <div id='mc' style='columns:2; column-gap:10px; width:200px; font:10px monospace'>
                <p id='p1' style='margin:0'>aaa bbb ccc ddd eee fff ggg hhh iii jjj kkk lll mmm nnn ooo ppp</p>
                <div id='f1' style='float:right; width:30px; height:20px; background:#f00'></div>
                <p id='p2' style='margin:0'>aaa bbb ccc ddd eee fff ggg hhh iii jjj kkk lll mmm nnn ooo ppp</p>
            </div>";

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayout(string body)
        {
            var html = $"<!DOCTYPE html><html><head></head><body>{body}</body></html>";
            var adapter = new PdfSharpAdapter();
            adapter.PixelsPerPoint = 1.0;
            var container = new HtmlContainerInt(adapter)
            {
                MarginTop = 0,
                MarginLeft = 0,
                MarginRight = 0,
                MarginBottom = 0
            };
            await container.SetHtml(html, null);

            var size = new XSize(400, 1000);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return (container.Root!, container);
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            if (box.HtmlTag?.TryGetAttribute("id", "") == id) return box;

            foreach (var child in box.Boxes)
            {
                if (FindById(child, id) is { } found) return found;
            }

            return null;
        }
    }
}
