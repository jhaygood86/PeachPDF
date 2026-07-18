using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Integration tests for the CSS `font` shorthand at the cascade level. `font` is expanded into its
    /// longhands (font-style/variant/weight/stretch/size/line-height/family) entirely by Layer A's
    /// FontProperty (a real ShorthandProperty - see CSS/PropertyTests/FontProperty.cs for parser-level
    /// coverage) before DomParser's cascade ever sees it; these tests confirm that end-to-end pipeline,
    /// including the calc()-in-font-size case a since-removed regex-based CssUtils.SetFontPropertyValue
    /// used to mangle (it searched for the first length-shaped substring rather than parsing the whole
    /// shorthand grammar, so "font: bold calc(12px + 4px)/1.4 Arial" corrupted font-size, dropped
    /// line-height, and injected leftover expression text into font-family).
    /// </summary>
    public class FontShorthandIntegrationTests
    {
        [Fact]
        public async Task FontShorthand_FullForm_ResolvesAllLonghands()
        {
            var root = await BuildBoxTree(FontHtml("italic bold 16px/1.4 Arial"));
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("italic", el!.FontStyle);
            Assert.Equal("bold", el.FontWeight);
            Assert.Equal("16px", el.FontSize);
            Assert.Equal("1.4", el.LineHeight);
            Assert.Contains("Arial", el.FontFamily);
        }

        [Fact]
        public async Task FontShorthand_CalcFontSize_FoldsCorrectly()
        {
            // The specific case the old regex-based parser mangled: font-size folded to "12px" (dropping
            // the "+ 4px" term), line-height silently discarded, and "+ 4px)/1.4 Arial" leaking into
            // font-family instead. FontProperty's real converter resolves calc() the same as any other
            // property.
            var root = await BuildBoxTree(FontHtml("bold calc(12px + 4px)/1.4 Arial"));
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("bold", el!.FontWeight);
            Assert.Equal("16px", el.FontSize);
            Assert.Equal("1.4", el.LineHeight);
            Assert.Contains("Arial", el.FontFamily);
            Assert.DoesNotContain("+", el.FontFamily);
            Assert.DoesNotContain("calc", el.FontFamily);
        }

        [Fact]
        public async Task FontShorthand_WithVar_ResolvesAfterSubstitution()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="el" style="--fs: 16px; font: bold var(--fs)/1.4 Arial"></div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("bold", el!.FontWeight);
            Assert.Equal("16px", el.FontSize);
            Assert.Equal("1.4", el.LineHeight);
            Assert.Contains("Arial", el.FontFamily);
        }

        [Fact]
        public async Task FontShorthand_WithVar_ResetsUnspecifiedSubPropertiesToInitial()
        {
            // Regression: DomParser.ApplyResolvedPropertyValue (the var()-containing shorthand path)
            // used to skip any sub-property the shorthand text didn't mention instead of resetting it to
            // its CSS-spec initial value - unlike the non-var() shorthand path, which already did this
            // correctly via the "initial" sentinel. A prior font-style: italic must not survive a later
            // "font: bold var(--fs)/1.4 Arial" (which declares no font-style at all).
            var html = """
                <!DOCTYPE html><html><body>
                <div id="el" style="--fs: 16px; font-style: italic; font: bold var(--fs)/1.4 Arial"></div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("normal", el!.FontStyle);
            Assert.Equal("bold", el.FontWeight);
            Assert.Equal("16px", el.FontSize);
        }

        [Theory]
        [InlineData("300", "bolder", 400)]
        [InlineData("400", "bolder", 700)]
        [InlineData("600", "bolder", 900)]
        [InlineData("300", "lighter", 100)]
        [InlineData("600", "lighter", 400)]
        [InlineData("800", "lighter", 700)]
        public async Task FontWeight_BolderLighter_StepsRelativeToRealParentWeight(string parentWeight, string childKeyword, int expected)
        {
            var html = $"""
                <!DOCTYPE html><html><body>
                <div style="font-weight: {parentWeight}"><span id="el" style="font-weight: {childKeyword}">text</span></div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal(expected, el!.ActualNumericWeight);
        }

        [Theory]
        [InlineData("condensed", 3)]
        [InlineData("expanded", 7)]
        [InlineData("ultra-condensed", 1)]
        [InlineData("ultra-expanded", 9)]
        public async Task FontStretch_ResolvesToExpectedNumericScale(string keyword, int expected)
        {
            var html = $"""
                <!DOCTYPE html><html><body>
                <div id="el" style="font-stretch: {keyword}">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal(keyword, el!.FontStretch);
            Assert.Equal(expected, el.ActualStretch);
        }

        [Fact]
        public async Task FontStretch_IsInherited_FromParentToChild()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div style="font-stretch: condensed"><span id="el">text</span></div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("condensed", el!.FontStretch);
            Assert.Equal(3, el.ActualStretch);
        }

        [Fact]
        public async Task FontStretch_DefaultsToNormal_WhenUnspecified()
        {
            var root = await BuildBoxTree(FontHtml("16px Arial"));
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("normal", el!.FontStretch);
            Assert.Equal(5, el.ActualStretch);
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private static string FontHtml(string fontValue) => $"""
            <!DOCTYPE html><html><body>
            <div id="el" style="font: {fontValue}"></div>
            </body></html>
            """;

        private static async Task<CssBox> BuildBoxTree(string html)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return container.Root!;
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            if (box.HtmlTag?.Attributes?.TryGetValue("id", out var boxId) == true
                && string.Equals(boxId, id, StringComparison.OrdinalIgnoreCase))
                return box;

            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found is not null) return found;
            }
            return null;
        }
    }
}
