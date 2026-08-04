using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Direct typed-storage assertions for the flex/grid alignment group (<c>flex-direction</c>,
    /// <c>flex-wrap</c>, <c>justify-content</c>, <c>align-items</c>, <c>align-content</c>,
    /// <c>align-self</c>, <c>justify-items</c>, <c>justify-self</c>) - each now backed by
    /// <c>CssProperty&lt;TEnum&gt;</c>. Complements the existing layout-geometry tests
    /// (<see cref="FlexboxIntegrationTests"/>/<c>GridLayoutIntegrationTests</c>), which exercise these
    /// properties indirectly through their effect on positions rather than asserting the stored
    /// <c>.Value</c> directly.
    /// </summary>
    public class FlexGridAlignmentTypedStorageTests
    {
        [Fact]
        public async Task FlexDirection_StoresTypedValue_CaseInsensitively()
        {
            var box = await GetBoxAsync("<div id='el' style='display:flex; flex-direction:ROW-Reverse'></div>");
            Assert.Equal(FlexDirection.RowReverse, box.FlexDirection.Value);
        }

        [Fact]
        public async Task FlexWrap_StoresTypedValue_CaseInsensitively()
        {
            var box = await GetBoxAsync("<div id='el' style='display:flex; flex-wrap:Wrap-REVERSE'></div>");
            Assert.Equal(FlexWrap.WrapReverse, box.FlexWrap.Value);
        }

        [Fact]
        public async Task JustifyContent_StoresTypedValue_CaseInsensitively()
        {
            var box = await GetBoxAsync("<div id='el' style='display:flex; justify-content:SPACE-Between'></div>");
            Assert.Equal(JustifyContent.SpaceBetween, box.JustifyContent.Value);
        }

        [Fact]
        public async Task AlignItems_StoresTypedValue_CaseInsensitively()
        {
            var box = await GetBoxAsync("<div id='el' style='display:flex; align-items:FLEX-End'></div>");
            Assert.Equal(AlignItem.FlexEnd, box.AlignItems.Value);
        }

        [Fact]
        public async Task AlignContent_StoresTypedValue_CaseInsensitively()
        {
            var box = await GetBoxAsync("<div id='el' style='display:flex; align-content:Space-EVENLY'></div>");
            Assert.Equal(AlignContent.SpaceEvenly, box.AlignContent.Value);
        }

        [Fact]
        public async Task AlignSelf_StoresTypedValue_CaseInsensitively()
        {
            var box = await GetBoxAsync("<div style='display:flex'><div id='el' style='align-self:CENTER'></div></div>");
            Assert.Equal(AlignItem.Center, box.AlignSelf.Value);
        }

        [Fact]
        public async Task AlignSelf_DefaultsToAuto()
        {
            var box = await GetBoxAsync("<div style='display:flex'><div id='el'></div></div>");
            Assert.Equal(AlignItem.Auto, box.AlignSelf.Value);
        }

        [Fact]
        public async Task JustifyItems_StoresTypedValue_CaseInsensitively()
        {
            var box = await GetBoxAsync("<div id='el' style='display:grid; justify-items:Self-End'></div>");
            Assert.Equal(JustifyItem.SelfEnd, box.JustifyItems.Value);
        }

        [Fact]
        public async Task JustifySelf_StoresTypedValue_CaseInsensitively()
        {
            var box = await GetBoxAsync("<div style='display:grid'><div id='el' style='justify-self:RIGHT'></div></div>");
            Assert.Equal(JustifyItem.Right, box.JustifySelf.Value);
        }

        [Fact]
        public async Task JustifySelf_DefaultsToAuto()
        {
            var box = await GetBoxAsync("<div style='display:grid'><div id='el'></div></div>");
            Assert.Equal(JustifyItem.Auto, box.JustifySelf.Value);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static async Task<CssBox> GetBoxAsync(string bodyHtml)
        {
            var html = $"<!DOCTYPE html><html><head></head><body>{bodyHtml}</body></html>";
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
            return FindById(container.Root!, "el")!;
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            var val = box.HtmlTag?.TryGetAttribute("id", "");
            if (val != null && val.Equals(id, System.StringComparison.OrdinalIgnoreCase))
                return box;
            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found != null) return found;
            }
            return null;
        }
    }
}
