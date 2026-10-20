using PeachPDF.Rendering;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachBrackets.Css;
using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachDrawing.PDF.Engine.Drawing;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Direct typed-storage assertions for <c>display</c>, now backed by
    /// <c>CssProperty&lt;DisplayMode&gt;</c>. <c>display</c> is the property issue #598 (keyword
    /// comparison case-sensitivity) is named for, so this specifically exercises that a non-canonical-case
    /// author value is still accepted and resolves to the same <see cref="DisplayMode"/> a lowercase
    /// value would - complementing the layout-geometry tests elsewhere, which exercise <c>display</c>
    /// indirectly through its effect on box placement rather than asserting the stored <c>.Value</c> and
    /// its case-insensitive acceptance directly.
    /// </summary>
    public class DisplayTypedStorageTests
    {
        [Theory]
        [InlineData("BLOCK", "Block")]
        [InlineData("Inline-Block", "InlineBlock")]
        [InlineData("FLEX", "Flex")]
        [InlineData("Inline-Flex", "InlineFlex")]
        [InlineData("GRID", "Grid")]
        [InlineData("Inline-Grid", "InlineGrid")]
        [InlineData("TABLE", "Table")]
        [InlineData("Inline-Table", "InlineTable")]
        [InlineData("List-Item", "ListItem")]
        [InlineData("NONE", "None")]
        [InlineData("CONTENTS", "Contents")]
        public async Task Display_StoresTypedValue_CaseInsensitively(string cssValue, string expectedModeName)
        {
            var expected = System.Enum.Parse<DisplayMode>(expectedModeName);
            var box = await GetBoxAsync($"<div id='el' style='display:{cssValue}'></div>");
            Assert.Equal(expected, box.Display.Value);
        }

        [Fact]
        public async Task Display_UnrecognizedValue_DeclarationIsDropped_UaStylesheetValueWins()
        {
            // An invalid value makes the whole declaration invalid (CSS Syntax 3 error recovery), so it
            // never reaches the cascade at all - the UA stylesheet's own "div { display: block }" rule is
            // what's left in effect, not the property's CSS-wide initial value ("inline").
            var box = await GetBoxAsync("<div id='el' style='display:not-a-real-display'></div>");
            Assert.Equal(DisplayMode.Block, box.Display.Value);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static async Task<CssBox> GetBoxAsync(string bodyHtml)
        {
            var html = $"<!DOCTYPE html><html><head></head><body>{bodyHtml}</body></html>";
            var renderContext = new HtmlRenderContext();
            var container = new HtmlContainerInt(renderContext);
            await container.SetHtml(html, null);

            var size = new Size(595, 842);
            container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);

            using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            // A display:contents element is in no box's children - only in the container's shell list.
            return (FindById(container.Root!, "el")
                ?? container.DisplayContentsShells.Find(s => s.HtmlTag?.TryGetAttribute("id", "") == "el"))!;
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
