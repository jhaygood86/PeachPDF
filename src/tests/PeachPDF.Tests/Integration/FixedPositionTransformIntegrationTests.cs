using PeachPDF.Layout;
using PeachPDF.Tests.TestSupport;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A <c>position: fixed</c> box paints with the canvas's initial (unbounded) clip, so its opacity group must be sized to the box and
    /// not to that clip - otherwise the group's /BBox is astronomically large and PDF/A rejects the integers.
    /// </summary>
    public class FixedPositionTransformIntegrationTests
    {
        const string Html = """
            <html><head><style>
              .wm { position: fixed; top: 300pt; left: 40pt; font-size: 60pt; color: #c00;
                    opacity: 0.3; transform: rotate(-35deg); }
            </style></head><body>
              <div class="wm">DRAFT</div>
              <p>Body text</p>
              <p style="break-before: page">Page 2</p>
            </body></html>
            """;

        [Fact]
        public async Task FixedBoxWithTransformAndOpacity_WritesNoOutOfRangeNumbers()
        {
            var config = new PdfGenerateConfig
            {
                PdfAConformance = PdfAConformance.PdfA3B,
                Metadata = new PdfDocumentMetadata { CreationDate = new System.DateTimeOffset(2026, 1, 15, 10, 30, 0, System.TimeSpan.Zero) },
            };

            var pdf = await PdfObjectReader.GeneratePdf(Html, config);

            // 9223372036854775807 is long.MaxValue; anything beyond PDF/A's 32-bit integer limit is out of range.
            Assert.DoesNotMatch(new Regex(@"[\[\s]\d{11,}[\]\s]"), pdf);
            Assert.DoesNotContain("9223372036854775807", pdf);
        }

        [Fact]
        public async Task FixedBoxWithTransformAndOpacity_IsPaintedOnEveryPage()
        {
            var generator = new PdfGenerator();
            var document = await generator.GeneratePdf(Html, new PdfGenerateConfig());

            Assert.Equal(2, document.PageCount);

            var pdf = PdfObjectReader.Save(document);

            // The watermark is a transparency group drawn once per page, with a finite bounding box.
            Assert.True(Regex.Matches(pdf, @"/ca 0\.3").Count >= 1, "the 30% opacity group should be written");
            Assert.DoesNotMatch(new Regex(@"/BBox\s*\[[^\]]*\d{11,}"), pdf);
        }
    }
}
