using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end cascade tests confirming a logical margin/padding/inset/border longhand resolves to the
    /// correct physical edge for a <c>writing-mode</c> other than the default <c>horizontal-tb</c>, and for
    /// <c>direction: rtl</c> - <see cref="LogicalBoxModelIntegrationTests"/> only ever exercises the
    /// default horizontal-tb/ltr case, where every logical edge happens to coincide with the "obvious"
    /// physical one.
    /// </summary>
    public class LogicalPropertiesWritingModeIntegrationTests
    {
        [Fact]
        public async Task MarginInlineStart_UnderDirectionRtl_ResolvesToMarginRight()
        {
            var box = await CascadedDiv("direction: rtl; margin-inline-start: 5pt");

            Assert.Equal("5pt", CssUtils.GetPropertyValue(box, "margin-right"));
            Assert.Equal("0", CssUtils.GetPropertyValue(box, "margin-left"));
        }

        [Fact]
        public async Task MarginInlineStart_UnderDirAutoWithRtlContent_ResolvesToMarginRight()
        {
            // dir="auto" runs the first-strong-character detection pre-pass
            // (DomParser.ResolveAutoDirectionality) before the cascade resolves Direction, so a logical
            // property here must pick up the *detected* rtl, not the ltr default - proving
            // ResolveLogicalProperties reads the box's final resolved Direction rather than one computed
            // before auto-detection ran.
            var html = """
                <!DOCTYPE html><html><head><style>
                  #el { margin-inline-start: 5pt; }
                </style></head><body><div id="el" dir="auto">שלום עולם</div></body></html>
                """;
            var root = await BuildBoxTree(html);
            var el = FindById(root, "el")!;

            Assert.Equal("5pt", CssUtils.GetPropertyValue(el, "margin-right"));
            Assert.Equal("0", CssUtils.GetPropertyValue(el, "margin-left"));
        }

        [Fact]
        public async Task MarginBlockStart_UnderWritingModeVerticalRl_ResolvesToMarginRight()
        {
            var box = await CascadedDiv("writing-mode: vertical-rl; margin-block-start: 5pt");

            Assert.Equal("5pt", CssUtils.GetPropertyValue(box, "margin-right"));
            Assert.Equal("0", CssUtils.GetPropertyValue(box, "margin-top"));
        }

        [Fact]
        public async Task MarginInlineStart_UnderWritingModeVerticalRlAndDirectionRtl_ResolvesToMarginBottom()
        {
            // vertical-rl's inline axis runs top-to-bottom for ltr, bottom-to-top for rtl (CSS Writing
            // Modes 4 §7.1) - inline-start is "bottom" here, not "top" as it would be for ltr.
            var box = await CascadedDiv("writing-mode: vertical-rl; direction: rtl; margin-inline-start: 5pt");

            Assert.Equal("5pt", CssUtils.GetPropertyValue(box, "margin-bottom"));
        }

        [Fact]
        public async Task PaddingBlockStart_UnderWritingModeVerticalLr_ResolvesToPaddingLeft()
        {
            var box = await CascadedDiv("writing-mode: vertical-lr; padding-block-start: 5pt");

            Assert.Equal("5pt", CssUtils.GetPropertyValue(box, "padding-left"));
        }

        [Fact]
        public async Task InsetInlineStart_UnderWritingModeSidewaysLr_ResolvesToBottom()
        {
            // sideways-lr reverses the inline mapping relative to vertical-lr/vertical-rl/sideways-rl:
            // ltr's inline-start is "bottom" here, not "top".
            var box = await CascadedDiv("position: absolute; writing-mode: sideways-lr; inset-inline-start: 5pt");

            Assert.Equal("5pt", CssUtils.GetPropertyValue(box, "bottom"));
        }

        [Fact]
        public async Task BorderBlockStartWidth_UnderWritingModeVerticalRl_ResolvesToBorderRightWidth()
        {
            var box = await CascadedDiv("writing-mode: vertical-rl; border-block-start-width: 3pt; border-block-start-style: solid");

            Assert.Equal("3pt", CssUtils.GetPropertyValue(box, "border-right-width"));
            Assert.Equal("solid", CssUtils.GetPropertyValue(box, "border-right-style"));
        }

        [Fact]
        public async Task LogicalMargin_UnderWritingModeVerticalRl_AffectsActualLayoutGeometry()
        {
            // margin-block-start should push the box's *right* edge inward (not its top) under
            // vertical-rl - proving the resolved value reaches real layout, not just a stored string.
            var html = """
                <!DOCTYPE html><html><head><style>
                  #el { writing-mode: vertical-rl; margin-block-start: 20pt; width: 50pt; height: 30pt; }
                </style></head><body><div id="el">x</div></body></html>
                """;
            var root = await BuildBoxTree(html);
            var el = FindById(root, "el")!;

            Assert.Equal(20, el.ActualMarginRight, 1);
            Assert.Equal(0, el.ActualMarginTop, 1);
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
