using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Html.Core.Utils
{
    public class CssUtilsTests
    {
        [Fact]
        public async Task GetPropertyValue_KnownNames_ReturnCorrespondingBoxProperty()
        {
            var box = await FindDivBox(
                "color: rgb(1, 2, 3); display: block; position: relative; overflow: hidden; " +
                "text-align: center; font-weight: bold; z-index: 5;");

            Assert.Equal(box.Color, CssUtils.GetPropertyValue(box, "color"));
            Assert.Equal(box.Display, CssUtils.GetPropertyValue(box, "display"));
            Assert.Equal(box.Position, CssUtils.GetPropertyValue(box, "position"));
            Assert.Equal(box.Overflow, CssUtils.GetPropertyValue(box, "overflow"));
            Assert.Equal(box.TextAlign, CssUtils.GetPropertyValue(box, "text-align"));
            Assert.Equal(box.FontWeight, CssUtils.GetPropertyValue(box, "font-weight"));
            Assert.Equal(box.ZIndex, CssUtils.GetPropertyValue(box, "z-index"));
        }

        [Fact]
        public async Task GetPropertyValue_PdfTagType_ReturnsBoxPdfTagType()
        {
            var box = await FindDivBox("-peachpdf-pdf-tag-type: BlockQuote;");

            Assert.Equal(box.PdfTagType, CssUtils.GetPropertyValue(box, "-peachpdf-pdf-tag-type"));
        }

        [Fact]
        public async Task GetPropertyValue_BackgroundAndListStyleImage_AlwaysReturnsNull()
        {
            var box = await FindDivBox("background-image: none; list-style-image: none;");

            Assert.Null(CssUtils.GetPropertyValue(box, "background-image"));
            Assert.Null(CssUtils.GetPropertyValue(box, "list-style-image"));
        }

        [Fact]
        public async Task GetPropertyValue_UnknownName_ReturnsNull()
        {
            var box = await FindDivBox("");

            Assert.Null(CssUtils.GetPropertyValue(box, "not-a-real-property"));
        }

        [Fact]
        public async Task SetPropertyValue_BorderShorthand_SetsWidthStyleAndColor()
        {
            var (box, parser) = await FindDivBoxAndParser("");

            CssUtils.SetPropertyValue(parser, box, "border", "2px solid rgb(1, 2, 3)");

            Assert.Equal("2px", box.BorderTopWidth);
            Assert.Equal("solid", box.BorderTopStyle);
            Assert.Equal(box.BorderTopColor, box.BorderBottomColor);
        }

        [Fact]
        public async Task SetPropertyValue_BorderWidthShorthand_SplitsFourDirections()
        {
            var (box, parser) = await FindDivBoxAndParser("");

            CssUtils.SetPropertyValue(parser, box, "border-width", "1px 2px 3px 4px");

            Assert.Equal("1px", box.BorderTopWidth);
            Assert.Equal("2px", box.BorderRightWidth);
            Assert.Equal("3px", box.BorderBottomWidth);
            Assert.Equal("4px", box.BorderLeftWidth);
        }

        [Fact]
        public async Task SetPropertyValue_MarginShorthand_SplitsFourDirections()
        {
            var (box, parser) = await FindDivBoxAndParser("");

            CssUtils.SetPropertyValue(parser, box, "margin", "1px 2px 3px 4px");

            Assert.Equal("1px", box.MarginTop);
            Assert.Equal("2px", box.MarginRight);
            Assert.Equal("3px", box.MarginBottom);
            Assert.Equal("4px", box.MarginLeft);
        }

        [Fact]
        public async Task SetPropertyValue_PaddingShorthand_TwoValues_SetsOpposingSides()
        {
            var (box, parser) = await FindDivBoxAndParser("");

            CssUtils.SetPropertyValue(parser, box, "padding", "5px 10px");

            Assert.Equal("5px", box.PaddingTop);
            Assert.Equal("10px", box.PaddingRight);
            Assert.Equal("5px", box.PaddingBottom);
            Assert.Equal("10px", box.PaddingRight);
        }

        [Fact]
        public async Task SetPropertyValue_InvalidWidth_IsIgnored()
        {
            var (box, parser) = await FindDivBoxAndParser("width: 50px;");

            CssUtils.SetPropertyValue(parser, box, "width", "not-a-length");

            Assert.Equal("50px", box.Width);
        }

        [Fact]
        public async Task SetPropertyValue_ListStyleShorthand_SetsTypeAndPosition()
        {
            var (box, parser) = await FindDivBoxAndParser("");

            CssUtils.SetPropertyValue(parser, box, "list-style", "square inside");

            Assert.Equal("square", box.ListStyleType);
            Assert.Equal("inside", box.ListStylePosition);
        }

        [Fact]
        public async Task SetPropertyValue_FlexShorthand_None_SetsFixedValues()
        {
            var (box, parser) = await FindDivBoxAndParser("");

            CssUtils.SetPropertyValue(parser, box, "flex", "none");

            Assert.Equal("0", box.FlexGrow);
            Assert.Equal("0", box.FlexShrink);
            Assert.Equal("auto", box.FlexBasis);
        }

        [Fact]
        public async Task SetPropertyValue_FlexShorthand_SingleNumber_SetsGrowAndDefaults()
        {
            var (box, parser) = await FindDivBoxAndParser("");

            CssUtils.SetPropertyValue(parser, box, "flex", "2");

            Assert.Equal("2", box.FlexGrow);
            Assert.Equal("1", box.FlexShrink);
            Assert.Equal("0", box.FlexBasis);
        }

        [Fact]
        public async Task SetPropertyValue_FlexFlowShorthand_SetsDirectionAndWrap()
        {
            var (box, parser) = await FindDivBoxAndParser("");

            CssUtils.SetPropertyValue(parser, box, "flex-flow", "column wrap");

            Assert.Equal("column", box.FlexDirection);
            Assert.Equal("wrap", box.FlexWrap);
        }

        [Fact]
        public async Task SetPropertyValue_GapShorthand_SingleValue_AppliesToBoth()
        {
            var (box, parser) = await FindDivBoxAndParser("");

            CssUtils.SetPropertyValue(parser, box, "gap", "10px");

            Assert.Equal("10px", box.FlexRowGap);
            Assert.Equal("10px", box.FlexColumnGap);
        }

        [Fact]
        public async Task SetPropertyValue_GapShorthand_TwoValues_SetsRowAndColumn()
        {
            var (box, parser) = await FindDivBoxAndParser("");

            CssUtils.SetPropertyValue(parser, box, "gap", "10px 20px");

            Assert.Equal("10px", box.FlexRowGap);
            Assert.Equal("20px", box.FlexColumnGap);
        }

        [Fact]
        public async Task SetPropertyValue_ColumnsShorthand_CountAndWidth_SetsBothLonghands()
        {
            var (box, parser) = await FindDivBoxAndParser("");

            CssUtils.SetPropertyValue(parser, box, "columns", "2 100px");

            Assert.Equal("2", box.ColumnCount);
            Assert.Equal("100px", box.ColumnWidth);
        }

        [Fact]
        public async Task SetPropertyValue_ColumnsShorthand_Auto_LeavesLonghandsAtDefault()
        {
            var (box, parser) = await FindDivBoxAndParser("");

            CssUtils.SetPropertyValue(parser, box, "columns", "auto");

            Assert.Equal("auto", box.ColumnCount);
            Assert.Equal("auto", box.ColumnWidth);
        }

        [Fact]
        public async Task SetPropertyValue_ColumnFill_AcceptsAutoAndBalance()
        {
            var (box, parser) = await FindDivBoxAndParser("");

            CssUtils.SetPropertyValue(parser, box, "column-fill", "balance");
            Assert.Equal("balance", box.ColumnFill);

            CssUtils.SetPropertyValue(parser, box, "column-fill", "auto");
            Assert.Equal("auto", box.ColumnFill);
        }

        [Fact]
        public async Task SetPropertyValue_ColumnSpan_AcceptsNoneAndAll()
        {
            var (box, parser) = await FindDivBoxAndParser("");

            CssUtils.SetPropertyValue(parser, box, "column-span", "all");
            Assert.Equal("all", box.ColumnSpan);

            CssUtils.SetPropertyValue(parser, box, "column-span", "none");
            Assert.Equal("none", box.ColumnSpan);
        }

        [Fact]
        public async Task SetPropertyValue_ColumnRuleShorthand_SetsWidthStyleAndColor()
        {
            var (box, parser) = await FindDivBoxAndParser("");

            CssUtils.SetPropertyValue(parser, box, "column-rule", "2px solid black");

            Assert.Equal("2px", box.ColumnRuleWidth);
            Assert.Equal("solid", box.ColumnRuleStyle);
            Assert.Equal("black", box.ColumnRuleColor);
        }

        [Fact]
        public async Task SetPropertyValue_ZIndex_AutoOrInteger_IsAccepted()
        {
            var (box, parser) = await FindDivBoxAndParser("");

            CssUtils.SetPropertyValue(parser, box, "z-index", "auto");
            Assert.Equal("auto", box.ZIndex);

            CssUtils.SetPropertyValue(parser, box, "z-index", "5");
            Assert.Equal("5", box.ZIndex);
        }

        [Fact]
        public async Task SetPropertyValue_ZIndex_InvalidValue_IsIgnored()
        {
            var (box, parser) = await FindDivBoxAndParser("z-index: 3;");

            CssUtils.SetPropertyValue(parser, box, "z-index", "not-a-number");

            Assert.Equal("3", box.ZIndex);
        }

        [Fact]
        public async Task SetPropertyValue_BoxSizing_InvalidValue_IsIgnored()
        {
            var (box, parser) = await FindDivBoxAndParser("box-sizing: border-box;");

            CssUtils.SetPropertyValue(parser, box, "box-sizing", "not-a-real-value");

            Assert.Equal("border-box", box.BoxSizing);
        }

        [Theory]
        [InlineData("border-box", true)]
        [InlineData("content-box", true)]
        [InlineData("padding-box", false)]
        public void IsValidBoxSizing_ChecksKnownValues(string value, bool expected)
        {
            Assert.Equal(expected, CssUtils.IsValidBoxSizing(value));
        }

        [Fact]
        public async Task NumericFontWeight_700OrAbove_ResolvesToBoldFont()
        {
            var box = await FindDivBox("font-weight: 700;");

            var font = (PeachPDF.Adapters.FontAdapter)box.ActualFont;
            Assert.True(font.Font.Bold);
        }

        [Fact]
        public async Task NumericFontWeight_Below700_DoesNotResolveToBoldFont()
        {
            var box = await FindDivBox("font-weight: 400;");

            var font = (PeachPDF.Adapters.FontAdapter)box.ActualFont;
            Assert.False(font.Font.Bold);
        }

        [Fact]
        public async Task ApplyCurrentColor_ReplacesCurrentColorWithColorValue()
        {
            var (box, parser) = await FindDivBoxAndParser("color: rgb(10, 20, 30); border-top-color: currentColor;");

            CssUtils.ApplyCurrentColor(box, parser);

            Assert.Equal("rgb(10, 20, 30)", box.BorderTopColor);
        }

        [Fact]
        public async Task WhiteSpace_ReturnsPositiveWidth()
        {
            var (box, _) = await FindDivBoxAndParser("font-size: 16px;");
            var adapter = new PdfSharpAdapter();
            var measure = XGraphics.CreateMeasureContext(new XSize(595, 842), XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new PeachPDF.Adapters.GraphicsAdapter(adapter, measure, 1.0);

            var width = CssUtils.WhiteSpace(graphics, box);

            Assert.True(width > 0);
        }

        // --- Helpers ---

        private static Task<CssBox> FindDivBox(string css) => FindDivBoxFromHtml(BuildHtml(css));

        private static async Task<(CssBox Box, CssValueParser Parser)> FindDivBoxAndParser(string css)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(BuildHtml(css), null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new PeachPDF.Adapters.GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            var box = FindByTag(container.Root!, "div")!;
            return (box, new CssValueParser(adapter));
        }

        private static string BuildHtml(string css) =>
            $@"<!DOCTYPE html><html><head><style>
div {{ width: 200px; height: 100px; {css} }}
</style></head><body><div>Text</div></body></html>";

        private static async Task<CssBox> FindDivBoxFromHtml(string html)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new PeachPDF.Adapters.GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return FindByTag(container.Root!, "div")!;
        }

        private static CssBox? FindByTag(CssBox box, string tag)
        {
            if (box.HtmlTag?.Name.Equals(tag, StringComparison.OrdinalIgnoreCase) == true)
                return box;
            foreach (var child in box.Boxes)
            {
                var found = FindByTag(child, tag);
                if (found != null) return found;
            }
            return null;
        }
    }
}
