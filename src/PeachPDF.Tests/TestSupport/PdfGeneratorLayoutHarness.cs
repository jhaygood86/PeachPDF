using PeachPDF.Rendering;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachBrackets;
using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Engine.Drawing;
using System.Threading.Tasks;

namespace PeachPDF.Tests.TestSupport
{
    /// <summary>
    /// Lays a document out the way <see cref="PdfGenerator.AddPdfPages"/> does, and hands back the
    /// laid-out box tree plus the container, so a test can assert on the fragment tree rather than on a
    /// content stream.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This exists because <see cref="LayoutHarness"/> is not the production path, and the difference
    /// has hidden a defect.</b> That harness calls <c>SetHtml</c> exactly once, never sees an
    /// <c>@page</c> rule, and hand-computes the content band. The generator resolves <c>@page</c> during
    /// that same <c>SetHtml</c> call: <c>DomParser.CascadeApplyPageStyles</c> corrects the container's
    /// page size and margins in place, before the expensive cascade and box-tree correction passes run,
    /// whenever the rule's page size differs from the configured one (issue #582) — so a document with
    /// an <c>@page</c> rule still takes a materially different code path than <see cref="LayoutHarness"/>
    /// models at all. <see href="https://github.com/jhaygood86/PeachPDF/issues/439">#439</see> survived
    /// two green harness tests and was visible only through this path.
    /// </para>
    /// <para>
    /// Deliberately not the whole of <c>AddPdfPages</c>, and a fixture that needs any of the following
    /// will diverge from production silently rather than loudly: no PDF is written and no metadata
    /// applied; the <c>ScaleToPageSize</c>/<c>ShrinkToFit</c> third <c>SetContent</c> is not modelled (it
    /// re-enters after a measuring layout, which is a different question and belongs to whichever test
    /// needs it); <c>PdfGenerateConfig.NetworkLoader</c> and <c>AllowLocalFileAccess</c> are not applied
    /// to the adapter, so a fixture with a relative image URL or local file access gets the adapter's
    /// default <c>DataUriNetworkLoader</c>; and <c>html</c> is never fetched from the loader. Add the
    /// missing piece here rather than working around it in a test — the point of this type is that it is
    /// the production path.
    /// </para>
    /// <para>
    /// The container is <b>not</b> disposed, exactly as <see cref="LayoutHarness"/> does not dispose its
    /// own: <c>HtmlContainerInt.Dispose</c> nulls <c>Root</c> and <c>CssData</c> and disposes every
    /// <c>CssImage</c> in the tree, so a disposed container hands back a null box tree and a fragment tree
    /// whose images are gone.
    /// </para>
    /// </remarks>
    internal static class PdfGeneratorLayoutHarness
    {
        /// <summary>
        /// Lays <paramref name="html"/> out for <paramref name="config"/>, mirroring
        /// <c>PdfGenerator.AddPdfPages</c>' page-size resolution (including the in-place <c>@page</c>
        /// correction done while the document loads) and its <c>MaxSize</c>/layout pair.
        /// </summary>
        /// <param name="html">the document to lay out</param>
        /// <param name="config">the generator configuration whose page size and margins apply</param>
        internal static async Task<(CssBox Root, HtmlContainerInt Container)> LayoutAsync(
            string html, PdfGenerateConfig config, Func<HtmlRenderContext, Task>? configureRenderContext = null)
        {
            var renderContext = new HtmlRenderContext { PixelsPerPoint = config.PixelsPerInch / 72d };

            if (configureRenderContext is not null)
            {
                await configureRenderContext(renderContext);
            }

            var document = await HtmlDocument.LoadAsync(html, renderContext, PdfGenerator.ToDocumentOptions(config, SheetSize(config)));

            var contentArea = document.ContentArea;
            using var measure = PdfCanvas.CreateMeasuring(renderContext, contentArea.Width, contentArea.Height);
            await document.LayoutAsync(measure);

            return (document.Container.Root!, document.Container);
        }

        /// <summary>
        /// Lays <paramref name="html"/> out exactly like <see cref="LayoutAsync"/>, but also models the
        /// <c>ScaleToPageSize</c>/<c>ShrinkToFit</c> arm <see cref="LayoutAsync"/>'s own doc comment says
        /// it deliberately skips: the measuring layout pass, and the reload that <c>PdfGenerator.AddPdfPages</c>
        /// only runs when <see cref="PdfGenerator.NeedsRescale"/> says the measured content actually needs a
        /// different <c>PixelsPerPoint</c> than the one the measuring pass already used. Returns whether that
        /// second pass ran, so a test can assert directly that ordinary non-overflowing content skips it (the
        /// common case a caller pays for on every <c>ShrinkToFit</c> render otherwise) without duplicating
        /// <c>AddPdfPages</c>' own decision inside the assertion itself.
        /// </summary>
        internal static async Task<(CssBox Root, HtmlContainerInt Container, bool Rescaled, double PixelsPerPoint)> LayoutWithRescaleAsync(
            string html, PdfGenerateConfig config)
        {
            var renderContext = new HtmlRenderContext { PixelsPerPoint = config.PixelsPerInch / 72d };
            var options = PdfGenerator.ToDocumentOptions(config, SheetSize(config));

            var document = await HtmlDocument.LoadAsync(html, renderContext, options);
            var contentArea = document.ContentArea;
            var measure = PdfCanvas.CreateMeasuring(renderContext, contentArea.Width, contentArea.Height);

            var basePixelsPerPoint = config.PixelsPerInch / 72d;
            var minPixelsPerPoint = config.MinContentWidth > 0
                ? config.MinContentWidth / contentArea.Width
                : basePixelsPerPoint;
            var pixelsPerPoint = minPixelsPerPoint;
            var needsRescale = true;

            if (config.ScaleToPageSize || config.ShrinkToFit)
            {
                await document.LayoutAsync(measure);

                var actualWidth = document.ContentExtent.Width;
                var candidatePixelsPerPoint = pixelsPerPoint * (actualWidth / contentArea.Width);

                if (candidatePixelsPerPoint < minPixelsPerPoint)
                {
                    candidatePixelsPerPoint = minPixelsPerPoint;
                }

                var effectivePixelsPerPoint = (config.ShrinkToFit && candidatePixelsPerPoint > 1) || config.ScaleToPageSize
                    ? candidatePixelsPerPoint
                    : renderContext.PixelsPerPoint;

                needsRescale = PdfGenerator.NeedsRescale(renderContext.PixelsPerPoint, effectivePixelsPerPoint);

                if (needsRescale)
                {
                    renderContext.ClearFontCache();
                    renderContext.PixelsPerPoint = effectivePixelsPerPoint;

                    document.Dispose();
                    document = await HtmlDocument.LoadAsync(html, renderContext, options);

                    measure.Dispose();
                    contentArea = document.ContentArea;
                    measure = PdfCanvas.CreateMeasuring(renderContext, contentArea.Width, contentArea.Height);
                }
            }

            if (needsRescale)
            {
                await document.LayoutAsync(measure);
            }

            measure.Dispose();

            return (document.Container.Root!, document.Container, needsRescale, renderContext.PixelsPerPoint);
        }

        internal static HtmlDocumentOptions ZeroMargins() => new()
        {
            PageWidthPoints = 595,
            PageHeightPoints = 842,
            MarginTopPoints = 0,
            MarginRightPoints = 0,
            MarginBottomPoints = 0,
            MarginLeftPoints = 0,
        };

        /// <summary>The top margin the document resolved, in true CSS points whatever the render context's layout scale.</summary>
        internal static double MarginTopPoints(HtmlDocument document, HtmlRenderContext renderContext) =>
            document.Container.MarginTop / renderContext.PixelsPerPoint;

        private static Size SheetSize(PdfGenerateConfig config)
        {
            var sheet = config.PageSize != PageSize.Undefined
                ? PageSizeConverter.ToSize(config.PageSize)
                : new Size(config.ManualPageWidth, config.ManualPageHeight);

            return config.PageOrientation == PageOrientation.Landscape ? new Size(sheet.Height, sheet.Width) : sheet;
        }
    }
}
