using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Text;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    public class ContentImageMeasurementIntegrationTests
    {
        private const string SvgMarkup = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32">
              <defs>
                <radialGradient id="a" cx="30%" cy="30%" r="80%">
                  <stop offset="0%" stop-color="#ffdca8"/>
                  <stop offset="60%" stop-color="#ffab6b"/>
                  <stop offset="100%" stop-color="#f4784a"/>
                </radialGradient>
                <radialGradient id="b" cx="35%" cy="30%" r="80%">
                  <stop offset="0%" stop-color="#ffd0c2"/>
                  <stop offset="60%" stop-color="#ff8f7d"/>
                  <stop offset="100%" stop-color="#e85d4a"/>
                </radialGradient>
              </defs>
              <circle cx="12" cy="18" r="10" fill="url(#a)"/>
              <circle cx="20" cy="18" r="10" fill="url(#b)"/>
              <path d="M16 10 C14 5 10 3 7 4 C9 7 12 9 15 9 Z" fill="#5cb85c"/>
              <path d="M16 10 C18 5 22 3 25 4 C23 7 20 9 17 9 Z" fill="#4a9e4a"/>
            </svg>
            """;

        private static string SvgDataUri() =>
            "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(SvgMarkup));

        // A <style> tag nested inside a <td> - matching every real-world content-image swatch,
        // one per <td> - whose pseudo-element rule's `content` is under test. The <style> tag's own
        // raw CSS text (unbroken for the length of a data: URI payload) must never be treated as
        // measurable content by table column-width measurement, regardless of the tag's display:none.
        private static string TableHtml(string content) =>
            "<!DOCTYPE html><html><body>" +
            "<table><tr><td>" +
            $"<style>.x::before {{ content: {content}; display: inline-block; width: 32px; height: 32px; }}</style>" +
            "<div class=\"x\"></div>" +
            "</td></tr></table>" +
            "</body></html>";

        private static async Task<double> MeasureActualWidth(string html, double pageWidth)
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            container.PageSize = new RSize(pageWidth, 800);
            // Mirrors PdfGenerator's ShrinkToFit measurement pass: width-constrained, height open
            // (src/PeachPDF/PdfGenerator.cs: container.MaxSize = new XSize(container.PageSize.Width, 0)).
            container.MaxSize = new RSize(pageWidth, 0);

            var measure = XGraphics.CreateMeasureContext(new XSize(pageWidth, 800), XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            return container.ActualSize.Width;
        }

        [Fact]
        public async Task NestedStyleTag_RealUrlImage_DoesNotInflateActualWidth()
        {
            const double pageWidth = 595;
            var actualWidth = await MeasureActualWidth(TableHtml($"url('{SvgDataUri()}')"), pageWidth);

            Assert.True(actualWidth <= pageWidth + 50,
                $"Expected ActualSize.Width to stay near the page width ({pageWidth}pt), but was {actualWidth}pt - " +
                "the <style> tag's own hidden CSS text was measured as visible content.");
        }

        [Fact]
        public async Task NestedStyleTag_Gradient_DoesNotInflateActualWidth()
        {
            const double pageWidth = 595;
            var actualWidth = await MeasureActualWidth(TableHtml("linear-gradient(to right, red, blue)"), pageWidth);

            Assert.True(actualWidth <= pageWidth + 50);
        }

        [Fact]
        public async Task NestedStyleTag_MissingUrl_DoesNotInflateActualWidth()
        {
            const double pageWidth = 595;
            var actualWidth = await MeasureActualWidth(TableHtml("url('nonexistent.png')"), pageWidth);

            Assert.True(actualWidth <= pageWidth + 50);
        }
    }
}
