using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Collections.Generic;
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
        public async Task SetPropertyValue_InvalidWidth_IsIgnored()
        {
            var (box, parser) = await FindDivBoxAndParser("width: 50px;");

            CssUtils.SetPropertyValue(parser, box, "width", "not-a-length");

            Assert.Equal("50px", box.Width);
        }
        [Fact]
        public async Task SetPropertyValue_GridTemplateColumns_ParsesTrackListIntoTypedValue()
        {
            var (box, parser) = await FindDivBoxAndParser("");

            CssUtils.SetPropertyValue(parser, box, "grid-template-columns", "100pt 200pt");

            Assert.False(box.GridTemplateColumns.IsGlobalValue);
            Assert.False(box.GridTemplateColumns.IsUnresolved);
            Assert.NotNull(box.GridTemplateColumns.Value);
            Assert.Equal(2, box.GridTemplateColumns.Value!.Tracks.Count);
            Assert.Equal("100pt 200pt", box.GridTemplateColumns.ToString());
        }

        [Fact]
        public async Task SetPropertyValue_GridTemplateColumns_GlobalKeyword_BecomesGlobalValue()
        {
            // The string setter's global-keyword branch (a CSS-wide keyword reaching the setter directly).
            var (box, parser) = await FindDivBoxAndParser("");

            CssUtils.SetPropertyValue(parser, box, "grid-template-rows", "inherit");

            Assert.True(box.GridTemplateRows.IsGlobalValue);
            Assert.Equal(CssGlobalKeyword.Inherit, box.GridTemplateRows.GlobalValue);
            Assert.Null(box.GridTemplateRows.Value);
        }

        [Fact]
        public async Task SetPropertyValue_GridTemplateColumns_VarValue_StaysUnresolved()
        {
            // The string setter's var() guard: a value still containing var() must NOT be parsed into a
            // template — it stays unresolved (no Value) until resolution replaces it.
            var (box, parser) = await FindDivBoxAndParser("");

            CssUtils.SetPropertyValue(parser, box, "grid-template-columns", "var(--cols)");

            Assert.True(box.GridTemplateColumns.IsUnresolved);
            Assert.False(box.GridTemplateColumns.IsGlobalValue);
            Assert.Null(box.GridTemplateColumns.Value);
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

        // Every property whose setter stores its value verbatim and whose getter returns it unchanged — the
        // round trip pins the full data-driven dispatch (both _propertySetters and _propertyGetters) in one
        // place, so an entry that maps to the wrong box field, or a stale getter/setter after the switch→table
        // refactor, fails loudly. Each row runs against a fresh box, so properties never interact. Excludes
        // properties with a normalizing/parsing setter or getter (font-family, font-size, the *-image parsers)
        // and the shorthands, which have their own dedicated tests above. The em-stripping NoEms properties
        // (text-indent/word-spacing/letter-spacing) are included but with px values, which pass through unchanged.
        public static IEnumerable<object[]> DirectRoundTripProperties() => new object[][]
        {
            ["border-bottom-width", "2px"], ["border-left-width", "3px"], ["border-right-width", "4px"], ["border-top-width", "5px"],
            ["border-bottom-style", "dashed"], ["border-left-style", "dotted"], ["border-right-style", "double"], ["border-top-style", "groove"],
            ["border-bottom-color", "rgb(1, 2, 3)"], ["border-left-color", "rgb(4, 5, 6)"], ["border-right-color", "rgb(7, 8, 9)"], ["border-top-color", "rgb(10, 11, 12)"],
            ["border-spacing", "3px"], ["border-collapse", "collapse"], ["box-sizing", "border-box"],
            ["border-top-left-radius", "1px"], ["border-top-right-radius", "2px"], ["border-bottom-right-radius", "3px"], ["border-bottom-left-radius", "4px"],
            ["transform", "rotate(10deg)"], ["transform-origin", "left top"], ["opacity", "0.5"],
            ["counter-increment", "c 1"], ["counter-reset", "c 0"], ["counter-set", "c 2"], ["string-set", "none"],
            ["page", "cover"], ["-peachpdf-pdf-tag-type", "BlockQuote"],
            ["margin-bottom", "5px"], ["margin-left", "6px"], ["margin-right", "7px"], ["margin-top", "8px"],
            ["padding-bottom", "5px"], ["padding-left", "6px"], ["padding-right", "7px"], ["padding-top", "8px"],
            ["break-after", "avoid"], ["break-before", "avoid"], ["break-inside", "avoid"],
            ["left", "10px"], ["top", "11px"], ["right", "12px"], ["bottom", "13px"],
            ["width", "100px"], ["max-width", "200px"], ["min-width", "50px"], ["height", "120px"], ["max-height", "240px"], ["min-height", "60px"],
            ["background-color", "rgb(4, 5, 6)"], ["background-position", "center"], ["background-size", "cover"], ["background-repeat", "no-repeat"],
            ["background-origin", "border-box"], ["background-clip", "padding-box"], ["background-attachment", "fixed"],
            ["color", "rgb(7, 8, 9)"], ["content", "normal"], ["display", "block"], ["direction", "rtl"], ["empty-cells", "hide"],
            ["clear", "both"], ["position", "absolute"], ["line-height", "1.5"], ["vertical-align", "middle"], ["text-indent", "20px"],
            ["text-align", "center"], ["text-decoration-color", "rgb(1, 2, 3)"], ["text-decoration-line", "underline"], ["text-decoration-style", "solid"],
            ["text-transform", "uppercase"], ["white-space", "nowrap"], ["word-break", "break-all"], ["visibility", "hidden"], ["word-spacing", "2px"], ["letter-spacing", "1px"],
            ["font-style", "italic"], ["font-variant", "small-caps"], ["font-weight", "bold"], ["font-stretch", "condensed"],
            ["list-style-position", "inside"], ["list-style-type", "square"], ["overflow", "hidden"], ["z-index", "5"],
            ["flex-direction", "column"], ["flex-wrap", "wrap"], ["justify-content", "center"], ["align-items", "stretch"], ["align-content", "center"],
            ["flex-grow", "2"], ["flex-shrink", "0"], ["flex-basis", "auto"], ["align-self", "center"], ["order", "3"],
            ["row-gap", "5px"], ["column-gap", "8px"], ["column-count", "3"], ["column-width", "120px"], ["column-fill", "auto"], ["column-span", "all"],
            ["column-rule-width", "2px"], ["column-rule-style", "solid"], ["column-rule-color", "rgb(1, 2, 3)"],
            ["float", "left"],
        };

        [Theory]
        [MemberData(nameof(DirectRoundTripProperties))]
        public async Task SetThenGetPropertyValue_RoundTripsVerbatim(string name, string value)
        {
            var (box, parser) = await FindDivBoxAndParser("");

            CssUtils.SetPropertyValue(parser, box, name, value);

            Assert.Equal(value, CssUtils.GetPropertyValue(box, name));
        }

        // Every remaining setter name: the value-validated/-transformed ones, the *-image parsers, and the
        // accepted-but-inert properties. This drives the dispatch arms the round trip can't (they don't store
        // verbatim) and asserts they neither throw nor leave the property engine in a bad state. Shorthand
        // names are deliberately absent — Layer B (CssUtils) no longer knows any shorthand; see
        // SetPropertyValue_ShorthandName_IsIgnoredByLayerB and the Layer-A expansion end-to-end tests.
        [Theory]
        [InlineData("page-break-after", "always")]
        [InlineData("page-break-before", "always")]
        [InlineData("page-break-inside", "avoid")]
        [InlineData("orphans", "3")]
        [InlineData("widows", "2")]
        [InlineData("hyphens", "auto")]
        [InlineData("background-image", "none")]
        [InlineData("font-family", "Arial")]
        [InlineData("font-size", "16px")]
        [InlineData("list-style-image", "none")]
        [InlineData("unicode-bidi", "isolate")]
        [InlineData("overflow-wrap", "break-word")]
        [InlineData("not-a-real-property", "whatever")]
        public async Task SetPropertyValue_HandledOrUnknownName_DoesNotThrow(string name, string value)
        {
            var (box, parser) = await FindDivBoxAndParser("");

            var ex = Record.Exception(() => CssUtils.SetPropertyValue(parser, box, name, value));

            Assert.Null(ex);
            // A follow-up read must also be well-behaved (never throws), whether or not the name has a getter.
            Assert.Null(Record.Exception(() => CssUtils.GetPropertyValue(box, name)));
        }

        // Layer B (CssUtils) is longhand-only: CSS-OM (Layer A) expands every shorthand before Layer B ever
        // sees a property name, so handing Layer B a shorthand directly must be an inert no-op — it must NOT
        // expand into the longhands. Here we set a longhand, then fire the shorthand that would (if Layer B
        // still knew it) overwrite that longhand, and assert the longhand is untouched.
        [Theory]
        [InlineData("margin", "99px", "margin-top")]
        [InlineData("padding", "99px", "padding-top")]
        [InlineData("border-width", "99px", "border-top-width")]
        [InlineData("border", "99px solid red", "border-top-width")]
        [InlineData("border-top", "99px solid red", "border-top-width")]
        [InlineData("border-style", "dashed", "border-top-style")]
        [InlineData("border-color", "rgb(9, 9, 9)", "border-top-color")]
        [InlineData("flex", "9 9 90px", "flex-grow")]
        [InlineData("flex-flow", "column wrap", "flex-direction")]
        [InlineData("columns", "9 90px", "column-count")]
        [InlineData("column-rule", "9px solid red", "column-rule-width")]
        [InlineData("gap", "90px", "row-gap")]
        [InlineData("list-style", "square inside", "list-style-type")]
        [InlineData("border-radius", "90px", "border-top-left-radius")]
        public async Task SetPropertyValue_ShorthandName_IsIgnoredByLayerB(string shorthand, string shorthandValue, string longhand)
        {
            var (box, parser) = await FindDivBoxAndParser("");
            // Seed the longhand with a value its own validator accepts, so a stray shorthand expansion would
            // be visible. Different longhand types demand different sentinel shapes.
            var seed = longhand switch
            {
                "border-top-style" => "dotted",
                "border-top-color" => "rgb(1, 1, 1)",
                "column-count" => "3",
                "flex-grow" => "5",
                "flex-direction" => "row-reverse",
                "list-style-type" => "square",
                _ => "7px", // the length-valued longhands
            };
            CssUtils.SetPropertyValue(parser, box, longhand, seed);

            CssUtils.SetPropertyValue(parser, box, shorthand, shorthandValue);

            // The shorthand did nothing, so the longhand still holds the seed — never the shorthand's value.
            Assert.Equal(seed, CssUtils.GetPropertyValue(box, longhand));
        }

        // End-to-end complement: shorthands DO still fully expand — through Layer A (CSS-OM), the only layer
        // that now knows shorthand grammar. Author CSS carrying a shorthand must populate every longhand box
        // field. This guards the Layer-B shorthand removal against silently breaking shorthand expansion.
        [Fact]
        public async Task Shorthand_InAuthorCss_ExpandsToLonghands_ViaLayerA()
        {
            var box = await FindDivBox("margin: 1px 2px 3px 4px; flex: 2 3 40px; border-top: 5px dashed black");

            Assert.Equal("1px", CssUtils.GetPropertyValue(box, "margin-top"));
            Assert.Equal("2px", CssUtils.GetPropertyValue(box, "margin-right"));
            Assert.Equal("3px", CssUtils.GetPropertyValue(box, "margin-bottom"));
            Assert.Equal("4px", CssUtils.GetPropertyValue(box, "margin-left"));

            Assert.Equal("2", CssUtils.GetPropertyValue(box, "flex-grow"));
            Assert.Equal("3", CssUtils.GetPropertyValue(box, "flex-shrink"));
            Assert.Equal("40px", CssUtils.GetPropertyValue(box, "flex-basis"));

            Assert.Equal("5px", CssUtils.GetPropertyValue(box, "border-top-width"));
            Assert.Equal("dashed", CssUtils.GetPropertyValue(box, "border-top-style"));
        }

        // The legacy page-break-* aliases map "always" to "page" on both the -after and -before axes; the plain
        // break-* properties do not transform (asserted by the round-trip theory, which feeds them "avoid").
        [Theory]
        [InlineData("page-break-after", "break-after")]
        [InlineData("page-break-before", "break-before")]
        public async Task SetPropertyValue_PageBreakAlways_MapsToPage(string setName, string getName)
        {
            var (box, parser) = await FindDivBoxAndParser("");

            CssUtils.SetPropertyValue(parser, box, setName, "always");

            Assert.Equal("page", CssUtils.GetPropertyValue(box, getName));
        }

        // The false side of each validation guard: an invalid value must be ignored, leaving the prior value.
        [Theory]
        [InlineData("width", "not-a-length", "40px")]
        [InlineData("border-top-width", "banana", "3px")]
        [InlineData("border-top-style", "not-a-style", "solid")]
        [InlineData("box-sizing", "weird-box", "border-box")]
        [InlineData("column-count", "0", "2")]
        [InlineData("column-span", "sometimes", "all")]
        public async Task SetPropertyValue_InvalidValue_IsRejected(string name, string invalid, string valid)
        {
            var (box, parser) = await FindDivBoxAndParser("");
            CssUtils.SetPropertyValue(parser, box, name, valid);

            CssUtils.SetPropertyValue(parser, box, name, invalid);

            Assert.Equal(valid, CssUtils.GetPropertyValue(box, name));
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
