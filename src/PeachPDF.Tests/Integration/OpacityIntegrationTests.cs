using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    public class OpacityIntegrationTests
    {
        // --- Box-property parsing/clamping ---

        [Fact]
        public async Task NoOpacity_IsFullyOpaque()
        {
            var divBox = await FindDivBox("");

            Assert.True(divBox.IsOpaque);
            Assert.Equal(1.0, divBox.ActualOpacity);
        }

        [Fact]
        public async Task Opacity_DecimalValue_IsParsed()
        {
            var divBox = await FindDivBox("opacity: 0.5;");

            Assert.False(divBox.IsOpaque);
            Assert.Equal(0.5, divBox.ActualOpacity, 3);
        }

        [Fact]
        public async Task Opacity_PercentValue_IsParsed()
        {
            var divBox = await FindDivBox("opacity: 40%;");

            Assert.Equal(0.4, divBox.ActualOpacity, 3);
        }

        [Fact]
        public async Task Opacity_AboveOne_IsClamped()
        {
            var divBox = await FindDivBox("opacity: 2;");

            Assert.True(divBox.IsOpaque);
            Assert.Equal(1.0, divBox.ActualOpacity);
        }

        [Fact]
        public async Task Opacity_BelowZero_IsClamped()
        {
            var divBox = await FindDivBox("opacity: -0.5;");

            Assert.Equal(0.0, divBox.ActualOpacity);
        }

        [Fact]
        public async Task Opacity_IsNotInherited()
        {
            var html = @"<!DOCTYPE html><html><head><style>
#parent { opacity: 0.5; }
#child { width: 50px; height: 50px; }
</style></head><body><div id=""parent""><div id=""child""></div></div></body></html>";

            var container = await LayoutHtml(html);
            var child = FindById(container.Root!, "child")!;

            Assert.True(child.IsOpaque);
            Assert.Equal(1.0, child.ActualOpacity);
        }

        // --- PDF content-stream compositing ---

        [Fact]
        public async Task Opacity_RendersIsolatedTransparencyGroupAndConstantAlphaExtGState()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div style="width: 100px; height: 100px; background: #ff0000; opacity: 0.5;"></div>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Contains("/Subtype /Form", pdfText);
            Assert.Contains("/S /Transparency", pdfText);
            Assert.Contains("/I true", pdfText);
            Assert.Matches(new Regex(@"/ca 0\.5\b"), pdfText);
            Assert.Matches(new Regex(@"/CA 0\.5\b"), pdfText);
        }

        [Fact]
        public async Task Opacity_GsAndDoShareTheSameCm()
        {
            // Same atomicity requirement as the SVG mask work (see
            // InlineSvg_Mask_SMaskGsAndContentDoShareTheSameCm) - the alpha-activating "gs" must be on
            // the same "q ... cm ... gs ... Do Q" line as the tile's placement, not applied separately.
            var html = """
                <!DOCTYPE html><html><body>
                <div style="width: 100px; height: 100px; background: #ff0000; opacity: 0.5;"></div>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Matches(new Regex(@"cm /GS\d+ gs /Fm\d+ Do"), pdfText);
        }

        [Fact]
        public async Task Opacity_OverlappingChildren_DoNotDoubleBlend()
        {
            // The regression this whole feature exists to fix: two overlapping semi-transparent
            // children under one opaque-child-bearing parent `opacity` must each paint at FULL local
            // alpha inside the isolated tile (no per-child "gs" with a reduced /ca), with only the
            // single outer group composite applying the actual opacity. If a future change regresses
            // to the simpler per-shape alpha multiply, this would start seeing an extra "gs" pair
            // per child inside the tile.
            var html = """
                <!DOCTYPE html><html><body>
                <div style="width: 100px; height: 100px; opacity: 0.5; position: relative;">
                  <div style="position: absolute; left: 0; top: 0; width: 60px; height: 60px; background: #ff0000;"></div>
                  <div style="position: absolute; left: 20px; top: 20px; width: 60px; height: 60px; background: #0000ff;"></div>
                </div>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            // Only one constant-alpha ExtGState at 0.5 should exist (the outer group composite) -
            // the children's own fills must not each carry their own /ca 0.5.
            var alphaMatches = Regex.Matches(pdfText, @"/ca 0\.5\b");
            Assert.Single(alphaMatches);
        }

        [Fact]
        public async Task Opacity_CombinedWithTransform_StillRenders()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div style="width: 100px; height: 100px; background: #ff0000; opacity: 0.5; transform: rotate(10deg);"></div>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Contains("/S /Transparency", pdfText);
            Assert.Matches(new Regex(@"/ca 0\.5\b"), pdfText);
        }

        [Fact]
        public async Task Opacity_NestedOpacityBoxes_BothComposite()
        {
            // Exercises the CreateTile nested-tile fix (XGraphics.Owner) - a child box with its own
            // opacity, painted from inside its already-opacity-wrapped parent's tile, must still be
            // able to create ITS OWN tile rather than silently falling back to unopaqued painting.
            var html = """
                <!DOCTYPE html><html><body>
                <div style="width: 100px; height: 100px; opacity: 0.5;">
                  <div style="width: 50px; height: 50px; background: #00ff00; opacity: 0.5;"></div>
                </div>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            // The two composites both use alpha 0.5, so PdfExtGStateTable's cache correctly shares a
            // single "/ca 0.5" ExtGState object between them (real PDF resource reuse, not a bug) -
            // assert on the number of "gs ... Do" placements (one per tile composite) instead of the
            // object body text, which only appears once regardless of how many times it's referenced.
            var placementMatches = Regex.Matches(pdfText, @"cm /GS\d+ gs /Fm\d+ Do");
            Assert.Equal(2, placementMatches.Count);
        }

        [Fact]
        public async Task Opacity_OnBoxWithImage_AppliesToImage()
        {
            const string pngBase64 =
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
            var html = $"""
                <!DOCTYPE html><html><body>
                <div style="width: 50px; height: 50px; opacity: 0.5;">
                  <img src="data:image/png;base64,{pngBase64}" width="50" height="50" />
                </div>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Contains("/Subtype /Image", pdfText);
            Assert.Contains("/S /Transparency", pdfText);
            Assert.Matches(new Regex(@"/ca 0\.5\b"), pdfText);
        }

        private static async Task<string> GetPdfText(string html)
        {
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            config.SetMargins(20);
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        // --- Helpers (box-property tests) ---

        private Task<CssBox> FindDivBox(string css)
        {
            var html = $@"<!DOCTYPE html><html><head><style>
div {{ width: 200px; height: 100px; {css} }}
</style></head><body><div></div></body></html>";
            return FindDivBoxFromHtml(html);
        }

        private async Task<CssBox> FindDivBoxFromHtml(string html)
        {
            var container = await LayoutHtml(html);
            Assert.NotNull(container.Root);
            return FindByTag(container.Root!, "div")!;
        }

        private async Task<HtmlContainerInt> LayoutHtml(string html)
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

            return container;
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
