using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Utils;
using PeachPDF;
using PeachPDF.Layout;
using PeachBrackets.Engine.Tests.TestSupport;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// The declarative builder's size limits, opacity, stacking, direction, transforms, page-flow, column and text-style methods:
    /// each is a thin setter over a CSS property the engine already supports, so the tests check the property it sets, that an explicit
    /// call beats a stylesheet rule, and (where layout is the point) the laid-out result.
    /// </summary>
    public class DeclarativeApiNamedElementsTests
    {
        private static Task<(CssBox root, HtmlContainerInt container)> Build(Action<IPageDescriptor> page) =>
            DeclarativeHarness.BuildAndLayoutAsync(page, null, new PdfGenerateConfig
            {
                PageSize = PageSize.A4, MarginTop = 20, MarginBottom = 20, MarginLeft = 20, MarginRight = 20
            });

        private static string? Style(IContainer container, string name) =>
            CssUtils.GetPropertyValue(((ContainerBuilder)container).Node.Box, name);

        private static CssBox Box(IContainer container) => ((ContainerBuilder)container).Node.Box;

        // ─── min / max size ─────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task MaxWidth_CapsAWiderWidth_AndMinWidth_RaisesANarrowerOne()
        {
            CssBox? capped = null, raised = null;
            await Build(page => page.Content(c => c.Column(column =>
            {
                capped = Box(column.Item().Id("capped").Width(200).MaxWidth(80).Height(10));
                raised = Box(column.Item().Id("raised").Width(50).MinWidth(150).Height(10));
            })));

            Assert.Equal(80, capped!.ActualBoxSizingWidth, 0.5);
            Assert.Equal(150, raised!.ActualBoxSizingWidth, 0.5);
        }

        [Fact]
        public async Task MinHeight_RaisesAndMaxHeight_CapsTheHeight()
        {
            CssBox? tall = null, short1 = null;
            await Build(page => page.Content(c => c.Column(column =>
            {
                tall = Box(column.Item().Height(10).MinHeight(40));
                short1 = Box(column.Item().Height(100).MaxHeight(30));
            })));

            Assert.Equal(40, tall!.ActualBoxSizingHeight, 0.5);
            Assert.Equal(30, short1!.ActualBoxSizingHeight, 0.5);
        }

        [Fact]
        public async Task MinAndMaxSizes_AcceptRelativeLengths()
        {
            CssBox? box = null;
            await Build(page => page.Content(c => c.Column(column =>
                box = Box(column.Item().MaxWidth(PdfLength.Percent(50)).Height(10)))));

            // The page content is 595.28 - 40 = 555.28pt wide.
            Assert.Equal(555.28 / 2, box!.ActualBoxSizingWidth, 0.5);
        }

        [Fact]
        public async Task MaxWidth_BeatsAStylesheetRule()
        {
            var generator = new PdfGenerator();
            var stylesheet = await generator.ParseStyleSheet(".c { max-width: 300pt; }");
            CssBox? box = null;
            await DeclarativeHarness.BuildAndLayoutAsync(page => page.Content(c =>
                box = Box(c.Class("c").MaxWidth(90).Height(10))), stylesheet, null);

            Assert.Equal(90, box!.ActualBoxSizingWidth, 0.5);
        }

        // ─── opacity, z-index, direction ────────────────────────────────────────────────────────

        [Theory]
        [InlineData(0.25, 0.25)]
        [InlineData(2, 1)]
        [InlineData(-1, 0)]
        [InlineData(double.NaN, 1)]
        public async Task Opacity_IsClampedToTheUnitRange(double given, double expected)
        {
            CssBox? box = null;
            await Build(page => page.Content(c => box = Box(c.Opacity(given).Height(10))));

            Assert.Equal(expected, box!.ActualOpacity, 3);
        }

        [Fact]
        public async Task ZIndex_PositionsTheBoxRelativeAndSetsTheIndex()
        {
            IContainer? container = null;
            await Build(page => page.Content(c => container = c.ZIndex(5).Height(10)));

            Assert.Equal("relative", Style(container!, "position"));
            Assert.Equal("5", Style(container!, "z-index"));
        }

        [Fact]
        public async Task Direction_ReachesTheBox_ForBothContentFromNames()
        {
            IContainer? rtl = null, ltr = null, direct = null;
            await Build(page => page.Content(c => c.Column(column =>
            {
                rtl = column.Item().ContentFromRightToLeft();
                ltr = column.Item().ContentFromLeftToRight();
                direct = column.Item().Direction(PdfTextDirection.Rtl);
            })));

            Assert.Equal("rtl", Style(rtl!, "direction"));
            Assert.Equal("ltr", Style(ltr!, "direction"));
            Assert.Equal("rtl", Style(direct!, "direction"));
        }

        [Fact]
        public async Task PageDirection_LeftToRight_ReachesThePageRoot()
        {
            var (root, _) = await Build(page => page.ContentFromLeftToRight().Content(c => c.Text("x")));

            Assert.Equal("ltr", CssUtils.GetPropertyValue(root, "direction"));
        }

        [Fact]
        public async Task PageDirection_ReachesThePageRoot()
        {
            var (root, _) = await Build(page => page.ContentFromRightToLeft().Content(c => c.Text("x")));

            Assert.Equal("rtl", CssUtils.GetPropertyValue(root, "direction"));
        }

        // ─── transforms ─────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Transforms_ComposeInTheOrderTheyAreCalled()
        {
            IContainer? container = null;
            await Build(page => page.Content(c => container = c.Rotate(30).TranslateX(10).FlipHorizontal().Height(10)));

            var transform = Style(container!, "transform");
            Assert.NotNull(transform);
            var rotate = transform!.IndexOf("rotate", StringComparison.Ordinal);
            var translate = transform.IndexOf("translate", StringComparison.Ordinal);
            var scale = transform.IndexOf("scale", StringComparison.Ordinal);
            Assert.InRange(rotate, 0, transform.Length);
            Assert.True(rotate < translate && translate < scale, transform);
        }

        [Fact]
        public async Task Transforms_MoveThePaintAndNotTheLayout()
        {
            CssBox? first = null, below = null;
            await Build(page => page.Content(c => c.Column(column =>
            {
                first = Box(column.Item().Height(50).Rotate(30).TranslateX(20).TranslateY(10)
                    .FlipHorizontal().FlipVertical().FlipOver().TransformScale(0.5).TransformScaleX(2).TransformScaleY(2));
                below = Box(column.Item().Height(20));
            })));

            Assert.Equal(50, first!.ActualBoxSizingHeight, 0.5);
            Assert.Equal(first.Location.Y + 50, below!.Location.Y, 0.5);
        }

        [Fact]
        public async Task Transforms_ActuallyApplyToTheBox()
        {
            CssBox? rotated = null, flipped = null, translated = null, scaled = null;
            await Build(page => page.Content(c => c.Column(column =>
            {
                rotated = Box(column.Item().Width(100).Height(50).Rotate(15));
                flipped = Box(column.Item().Width(100).Height(50).FlipHorizontal());
                translated = Box(column.Item().Width(100).Height(50).TranslateX(30).TranslateY(10));
                scaled = Box(column.Item().Width(100).Height(50).TransformScale(0.5));
            })));

            // `transform` only applies to a box that is not a non-replaced inline: the builder's boxes would otherwise be inline.
            Assert.True(rotated!.HasTransform && rotated.IsTransformed);
            Assert.True(flipped!.HasTransform && flipped.IsTransformed);
            Assert.True(translated!.HasTransform && translated.IsTransformed);
            Assert.True(scaled!.HasTransform && scaled.IsTransformed);
        }

        [Fact]
        public async Task Rotate_UsesDegreesAndTheInvariantCulture()
        {
            IContainer? container = null;
            await Build(page => page.Content(c => container = c.Rotate(12.5).Height(10)));

            Assert.Contains("12.5deg", Style(container!, "transform"));
        }

        // ─── page flow, columns ─────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task PreventPageBreakAndShowEntire_SetBreakInsideAvoid(bool showEntire)
        {
            IContainer? container = null;
            await Build(page => page.Content(c => container = showEntire ? c.ShowEntire().Height(10) : c.PreventPageBreak().Height(10)));

            Assert.Equal("avoid", Style(container!, "break-inside"));
        }

        [Fact]
        public async Task PreventPageBreak_MovesAWholeBlockToTheNextPage()
        {
            CssBox? block = null;
            // 800pt of filler leaves about 22pt of the 802pt content area; the 100pt block does not fit and would otherwise split.
            await Build(page => page.Content(c => c.Column(column =>
            {
                column.Item().Height(780);
                var item = column.Item().PreventPageBreak();
                block = Box(item);
                item.Column(inner =>
                {
                    for (var i = 0; i < 4; i++) inner.Item().Height(25);
                });
            })));

            // Document space: the second page's content begins at 822 (20 + the 802pt page area), where the block now starts.
            Assert.True(block!.Location.Y >= 820, $"the block should have started the second page but is at {block.Location.Y}");
        }

        [Fact]
        public async Task PageBreak_StartsTheNextItemOnANewPage()
        {
            CssBox? before = null, after = null;
            await Build(page => page.Content(c => c.Column(column =>
            {
                before = Box(column.Item().Height(50));
                column.Item().PageBreak();
                after = Box(column.Item().Height(50));
            })));

            Assert.True(after!.Location.Y >= 820, $"expected a new page: {before!.Location.Y} then {after.Location.Y}");
        }

        [Fact]
        public async Task Columns_SetTheColumnProperties()
        {
            IContainer? container = null;
            await Build(page => page.Content(c => container = c.Columns(3).ColumnGap(12).ColumnRule(1, PdfColor.Red).ColumnFill(false)));

            Assert.Equal("3", Style(container!, "column-count"));
            Assert.Equal("auto", Style(container!, "column-fill"));
            Assert.Equal("solid", Style(container!, "column-rule-style"));
            Assert.Equal(1, Box(container!).ActualColumnRuleWidth, 0.1);
        }

        [Fact]
        public void Columns_RejectsAnEmptyCount() =>
            Assert.Throws<ArgumentOutOfRangeException>(() => DeclarativeHarness
                .BuildAndLayoutAsync(page => page.Content(c => c.Columns(0))).GetAwaiter().GetResult());

        [Fact]
        public async Task MultiColumn_FlowsContentAcrossTheColumns()
        {
            var (root, _) = await Build(page => page.Content(c => c.MultiColumn(m =>
            {
                m.Columns(2).Spacing(20).BalanceHeight();
                m.Content().Id("flow").Column(column =>
                {
                    for (var i = 0; i < 4; i++) column.Item().Height(40);
                });
            })));

            // Balanced over two columns the four 40pt items make a container 80pt tall; in one column it would be 160pt.
            Assert.Equal(80, LayoutHarness.FindById(root, "flow")!.ActualBoxSizingHeight, 1);
        }

        [Fact]
        public async Task MultiColumn_ColumnSpacing_SeparatesTheStackedItems_WhetherSetBeforeOrAfterTheItems()
        {
            var (root, _) = await Build(page => page.Content(c => c.MultiColumn(m =>
            {
                m.Columns(1);
                m.Content().Column(column =>
                {
                    column.Item().Id("a").Height(10);
                    column.Item().Id("b").Height(10);
                    column.Spacing(7);
                    column.Item().Id("c").Height(10);
                });
            })));

            var a = LayoutHarness.FindById(root, "a")!;
            var b = LayoutHarness.FindById(root, "b")!;
            var c = LayoutHarness.FindById(root, "c")!;
            Assert.Equal(17, b.Location.Y - a.Location.Y, 0.5);
            Assert.Equal(17, c.Location.Y - b.Location.Y, 0.5);
        }

        [Fact]
        public async Task MultiColumn_IsTerminal()
        {
            await Build(page => page.Content(c =>
            {
                c.MultiColumn(m => m.Content().Text("x"));
                Assert.Throws<InvalidOperationException>(() => c.PageBreak());
            }));
        }

        // ─── text ───────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task WeightNames_SetTheNumericWeights()
        {
            var expected = new (Func<ITextStyle, ITextStyle> Apply, string Weight)[]
            {
                (s => s.Thin(), "100"), (s => s.ExtraLight(), "200"), (s => s.Light(), "300"), (s => s.NormalWeight(), "400"),
                (s => s.Medium(), "500"), (s => s.SemiBold(), "600"), (s => s.Bold(), "700"), (s => s.ExtraBold(), "800"), (s => s.Black(), "900")
            };

            foreach (var (apply, weight) in expected)
            {
                IContainer? container = null;
                await Build(page => page.Content(c => container = c.DefaultTextStyle(s => apply(s))));
                Assert.Equal(weight, Style(container!, "font-weight"));
            }
        }

        [Fact]
        public async Task WrapAnywhere_SetsOverflowWrap()
        {
            IContainer? container = null;
            await Build(page => page.Content(c => container = c.DefaultTextStyle(s => s.WrapAnywhere())));

            Assert.Equal("anywhere", Style(container!, "overflow-wrap"));
        }

        [Fact]
        public async Task DecorationNames_SetTheDecorationStyle()
        {
            var cases = new (Func<ITextSpan, ITextSpan> Apply, string Style)[]
            {
                (s => s.DecorationSolid(), "solid"), (s => s.DecorationDouble(), "double"), (s => s.DecorationDotted(), "dotted"),
                (s => s.DecorationDashed(), "dashed"), (s => s.DecorationWavy(), "wavy")
            };

            foreach (var (apply, expected) in cases)
            {
                ITextSpan? span = null;
                await Build(page => page.Content(c => c.Text(t =>
                {
                    var run = t.Span("x");
                    run.Underline();
                    span = apply(run);
                })));
                Assert.Equal(expected, CssUtils.GetPropertyValue(((TextStyleApplier)span!).Node.Box, "text-decoration-style"));
            }
        }

        [Fact]
        public async Task AlignmentNames_SetTextAlign()
        {
            var cases = new (Func<ITextSpanContainer, ITextSpanContainer> Apply, PeachBrackets.Css.HorizontalAlignment Align)[]
            {
                (t => t.AlignLeft(), PeachBrackets.Css.HorizontalAlignment.Left), (t => t.AlignCenter(), PeachBrackets.Css.HorizontalAlignment.Center),
                (t => t.AlignRight(), PeachBrackets.Css.HorizontalAlignment.Right), (t => t.Justify(), PeachBrackets.Css.HorizontalAlignment.Justify),
                (t => t.AlignStart(), PeachBrackets.Css.HorizontalAlignment.Start), (t => t.AlignEnd(), PeachBrackets.Css.HorizontalAlignment.End)
            };

            foreach (var (apply, expected) in cases)
            {
                CssBox? paragraph = null;
                await Build(page => page.Content(c =>
                {
                    c.Text(t => apply(t).Span("x"));
                    paragraph = Box(c);
                }));
                Assert.Equal(expected, paragraph!.ActualTextAlignAll);
            }
        }
    }
}
