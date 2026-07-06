using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.Html.Core
{
    /// <summary>
    /// HTML-layer regression test verifying that a real WOFF2 font (Inter-Medium.woff2) declared
    /// via @font-face is loaded and resolved during HTML/CSS layout -- i.e. the DomParser's
    /// async font-face handling (AddFontFamilyFromUrl -> PdfSharpAdapter.AddFont ->
    /// FontFormatConverter.ToOpenType -> Woff2Converter.Convert) completes and the box tree ends
    /// up using the custom family rather than silently falling back to the default font.
    /// </summary>
    public class FontFaceWoff2RenderingTests
    {
        private static string GetWoff2Base64()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Inter-Medium.woff2");
            return Convert.ToBase64String(File.ReadAllBytes(path));
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

        [Fact]
        public async Task Woff2FontFace_RendersBodyText_WithDeclaredFontFamily()
        {
            var html = $@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'InterWoff2Test'; src: url('data:font/woff2;base64,{GetWoff2Base64()}') format('woff2'); }}
body {{ font-family: 'InterWoff2Test', serif; font-size: 14pt; }}
p {{ width: 300px; }}
</style></head>
<body><p>Hello WOFF2</p></body>
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

            // The resolved XFont reports the font's own internal family name (read from its
            // 'name' table after WOFF2->OpenType conversion), not the @font-face CSS alias --
            // this confirms the real WOFF2 font was decoded and actually used, not silently
            // falling back to a default/system font.
            var fontAdapter = Assert.IsType<FontAdapter>(paragraph!.ActualFont);
            Assert.Equal("Inter Medium", fontAdapter.Font.Name);
        }
    }
}
