using PeachPDF;
using PeachPDF.PdfSharpCore;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Coverage for real page-relative <c>background-attachment: fixed</c> semantics (CSS Backgrounds 3
    /// §3.9), added to close a documented accepted gap ("no visual effect possible in a static PDF")
    /// that turned out to have a well-defined paginated-media meaning: a fixed-attachment background's
    /// positioning area is the page/viewport box, not the element's own box - exactly the model
    /// <c>position: fixed</c> already uses (both ignore <see cref="Html.Core.HtmlContainerInt.ScrollOffset"/>).
    /// This is directly exercised by several "trap" declarations in the real Acid2 fixture.
    ///
    /// Asserted structurally per this repo's painting-test convention: the <c>cm</c> matrix
    /// immediately preceding the image's <c>Do</c> operator (adjacency, not bare substring presence)
    /// is compared between two documents whose box has a *different* margin-top - for `fixed`, the
    /// resolved image position must be identical in both (viewport-anchored, box-position-independent);
    /// for the default `scroll`, it must differ (element-anchored, moves with the box).
    /// </summary>
    public class BackgroundAttachmentFixedIntegrationTests
    {
        // A real 1x1 yellow-pixel PNG data URI (also used by the Acid2 fixture itself).
        private const string PngDataUri =
            "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR42mP4/58BAAT/Af9jgNErAAAAAElFTkSuQmCC";

        private static readonly Regex CmBeforeDo = new(@"q ([-\d.]+) ([-\d.]+) ([-\d.]+) ([-\d.]+) ([-\d.]+) ([-\d.]+) cm /I0 Do", RegexOptions.Compiled);

        [Fact]
        public async Task FixedAttachment_ImagePosition_IsIndependentOfBoxMargin()
        {
            var matrixAtMargin50 = await GetImageCmMatrix(marginTop: 50, attachment: "fixed");
            var matrixAtMargin20 = await GetImageCmMatrix(marginTop: 20, attachment: "fixed");

            Assert.Equal(matrixAtMargin50, matrixAtMargin20);
        }

        [Fact]
        public async Task ScrollAttachment_ImagePosition_MovesWithBoxMargin()
        {
            var matrixAtMargin50 = await GetImageCmMatrix(marginTop: 50, attachment: "scroll");
            var matrixAtMargin20 = await GetImageCmMatrix(marginTop: 20, attachment: "scroll");

            Assert.NotEqual(matrixAtMargin50, matrixAtMargin20);
        }

        [Fact]
        public async Task DefaultAttachment_BehavesLikeScroll()
        {
            // No background-attachment declared at all - must default to "scroll" (unchanged legacy
            // behavior), not silently inherit/leak a "fixed" default.
            var matrixDefault = await GetImageCmMatrix(marginTop: 50, attachment: null);
            var matrixScroll = await GetImageCmMatrix(marginTop: 50, attachment: "scroll");

            Assert.Equal(matrixScroll, matrixDefault);
        }

        private static async Task<string> GetImageCmMatrix(double marginTop, string? attachment)
        {
            var attachmentDecl = attachment is null ? "" : " " + attachment;
            var html = "<!DOCTYPE html><html><head><style>"
                + "@page { size: 200pt 200pt; margin: 0 }"
                + "body { margin: 0 }"
                + ".box { margin-top: " + marginTop.ToString(System.Globalization.CultureInfo.InvariantCulture) + "pt; margin-left: 30pt; width: 60pt; height: 60pt;"
                + "  background: url(" + PngDataUri + ") no-repeat" + attachmentDecl + " 0 0; }"
                + "</style></head><body><div class=\"box\"></div></body></html>";

            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            var pdfText = Encoding.Latin1.GetString(ms.ToArray());

            var match = CmBeforeDo.Match(pdfText);
            Assert.True(match.Success, "Expected a 'cm ... /I0 Do' image draw in the content stream.");
            return match.Value;
        }
    }
}
