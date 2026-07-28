using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore;
using PeachPDF.PdfSharpCore.Drawing;
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
    /// <c>SetHtml</c> and then, when the rule's page size differs from the configured one, throws the whole
    /// box tree away and lays the document out again against the CSS size — so a document with an
    /// <c>@page</c> rule is parsed twice and every once-per-layout decision is taken twice.
    /// <see href="https://github.com/jhaygood86/PeachPDF/issues/439">#439</see> survived two green harness
    /// tests and was visible only through this path.
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
        /// <c>PdfGenerator.AddPdfPages</c>' page-size resolution, its <c>@page</c> re-layout, and its
        /// <c>MaxSize</c>/<c>PerformLayout</c> pair.
        /// </summary>
        /// <param name="html">the document to lay out</param>
        /// <param name="config">the generator configuration whose page size and margins apply</param>
        internal static async Task<(CssBox Root, HtmlContainerInt Container)> LayoutAsync(
            string html, PdfGenerateConfig config)
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = config.PixelsPerInch / 72d };

            var orgPageSize = config.PageSize != PageSize.Undefined
                ? PageSizeConverter.ToSize(config.PageSize)
                : new XSize(config.ManualPageWidth, config.ManualPageHeight);

            if (config.PageOrientation == PageOrientation.Landscape)
            {
                orgPageSize = new XSize(orgPageSize.Height, orgPageSize.Width);
            }

            var container = new HtmlContainer(adapter);

            await PdfGenerator.SetContent(container, config, html, null, orgPageSize);

            // The @page arm: the CSS size wins, and the document is parsed and laid out a second time.
            if (container.CssPageSize.HasValue && container.CssPageSize.Value != orgPageSize)
            {
                await PdfGenerator.SetContent(container, config, html, null, container.CssPageSize.Value);
            }

            using var measure = XGraphics.CreateMeasureContext(
                container.PageSize, XGraphicsUnit.Point, XPageDirection.Downwards);

            container.MaxSize = new XSize(container.PageSize.Width, 0);

            await container.PerformLayout(measure);

            return (container.HtmlContainerInt.Root!, container.HtmlContainerInt);
        }
    }
}
