using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Utils;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Html.Core
{
    /// <summary>
    /// HTML-layer regression test for the bug where a `@font-face src: url(...)` with no `format()`
    /// hint at all (valid CSS - format() is an optional hint) was silently dropped by
    /// <c>PdfSharpAdapter.AddFontFromStream</c>'s format allowlist, which had no case for a missing
    /// hint. Real-world stylesheets (e.g. css4.pub's Icelandic dictionary page: `src: url("Satyr10.otf")`,
    /// no format()) hit this exactly, causing the custom font to silently fall back to a default/system
    /// font instead of rendering. Mirrors <see cref="FontFaceWoff2RenderingTests"/>'s direct-layout
    /// assertion pattern, using a real CFF-flavored OTF (the same fixture the CFF embedding tests in
    /// FontEmbeddingIntegrationTests.cs use) since that's the exact font technology the reported bug
    /// involved.
    /// </summary>
    public class FontFaceNoFormatHintRenderingTests
    {
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

        [Fact]
        public async Task NoFormatHint_OtfFontFace_RendersBodyText_WithDeclaredFontFamily()
        {
            var otfBytes = File.ReadAllBytes(BundledFonts.Otf);
            var b64 = Convert.ToBase64String(otfBytes);
            var expectedFontName = TtfFontDescription.LoadDescription(BundledFonts.Otf).FontNameInvariantCulture;

            var html = $@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'NoFormatHintOtf'; src: url('data:font/opentype;base64,{b64}'); }}
body {{ font-family: 'NoFormatHintOtf', serif; font-size: 14pt; }}
p {{ width: 300px; }}
</style></head>
<body><p>Hello CFF, no format hint</p></body>
</html>";

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
            var paragraph = FindByTag(container.Root!, "p");
            Assert.NotNull(paragraph);

            // The resolved XFont reports the font's own internal name (read from its 'name' table),
            // not the @font-face CSS alias and not a fallback - confirms the real OTF was actually
            // loaded and used despite the missing format() hint, not silently falling back.
            var fontAdapter = Assert.IsType<FontAdapter>(paragraph!.ActualFont);
            Assert.Equal(expectedFontName, fontAdapter.Font.Name);
        }
    }
}
