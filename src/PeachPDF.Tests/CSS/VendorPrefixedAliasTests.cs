using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.CSS
{
    /// <summary>
    /// The legacy <c>-webkit-</c>/<c>-moz-</c> spellings of properties, keyword values and gradient functions PeachPDF
    /// supports in standard form resolve to the standard one before anything else sees them.
    /// </summary>
    public class VendorPrefixedAliasTests : CssConstructionFunctions
    {
        [Theory]
        [InlineData("-webkit-box-shadow: 1px 1px red", "box-shadow", typeof(BoxShadowProperty))]
        [InlineData("-moz-box-shadow: 1px 1px red", "box-shadow", typeof(BoxShadowProperty))]
        [InlineData("-webkit-transform: none", "transform", null)]
        [InlineData("-webkit-transform-origin: 10px 10px", "transform-origin", null)]
        [InlineData("-webkit-box-sizing: border-box", "box-sizing", typeof(BoxSizingProperty))]
        [InlineData("-moz-box-sizing: border-box", "box-sizing", typeof(BoxSizingProperty))]
        [InlineData("-webkit-filter: blur(2px)", "filter", typeof(FilterProperty))]
        [InlineData("-webkit-backdrop-filter: blur(2px)", "backdrop-filter", typeof(BackdropFilterProperty))]
        [InlineData("-webkit-border-top-left-radius: 4px", "border-top-left-radius", typeof(BorderTopLeftRadiusProperty))]
        [InlineData("-webkit-column-count: 2", "column-count", null)]
        [InlineData("-moz-column-gap: 10px", "column-gap", null)]
        [InlineData("-webkit-hyphens: auto", "hyphens", null)]
        [InlineData("-moz-tab-size: 4", "tab-size", null)]
        [InlineData("-webkit-flex-direction: column", "flex-direction", null)]
        [InlineData("-webkit-justify-content: center", "justify-content", null)]
        [InlineData("-webkit-align-items: center", "align-items", null)]
        [InlineData("-webkit-order: 2", "order", null)]
        [InlineData("-webkit-margin-start: 4px", "margin-inline-start", null)]
        [InlineData("-webkit-padding-end: 4px", "padding-inline-end", null)]
        [InlineData("-webkit-writing-mode: vertical-rl", "writing-mode", null)]
        [InlineData("-webkit-font-feature-settings: \"liga\" 0", "font-feature-settings", null)]
        [InlineData("-webkit-text-decoration-line: underline", "text-decoration-line", null)]
        public void LonghandAlias_IsCreatedAsTheStandardProperty(string declaration, string standardName, System.Type? expectedType)
        {
            var property = ParseDeclaration(declaration);

            Assert.Equal(standardName, property.Name);
            Assert.True(property.HasValue);
            if (expectedType != null) Assert.IsType(expectedType, property);
        }

        [Theory]
        [InlineData("-webkit-border-radius: 5px", "border-top-left-radius")]
        [InlineData("-webkit-flex: 1 1 auto", "flex-grow")]
        [InlineData("-webkit-flex-flow: row wrap", "flex-wrap")]
        [InlineData("-moz-columns: 2 100px", "column-count")]
        [InlineData("-webkit-text-decoration: underline", "text-decoration-line")]
        [InlineData("-webkit-border-image: url(a.png) 10 stretch", "border-image-source")]
        public void ShorthandAlias_ExpandsToTheStandardLonghands(string declaration, string expectedLonghand)
        {
            var sheet = ParseStyleSheet("div { " + declaration + " }");
            var style = ((StyleRule)sheet.Rules[0]).Style;

            Assert.NotNull(style.GetProperty(expectedLonghand));
            Assert.Null(style.GetProperty(declaration[..declaration.IndexOf(':')]));
        }

        [Fact]
        public void TheAlias_AndTheStandardSpelling_AddressTheSameSlotInADeclarationBlock()
        {
            var sheet = ParseStyleSheet("div { -webkit-box-shadow: 1px 1px red; box-shadow: 2px 2px blue }");
            var style = ((StyleRule)sheet.Rules[0]).Style;

            Assert.Single(style, p => p.Name == "box-shadow");
            Assert.Contains("blue", style.GetPropertyValue("-webkit-box-shadow"));
            Assert.Equal(style.GetPropertyValue("box-shadow"), style.GetPropertyValue("-moz-box-shadow"));
        }

        [Fact]
        public void PrefixesWithNoStandardGrammar_AreStillDropped()
        {
            var sheet = ParseStyleSheet("div { -webkit-box-orient: vertical; -webkit-box-flex: 1; color: red }");
            var style = ((StyleRule)sheet.Rules[0]).Style;

            Assert.Single(style);
        }

        [Fact]
        public void CssSheetIgnoreVendorPrefixes_KeepsTheAliasedDeclarations()
        {
            var sheet = ParseStyleSheet(@".something {
  -o-border-radius: 5px;
  -webkit-border-radius: 5px;
  border-radius: 5px;
  display: -webkit-box;
  display: -webkit-flex;
  display: -ms-flexbox;
  display: flex;
  background: -webkit-linear-gradient(red, green);
  background: linear-gradient(red, green);
}");
            var style = ((StyleRule)sheet.Rules[0]).Style;

            Assert.NotNull(style.GetProperty("border-top-left-radius"));
            Assert.Equal("flex", style.GetPropertyValue("display"));
        }

        [Theory]
        [InlineData("display: -webkit-flex", "flex")]
        [InlineData("display: -webkit-inline-flex", "inline-flex")]
        [InlineData("position: -webkit-sticky", "sticky")]
        public void PrefixedKeywordValue_IsAcceptedByTheCssOm(string declaration, string _)
        {
            Assert.True(ParseDeclaration(declaration).HasValue);
        }

        [Theory]
        [InlineData("-webkit-linear-gradient(red, blue)", "linear-gradient(red, blue)")]
        [InlineData("-moz-linear-gradient(top, red, blue)", "linear-gradient(to bottom, red, blue)")]
        [InlineData("-webkit-linear-gradient(left, red, blue)", "linear-gradient(to right, red, blue)")]
        [InlineData("-webkit-linear-gradient(bottom right, red, blue)", "linear-gradient(to left top, red, blue)")]
        [InlineData("-webkit-linear-gradient(right top, red, blue)", "linear-gradient(to left bottom, red, blue)")]
        [InlineData("-webkit-linear-gradient(45deg, red, blue)", "linear-gradient(45deg, red, blue)")]
        [InlineData("-webkit-linear-gradient(0deg, red, blue)", "linear-gradient(90deg, red, blue)")]
        [InlineData("-webkit-linear-gradient(90deg, red, blue)", "linear-gradient(0deg, red, blue)")]
        [InlineData("-webkit-linear-gradient(180deg, red, blue)", "linear-gradient(270deg, red, blue)")]
        [InlineData("-webkit-linear-gradient(-90deg, red, blue)", "linear-gradient(180deg, red, blue)")]
        [InlineData("-webkit-linear-gradient(0.25turn, red, blue)", "linear-gradient(0deg, red, blue)")]
        [InlineData("-webkit-linear-gradient(100grad, red, blue)", "linear-gradient(0deg, red, blue)")]
        [InlineData("-webkit-linear-gradient(1.5707963rad, red, blue)", "linear-gradient(0deg, red, blue)")]
        [InlineData("-webkit-linear-gradient(0, red, blue)", "linear-gradient(90deg, red, blue)")]
        [InlineData("-o-linear-gradient(left, red, blue)", "linear-gradient(to right, red, blue)")]
        [InlineData("-ms-linear-gradient(left, red, blue)", "linear-gradient(to right, red, blue)")]
        [InlineData("-webkit-radial-gradient(farthest-side, red, blue)", "radial-gradient(farthest-side, red, blue)")]
        [InlineData("-webkit-repeating-linear-gradient(left, red 0, blue 10px)", "repeating-linear-gradient(to right, red 0, blue 10px)")]
        [InlineData("-webkit-linear-gradient(to right, red, blue)", "linear-gradient(to right, red, blue)")]
        [InlineData("-webkit-radial-gradient(red, blue)", "radial-gradient(red, blue)")]
        [InlineData("-webkit-radial-gradient(center, circle cover, red, blue)", "radial-gradient(circle farthest-corner at center, red, blue)")]
        [InlineData("-webkit-radial-gradient(top left, ellipse contain, red, blue)", "radial-gradient(ellipse closest-side at top left, red, blue)")]
        [InlineData("-moz-radial-gradient(30% 40%, red, blue)", "radial-gradient(at 30% 40%, red, blue)")]
        [InlineData("-webkit-radial-gradient(circle closest-corner, red, blue)", "radial-gradient(circle closest-corner, red, blue)")]
        [InlineData("-webkit-repeating-radial-gradient(center, circle, red 0, blue 10px)", "repeating-radial-gradient(circle at center, red 0, blue 10px)")]
        public void LegacyGradientFunction_IsRewrittenToStandardSyntax(string legacy, string expected)
        {
            var property = ParseDeclaration("background-image: " + legacy);

            Assert.True(property.HasValue);
            // Named colors are normalized to rgb() at parse time.
            Assert.Equal(expected.Replace("red", "rgb(255, 0, 0)").Replace("blue", "rgb(0, 0, 255)"), property.Value);
        }

        [Theory]
        [InlineData("-webkit-linear-gradient(top top, red, blue)")]
        [InlineData("-webkit-linear-gradient(left right, red, blue)")]
        [InlineData("-webkit-linear-gradient()")]
        [InlineData("-webkit-gradient(linear, left top, left bottom, from(red), to(blue))")]
        [InlineData("-webkit-conic-gradient(red, blue)")]
        [InlineData("-khtml-linear-gradient(left, red, blue)")]
        public void UnrewritableLegacyGradient_IsLeftAsWrittenAndRejectedByTheConverter(string value)
        {
            Assert.False(ParseDeclaration("background-image: " + value).HasValue);
        }

        [Fact]
        public void LegacyGradient_InTheBackgroundShorthand_IsRewrittenToo()
        {
            var sheet = ParseStyleSheet("div { background: -webkit-linear-gradient(left, red, blue) no-repeat }");
            var style = ((StyleRule)sheet.Rules[0]).Style;

            Assert.Contains("linear-gradient(to right, rgb(255, 0, 0), rgb(0, 0, 255))", style.GetPropertyValue("background-image"));
        }

        [Fact]
        public void SupportsCondition_WithAPrefixedName_BehavesLikeTheStandardName()
        {
            var sheet = ParseStyleSheet("@supports (-webkit-box-shadow: 0 0 2px black) { }");
            var supports = (SupportsRule)sheet.Rules[0];

            Assert.True(supports.Condition.Check());
        }

        // --- Box layer: the cascade sees only the standard name, so ordering and var() behave uniformly. ---

        [Theory]
        [InlineData("display: -webkit-flex", "display", "flex")]
        [InlineData("display: -webkit-inline-flex", "display", "inline-flex")]
        [InlineData("position: -webkit-sticky", "position", "sticky")]
        [InlineData("-webkit-box-sizing: border-box", "box-sizing", "border-box")]
        [InlineData("-webkit-flex-direction: column", "flex-direction", "column")]
        public async Task Cascade_AppliesThePrefixedDeclaration(string css, string standardName, string expected)
        {
            var box = await FindDivBox(css);

            Assert.Equal(expected, CssUtils.GetPropertyValue(box, standardName));
        }

        [Fact]
        public async Task Cascade_ALaterStandardDeclaration_BeatsAnEarlierPrefixedOne_AndViceVersa()
        {
            var standardWins = await FindDivBox("-webkit-box-sizing: content-box; box-sizing: border-box;");
            var prefixedWins = await FindDivBox("box-sizing: border-box; -webkit-box-sizing: content-box;");

            Assert.Equal("border-box", CssUtils.GetPropertyValue(standardWins, "box-sizing"));
            Assert.Equal("content-box", CssUtils.GetPropertyValue(prefixedWins, "box-sizing"));
        }

        [Fact]
        public async Task Cascade_APrefixedDeclaration_SupersedesAnEarlierPendingVarOfTheStandardName()
        {
            var box = await FindDivBox("--s: content-box; box-sizing: var(--s); -webkit-box-sizing: border-box;");

            Assert.Equal("border-box", CssUtils.GetPropertyValue(box, "box-sizing"));
        }

        [Fact]
        public async Task Cascade_AVarDeclaredOnThePrefixedName_ResolvesLikeTheStandardName()
        {
            var box = await FindDivBox("--s: border-box; -webkit-box-sizing: var(--s);");

            Assert.Equal("border-box", CssUtils.GetPropertyValue(box, "box-sizing"));
        }

        [Fact]
        public async Task Cascade_GlobalKeywordsWorkOnThePrefixedSpelling()
        {
            var box = await FindDivBox("box-sizing: border-box; -webkit-box-sizing: initial;");

            Assert.Equal("content-box", CssUtils.GetPropertyValue(box, "box-sizing"));
        }

        private static async Task<CssBox> FindDivBox(string css)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(
                $"<!DOCTYPE html><html><head><style>div {{ width: 200px; height: 100px; {css} }}</style></head><body><div>Text</div></body></html>",
                null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return Find(container.Root!, "div")!;
        }

        private static CssBox? Find(CssBox box, string tag)
        {
            if (box.HtmlTag?.Name == tag) return box;

            foreach (var child in box.Boxes)
            {
                var found = Find(child, tag);
                if (found != null) return found;
            }

            return null;
        }
    }
}
