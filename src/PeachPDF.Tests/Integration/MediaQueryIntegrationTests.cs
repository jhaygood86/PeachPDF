using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Integration tests verifying that @media rules are applied correctly when rendering
    /// to PDF (print medium). Rules targeting "print" or "all" must apply; rules targeting
    /// "screen" must not. The "not" modifier inverts that logic.
    /// </summary>
    public class MediaQueryIntegrationTests
    {
        // ── @media print ───────────────────────────────────────────────────────

        [Fact]
        public async Task AtMediaPrint_StyleRule_IsApplied()
        {
            var html = """
                <!DOCTYPE html><html><head><style>
                @media print { div { color: red; } }
                </style></head><body><div id="el">text</div></body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("rgb(255, 0, 0)", el!.Color);
        }

        // ── @media screen ─────────────────────────────────────────────────────

        [Fact]
        public async Task AtMediaScreen_StyleRule_IsNotApplied()
        {
            var html = """
                <!DOCTYPE html><html><head><style>
                @media screen { div { color: blue; } }
                </style></head><body><div id="el">text</div></body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("black", el!.Color);
        }

        // ── @media all ────────────────────────────────────────────────────────

        [Fact]
        public async Task AtMediaAll_StyleRule_IsApplied()
        {
            var html = """
                <!DOCTYPE html><html><head><style>
                @media all { div { color: green; } }
                </style></head><body><div id="el">text</div></body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("rgb(0, 128, 0)", el!.Color);
        }

        // ── @media only print ─────────────────────────────────────────────────

        [Fact]
        public async Task AtMediaOnlyPrint_StyleRule_IsApplied()
        {
            var html = """
                <!DOCTYPE html><html><head><style>
                @media only print { div { color: red; } }
                </style></head><body><div id="el">text</div></body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("rgb(255, 0, 0)", el!.Color);
        }

        // ── @media not print ──────────────────────────────────────────────────

        [Fact]
        public async Task AtMediaNotPrint_StyleRule_IsNotApplied()
        {
            var html = """
                <!DOCTYPE html><html><head><style>
                @media not print { div { color: orange; } }
                </style></head><body><div id="el">text</div></body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("black", el!.Color);
        }

        // ── @media not screen ─────────────────────────────────────────────────

        [Fact]
        public async Task AtMediaNotScreen_StyleRule_IsApplied()
        {
            var html = """
                <!DOCTYPE html><html><head><style>
                @media not screen { div { color: purple; } }
                </style></head><body><div id="el">text</div></body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("rgb(128, 0, 128)", el!.Color);
        }

        // ── print overrides screen ────────────────────────────────────────────

        [Fact]
        public async Task AtMediaPrint_OverridesAtMediaScreen_WhenBothPresent()
        {
            var html = """
                <!DOCTYPE html><html><head><style>
                @media screen { div { color: blue; } }
                @media print  { div { color: red;  } }
                </style></head><body><div id="el">text</div></body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("rgb(255, 0, 0)", el!.Color);
        }

        // ── comma-separated list: print in the list ───────────────────────────

        [Fact]
        public async Task AtMediaPrintCommaScreen_StyleRule_IsApplied()
        {
            var html = """
                <!DOCTYPE html><html><head><style>
                @media print, screen { div { color: green; } }
                </style></head><body><div id="el">text</div></body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("rgb(0, 128, 0)", el!.Color);
        }

        // ── helpers ───────────────────────────────────────────────────────────

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
