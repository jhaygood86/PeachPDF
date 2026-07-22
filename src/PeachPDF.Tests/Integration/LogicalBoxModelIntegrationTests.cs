using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Integration tests for CSS logical box-model properties (CSS Logical Properties &amp; Values Level 1).
    /// PeachPDF is always LTR / horizontal-tb, so each logical property maps 1:1 to a physical one:
    /// block-start→top, block-end→bottom, inline-start→left, inline-end→right. The logical names resolve to
    /// the physical longhands in the CSS-OM (Layer A), so these tests assert the physical longhand each
    /// logical declaration produces on the cascaded box, plus one end-to-end layout-geometry check.
    /// </summary>
    public class LogicalBoxModelIntegrationTests
    {
        // ── Longhands (block/inline start/end → physical edge) ────────────────────

        [Theory]
        [InlineData("margin-block-start: 5pt", "margin-top", "5pt")]
        [InlineData("margin-block-end: 5pt", "margin-bottom", "5pt")]
        [InlineData("margin-inline-start: 5pt", "margin-left", "5pt")]
        [InlineData("margin-inline-end: 5pt", "margin-right", "5pt")]
        [InlineData("padding-block-start: 5pt", "padding-top", "5pt")]
        [InlineData("padding-block-end: 5pt", "padding-bottom", "5pt")]
        [InlineData("padding-inline-start: 5pt", "padding-left", "5pt")]
        [InlineData("padding-inline-end: 5pt", "padding-right", "5pt")]
        [InlineData("inset-block-start: 5pt", "top", "5pt")]
        [InlineData("inset-block-end: 5pt", "bottom", "5pt")]
        [InlineData("inset-inline-start: 5pt", "left", "5pt")]
        [InlineData("inset-inline-end: 5pt", "right", "5pt")]
        [InlineData("border-block-start-width: 5pt", "border-top-width", "5pt")]
        [InlineData("border-block-start-style: dashed", "border-top-style", "dashed")]
        [InlineData("border-block-end-width: 5pt", "border-bottom-width", "5pt")]
        [InlineData("border-inline-start-width: 5pt", "border-left-width", "5pt")]
        [InlineData("border-inline-end-style: dotted", "border-right-style", "dotted")]
        [InlineData("border-block-start-color: red", "border-top-color", "rgb(255, 0, 0)")]
        [InlineData("border-block-end-color: red", "border-bottom-color", "rgb(255, 0, 0)")]
        [InlineData("border-inline-start-color: red", "border-left-color", "rgb(255, 0, 0)")]
        [InlineData("border-inline-end-color: red", "border-right-color", "rgb(255, 0, 0)")]
        public async Task LogicalLonghand_ResolvesToPhysicalLonghand(string declaration, string physical, string expected)
        {
            var box = await CascadedDiv(declaration);
            Assert.Equal(expected, CssUtils.GetPropertyValue(box, physical));
        }

        // ── Two-value block/inline shorthands ─────────────────────────────────────

        [Fact]
        public async Task MarginBlockAndInline_ExpandToPhysicalEdges()
        {
            var box = await CascadedDiv("margin-block: 1pt 2pt; margin-inline: 3pt 4pt");
            Assert.Equal("1pt", CssUtils.GetPropertyValue(box, "margin-top"));
            Assert.Equal("2pt", CssUtils.GetPropertyValue(box, "margin-bottom"));
            Assert.Equal("3pt", CssUtils.GetPropertyValue(box, "margin-left"));
            Assert.Equal("4pt", CssUtils.GetPropertyValue(box, "margin-right"));
        }

        [Fact]
        public async Task MarginBlock_SingleValue_AppliesToBothBlockEdges()
        {
            var box = await CascadedDiv("margin-block: 7pt");
            Assert.Equal("7pt", CssUtils.GetPropertyValue(box, "margin-top"));
            Assert.Equal("7pt", CssUtils.GetPropertyValue(box, "margin-bottom"));
        }

        [Fact]
        public async Task PaddingBlockAndInline_ExpandToPhysicalEdges()
        {
            var box = await CascadedDiv("padding-block: 1pt 2pt; padding-inline: 3pt 4pt");
            Assert.Equal("1pt", CssUtils.GetPropertyValue(box, "padding-top"));
            Assert.Equal("2pt", CssUtils.GetPropertyValue(box, "padding-bottom"));
            Assert.Equal("3pt", CssUtils.GetPropertyValue(box, "padding-left"));
            Assert.Equal("4pt", CssUtils.GetPropertyValue(box, "padding-right"));
        }

        // ── inset shorthands ──────────────────────────────────────────────────────

        [Fact]
        public async Task Inset_FourValues_MapToTopRightBottomLeft()
        {
            var box = await CascadedDiv("position: absolute; inset: 1pt 2pt 3pt 4pt");
            Assert.Equal("1pt", CssUtils.GetPropertyValue(box, "top"));
            Assert.Equal("2pt", CssUtils.GetPropertyValue(box, "right"));
            Assert.Equal("3pt", CssUtils.GetPropertyValue(box, "bottom"));
            Assert.Equal("4pt", CssUtils.GetPropertyValue(box, "left"));
        }

        [Fact]
        public async Task InsetBlockAndInline_MapToPhysicalEdges()
        {
            var box = await CascadedDiv("position: absolute; inset-block: 1pt 2pt; inset-inline: 3pt 4pt");
            Assert.Equal("1pt", CssUtils.GetPropertyValue(box, "top"));
            Assert.Equal("2pt", CssUtils.GetPropertyValue(box, "bottom"));
            Assert.Equal("3pt", CssUtils.GetPropertyValue(box, "left"));
            Assert.Equal("4pt", CssUtils.GetPropertyValue(box, "right"));
        }

        // ── border logical shorthands ─────────────────────────────────────────────

        [Fact]
        public async Task BorderBlockStart_EdgeShorthand_ExpandsToPhysicalEdge()
        {
            var box = await CascadedDiv("border-block-start: 2pt solid red");
            Assert.Equal("2pt", CssUtils.GetPropertyValue(box, "border-top-width"));
            Assert.Equal("solid", CssUtils.GetPropertyValue(box, "border-top-style"));
            Assert.Equal("rgb(255, 0, 0)", CssUtils.GetPropertyValue(box, "border-top-color"));
        }

        [Fact]
        public async Task BorderInlineEnd_EdgeShorthand_ExpandsToRightEdge()
        {
            var box = await CascadedDiv("border-inline-end: 3pt dotted blue");
            Assert.Equal("3pt", CssUtils.GetPropertyValue(box, "border-right-width"));
            Assert.Equal("dotted", CssUtils.GetPropertyValue(box, "border-right-style"));
            Assert.Equal("rgb(0, 0, 255)", CssUtils.GetPropertyValue(box, "border-right-color"));
        }

        [Fact]
        public async Task BorderBlock_TwoEdgeShorthand_AppliesToBothBlockEdges()
        {
            var box = await CascadedDiv("border-block: 2pt solid red");
            Assert.Equal("2pt", CssUtils.GetPropertyValue(box, "border-top-width"));
            Assert.Equal("2pt", CssUtils.GetPropertyValue(box, "border-bottom-width"));
            Assert.Equal("solid", CssUtils.GetPropertyValue(box, "border-top-style"));
            Assert.Equal("solid", CssUtils.GetPropertyValue(box, "border-bottom-style"));
            Assert.Equal("rgb(255, 0, 0)", CssUtils.GetPropertyValue(box, "border-top-color"));
            Assert.Equal("rgb(255, 0, 0)", CssUtils.GetPropertyValue(box, "border-bottom-color"));
        }

        [Fact]
        public async Task BorderBlockWidth_TwoValues_MapToBlockEdges()
        {
            var box = await CascadedDiv("border-block-width: 1pt 2pt; border-inline-color: red blue");
            Assert.Equal("1pt", CssUtils.GetPropertyValue(box, "border-top-width"));
            Assert.Equal("2pt", CssUtils.GetPropertyValue(box, "border-bottom-width"));
            Assert.Equal("rgb(255, 0, 0)", CssUtils.GetPropertyValue(box, "border-left-color"));
            Assert.Equal("rgb(0, 0, 255)", CssUtils.GetPropertyValue(box, "border-right-color"));
        }

        [Fact]
        public async Task BorderBlockStyleAndColor_TwoValues_MapToBlockEdges()
        {
            var box = await CascadedDiv("border-block-style: solid dashed; border-block-color: red blue");
            Assert.Equal("solid", CssUtils.GetPropertyValue(box, "border-top-style"));
            Assert.Equal("dashed", CssUtils.GetPropertyValue(box, "border-bottom-style"));
            Assert.Equal("rgb(255, 0, 0)", CssUtils.GetPropertyValue(box, "border-top-color"));
            Assert.Equal("rgb(0, 0, 255)", CssUtils.GetPropertyValue(box, "border-bottom-color"));
        }

        [Fact]
        public async Task BorderInline_TwoEdgeShorthand_AppliesToBothInlineEdges()
        {
            var box = await CascadedDiv("border-inline: 2pt solid green");
            Assert.Equal("2pt", CssUtils.GetPropertyValue(box, "border-left-width"));
            Assert.Equal("2pt", CssUtils.GetPropertyValue(box, "border-right-width"));
            Assert.Equal("solid", CssUtils.GetPropertyValue(box, "border-left-style"));
            Assert.Equal("solid", CssUtils.GetPropertyValue(box, "border-right-style"));
            Assert.Equal("rgb(0, 128, 0)", CssUtils.GetPropertyValue(box, "border-left-color"));
            Assert.Equal("rgb(0, 128, 0)", CssUtils.GetPropertyValue(box, "border-right-color"));
        }

        [Fact]
        public async Task BorderInlineWidthAndStyle_TwoValues_MapToInlineEdges()
        {
            var box = await CascadedDiv("border-inline-width: 1pt 2pt; border-inline-style: solid dashed");
            Assert.Equal("1pt", CssUtils.GetPropertyValue(box, "border-left-width"));
            Assert.Equal("2pt", CssUtils.GetPropertyValue(box, "border-right-width"));
            Assert.Equal("solid", CssUtils.GetPropertyValue(box, "border-left-style"));
            Assert.Equal("dashed", CssUtils.GetPropertyValue(box, "border-right-style"));
        }

        // ── End-to-end layout geometry ────────────────────────────────────────────

        [Fact]
        public async Task LogicalMargins_AffectActualLayoutGeometry()
        {
            // margin-block-start pushes the box down; margin-inline-start shifts it right — proving the logical
            // declarations reach the layout engine as real physical margins, not just stored strings.
            var html = """
                <!DOCTYPE html><html><head><style>
                  #el { margin-block-start: 20pt; margin-inline-start: 15pt; width: 50pt; height: 30pt; }
                </style></head><body><div id="el">x</div></body></html>
                """;
            var root = await BuildBoxTree(html);
            var el = FindById(root, "el")!;

            Assert.Equal(20, el.ActualMarginTop, 1);
            Assert.Equal(15, el.ActualMarginLeft, 1);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static async Task<CssBox> CascadedDiv(string declarations)
        {
            var html = $"<!DOCTYPE html><html><head><style>#el {{ {declarations}; }}</style></head><body><div id=\"el\">x</div></body></html>";
            var root = await BuildBoxTree(html);
            return FindById(root, "el")!;
        }

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
