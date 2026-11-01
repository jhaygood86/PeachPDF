using PeachPDF;
using System;
using System.Threading.Tasks;
using PeachBrackets;
using PeachBrackets.Building;
using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Utils;
using PeachDrawing.PDF;
using PeachPDF.Layout;
using PeachPDF.Rendering;

namespace PeachBrackets.Engine.Tests.TestSupport
{
    /// <summary>
    /// Builds and lays out exactly one declarative page - mirroring <c>PdfGenerator.AddDeclarativePage</c> up to (but not including) writing the PDF -
    /// and returns the resulting root box plus the document it was laid out in, for geometry and resolved-style assertions.
    /// </summary>
    internal static class DeclarativeHarness
    {
        public static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayoutAsync(
            Action<IPageDescriptor> pageHandler, PeachPdfCssContent? stylesheet = null, PdfGenerateConfig? config = null,
            Action<HtmlDocumentOptions>? adjustOptions = null)
        {
            var (_, root, container) = await BuildAsync(pageHandler, stylesheet, config, adjustOptions);
            return (root, container);
        }

        public static async Task<(PageDescriptorBuilder descriptor, CssBox root, HtmlContainerInt container)> BuildAsync(
            Action<IPageDescriptor> pageHandler, PeachPdfCssContent? stylesheet = null, PdfGenerateConfig? config = null,
            Action<HtmlDocumentOptions>? adjustOptions = null)
        {
            // Mirrors PdfGenerator.AddPages's own declarative-specific default (A4/20pt margins) - PdfGenerateConfig's bare field defaults are
            // PageSize.Undefined/0pt margins, which would otherwise silently build a 0x0-point page.
            config ??= new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                MarginTop = 20,
                MarginBottom = 20,
                MarginLeft = 20,
                MarginRight = 20
            };

            var renderContext = new HtmlRenderContext { PixelsPerPoint = 1.0 };
            var context = new DocumentBuildContext(renderContext);
            var descriptor = DocumentBuilder.BuildPage(pageHandler, context);

            var sheet = descriptor.PageSizeOverride
                ?? (config.PageSize != PageSize.Undefined
                    ? PageSizeConverter.ToSize(config.PageSize)
                    : new PeachDrawing.Core.Size(config.ManualPageWidth, config.ManualPageHeight));

            var options = new HtmlDocumentOptions
            {
                PageWidthPoints = sheet.Width,
                PageHeightPoints = sheet.Height,
                MarginTopPoints = descriptor.MarginTopOverride ?? config.MarginTop,
                MarginBottomPoints = descriptor.MarginBottomOverride ?? config.MarginBottom,
                MarginLeftPoints = descriptor.MarginLeftOverride ?? config.MarginLeft,
                MarginRightPoints = descriptor.MarginRightOverride ?? config.MarginRight,
                DefaultLanguage = config.DefaultLanguage,
            };

            adjustOptions?.Invoke(options);
            var document = await HtmlDocument.CreateAsync(descriptor.Page, options, stylesheet?.StyleSheet);
            var contentArea = document.ContentArea;
            using var measure = PdfCanvas.CreateMeasuring(renderContext, contentArea.Width, contentArea.Height);
            await document.LayoutAsync(measure);

            var container = document.Container;
            return (descriptor, container.Root!, container);
        }
    }
}
