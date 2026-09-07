using PeachPDF;
using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.PdfSharpCore;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
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
        public async Task FixedAttachment_ViewportReflectsThePerSlotPageClipOverride_NotTheBasePageBoxRect()
        {
            // Issue #146: FragmentPainter.PaintBackground's viewportRect (the positioning area a
            // background-attachment:fixed layer resolves its position against) must prefer
            // HtmlContainerInt.PageClipOverride - the per-slot window PdfGenerator.AddPdfPages sets
            // before painting each page, already used the same way by FragmentPainter's own PushClip
            // and PdfGenerator.HandleLinks - over the single, page-independent PageBoxRect. Isolated at
            // the RGraphics boundary (RecordingGraphics.DrawnImageRects), not by parsing PDF content
            // streams: PageClipOverride is set directly, sidestepping PdfGenerator.AddPdfPages' own
            // per-page paint-time translate entirely, since that translate is a separate, orthogonal
            // mechanism (correcting WHERE the whole page's content lands physically) from this ("what
            // rect does a percentage position resolve against").
            // No PageClipOverride: falls back to the base PageBoxRect - here (0, 0, 500, 500), since
            // MarginLeft/Top/Right are all 0 and PageSize is 500x500. "100% 100%" with a 10x10 image
            // lands its top-left corner at the far corner minus the image size: (490, 490).
            var withoutOverride = await PaintFixedBackground(pageClipOverride: null);
            Assert.Single(withoutOverride.DrawnImageRects);
            Assert.Equal(490, withoutOverride.DrawnImageRects[0].X, 0.01);
            Assert.Equal(490, withoutOverride.DrawnImageRects[0].Y, 0.01);

            // A per-slot override - same origin as the div's own content (PageClipOverride is also the
            // page's content clip, so shifting the origin away would clip the div itself out entirely),
            // but a genuinely smaller window, as a `:first`/margin-overridden page's own content band
            // could be - must be what "100% 100%" resolves against instead: (0, 0, 200, 200) puts the
            // far corner at (200, 200), so the image's top-left lands at (190, 190).
            var withOverride = await PaintFixedBackground(pageClipOverride: new RRect(0, 0, 200, 200));
            Assert.Single(withOverride.DrawnImageRects);
            Assert.Equal(190, withOverride.DrawnImageRects[0].X, 0.01);
            Assert.Equal(190, withOverride.DrawnImageRects[0].Y, 0.01);
        }

        private static async Task<RecordingGraphics> PaintFixedBackground(RRect? pageClipOverride)
        {
            var html = "<!DOCTYPE html><html><body>"
                + "<div id='bg' style=\"width:20pt;height:20pt;"
                + "background-image:url(" + PngDataUri + ");background-repeat:no-repeat;"
                + "background-attachment:fixed;background-position:100% 100%;background-size:10pt 10pt;\">"
                + "</div></body></html>";

            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            container.PageSize = new RSize(500, 500);
            container.Location = new RPoint(0, 0);
            container.MaxSize = new RSize(500, 0);

            var measure = XGraphics.CreateMeasureContext(new XSize(500, 500), XGraphicsUnit.Point, XPageDirection.Downwards);
            using (var measureGraphics = new GraphicsAdapter(adapter, measure, 1.0))
            {
                await container.PerformLayout(measureGraphics);
            }

            container.PageClipOverride = pageClipOverride;

            var recorder = new RecordingGraphics(adapter);
            FragmentPaintHarness.PaintPage(container, recorder);
            return recorder;
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
