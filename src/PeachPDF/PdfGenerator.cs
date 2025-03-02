// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
// 
// - Sun Tsu,
// "The Art of War"

using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Network;
using PeachPDF.PdfSharpCore;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Pdf.Advanced;
using System.IO;
using System.Threading.Tasks;

namespace PeachPDF
{
    /// <summary>
    /// TODO:a add doc
    /// </summary>
    public class PdfGenerator
    {
        private readonly PdfSharpAdapter _pdfSharpAdapter = new();

        /// <summary>
        /// Adds a font mapping from <paramref name="fromFamily"/> to <paramref name="toFamily"/> iff the <paramref name="fromFamily"/> is not found.<br/>
        /// When the <paramref name="fromFamily"/> font is used in rendered html and is not found in existing 
        /// fonts (installed or added) it will be replaced by <paramref name="toFamily"/>.<br/>
        /// </summary>
        /// <remarks>
        /// This fonts mapping can be used as a fallback in case the requested font is not installed in the client system.
        /// </remarks>
        /// <param name="fromFamily">the font family to replace</param>
        /// <param name="toFamily">the font family to replace with</param>
        public void AddFontFamilyMapping(string fromFamily, string toFamily)
        {
            ArgChecker.AssertArgNotNullOrEmpty(fromFamily, "fromFamily");
            ArgChecker.AssertArgNotNullOrEmpty(toFamily, "toFamily");

            _pdfSharpAdapter.AddFontFamilyMapping(fromFamily, toFamily);
        }

        /// <summary>
        /// Add a font to be rendered
        /// </summary>
        /// <param name="stream">Font stream</param>
        public async Task AddFontFromStream(Stream stream)
        {
            await _pdfSharpAdapter.AddFont(stream, null);
        }

        /// <summary>
        /// Parse the given stylesheet to <see cref="CssData"/> object.<br/>
        /// If <paramref name="combineWithDefault"/> is true the parsed css blocks are added to the 
        /// default css data (as defined by W3), merged if class name already exists. If false only the data in the given stylesheet is returned.
        /// </summary>
        /// <seealso cref="http://www.w3.org/TR/CSS21/sample.html"/>
        /// <param name="stylesheet">the stylesheet source to parse</param>
        /// <param name="combineWithDefault">true - combine the parsed css data with default css data, false - return only the parsed css data</param>
        /// <returns>the parsed css data</returns>
        public async Task<CssData> ParseStyleSheet(string stylesheet, bool combineWithDefault = true)
        {
            return await CssData.Parse(_pdfSharpAdapter, stylesheet, combineWithDefault);
        }

        /// <summary>
        /// Create PDF document from given HTML.<br/>
        /// </summary>
        /// <param name="html">HTML source to create PDF from</param>
        /// <param name="pageSize">the page size to use for each page in the generated pdf </param>
        /// <param name="margin">the margin to use between the HTML and the edges of each page</param>
        /// <param name="cssData">optional: the style to use for html rendering (default - use W3 default style)</param>
        /// <returns>the generated image of the html</returns>
        public async Task<PdfDocument> GeneratePdf(string html, PageSize pageSize, int margin = 20, CssData? cssData = null)
        {
            var config = new PdfGenerateConfig
            {
                PageSize = pageSize
            };

            config.SetMargins(margin);

            return await GeneratePdf(html, config, cssData);
        }

        /// <summary>
        /// Create PDF document from given HTML.<br/>
        /// </summary>
        /// <param name="html">HTML source to create PDF from</param>
        /// <param name="config">the configuration to use for the PDF generation (page size/page orientation/margins/etc.)</param>
        /// <param name="cssData">optional: the style to use for html rendering (default - use W3 default style)</param>
        /// <returns>the generated image of the html</returns>
        public async Task<PdfDocument> GeneratePdf(string? html, PdfGenerateConfig config, CssData? cssData = null)
        {
            // create PDF document to render the HTML into
            var document = new PdfDocument();

            // add rendered PDF pages to document
            await AddPdfPages(document, html, config, cssData);

            return document;
        }

        /// <summary>
        /// Create PDF pages from given HTML and appends them to the provided PDF document.<br/>
        /// </summary>
        /// <param name="document">PDF document to append pages to</param>
        /// <param name="html">HTML source to create PDF from</param>
        /// <param name="pageSize">the page size to use for each page in the generated pdf </param>
        /// <param name="margin">the margin to use between the HTML and the edges of each page</param>
        /// <param name="cssData">optional: the style to use for html rendering (default - use W3 default style)</param>
        /// <returns>the generated image of the html</returns>
        public async Task AddPdfPages(PdfDocument document, string html, PageSize pageSize, int margin = 20, CssData? cssData = null)
        {
            var config = new PdfGenerateConfig
            {
                PageSize = pageSize
            };

            config.SetMargins(margin);

            await AddPdfPages(document, html, config, cssData);
        }

        /// <summary>
        /// Create PDF pages from given HTML and appends them to the provided PDF document.<br/>
        /// </summary>
        /// <param name="document">PDF document to append pages to</param>
        /// <param name="html">HTML source to create PDF from</param>
        /// <param name="config">the configuration to use for the PDF generation (page size/page orientation/margins/etc.)</param>
        /// <param name="cssData">optional: the style to use for html rendering (default - use W3 default style)</param>
        /// <returns>the generated image of the html</returns>
        public async Task AddPdfPages(PdfDocument document, string? html, PdfGenerateConfig config, CssData? cssData = null)
        {
            // get the size of each page to layout the HTML in
            var orgPageSize = config.PageSize != PageSize.Undefined ? PageSizeConverter.ToSize(config.PageSize) : config.ManualPageSize;

            if (config.PageOrientation == PageOrientation.Landscape)
            {
                // invert pagesize for landscape
                orgPageSize = new XSize(orgPageSize.Height, orgPageSize.Width);
            }

            if (string.IsNullOrEmpty(html) && config.NetworkLoader is null) return;

            _pdfSharpAdapter.NetworkLoader = config.NetworkLoader ?? new DataUriNetworkLoader();
            _pdfSharpAdapter.PixelsPerPoint = config.PixelsPerInch / 72d;

            html ??= await _pdfSharpAdapter.NetworkLoader.GetPrimaryContents();

            using var container = new HtmlContainer(_pdfSharpAdapter);

            await SetContent(container, config, html, cssData, orgPageSize);

            var measure = XGraphics.CreateMeasureContext(container.PageSize, XGraphicsUnit.Point, XPageDirection.Downwards);

            var basePixelsPerPoint = config.PixelsPerInch / 72d;
            var minPixelsPerPoint = config.MinContentWidth > 0 ? config.MinContentWidth / container.PageSize.Width : basePixelsPerPoint;
            var pixelsPerPoint = minPixelsPerPoint;

            if (config.ScaleToPageSize || config.ShrinkToFit)
            {
                container.MaxSize = new XSize(container.PageSize.Width, 0);
                await container.PerformLayout(measure);

                var actualWidth = container.ActualSize.Width;

                _pdfSharpAdapter.ClearFontCache();
                pixelsPerPoint *= (actualWidth / container.PageSize.Width);

                if (pixelsPerPoint < minPixelsPerPoint)
                {
                    pixelsPerPoint = minPixelsPerPoint;
                }

                _pdfSharpAdapter.PixelsPerPoint = (config.ShrinkToFit && pixelsPerPoint > 1) || config.ScaleToPageSize
                    ? pixelsPerPoint
                    : _pdfSharpAdapter.PixelsPerPoint;

                await SetContent(container, config, html, cssData, orgPageSize);

                measure?.Dispose();
                measure = XGraphics.CreateMeasureContext(container.PageSize, XGraphicsUnit.Point, XPageDirection.Downwards);
            }

            container.MaxSize = new XSize(container.PageSize.Width, 0);

            // layout the HTML with the page width restriction to know how many pages are required
            await container.PerformLayout(measure);

            // while there is un-rendered HTML, create another PDF page and render with proper offset for the next page
            double scrollOffset = 0;
            while (scrollOffset > -container.ActualSize.Height)
            {
                var page = document.AddPage();
                page.Height = orgPageSize.Height;
                page.Width = orgPageSize.Width;

                using var g = XGraphics.FromPdfPage(page);
                g.IntersectClip(new XRect(0, 0, page.Width, page.Height));

                container.ScrollOffset = new XPoint(0, scrollOffset);
                await container.PerformPaint(g);
                scrollOffset -= container.PageSize.Height;
            }

            // add web links and anchors
            HandleLinks(document, container, orgPageSize, container.PageSize);

            measure?.Dispose();
        }

        private static async Task SetContent(HtmlContainer container, PdfGenerateConfig config, string html, CssData? cssData, XSize orgPageSize)
        {
            container.MarginBottom = config.MarginBottom;
            container.MarginLeft = config.MarginLeft;
            container.MarginRight = config.MarginRight;
            container.MarginTop = config.MarginTop;

            await container.SetHtml(html, cssData);

            // Just in case @page rules got applied
            var pageSize = new XSize(orgPageSize.Width - container.MarginLeft - container.MarginRight, orgPageSize.Height - container.MarginTop - container.MarginBottom);
            container.PageSize = pageSize;
            container.Location = new XPoint(container.MarginLeft, container.MarginTop);
        }


        #region Private/Protected methods

        /// <summary>
        /// Handle HTML links by create PDF Documents link either to external URL or to another page in the document.
        /// </summary>
        private static void HandleLinks(PdfDocument document, HtmlContainer container, XSize orgPageSize, XSize pageSize)
        {
            foreach (var link in container.GetLinks())
            {
                var i = (int)(link.Rectangle.Top / pageSize.Height);
                for (; i < document.Pages.Count && pageSize.Height * i < link.Rectangle.Bottom; i++)
                {
                    var offset = pageSize.Height * i;

                    // position is from the bottom of the page
                    var xRect = new XRect(link.Rectangle.Left, orgPageSize.Height - (link.Rectangle.Height + link.Rectangle.Top - offset), link.Rectangle.Width, link.Rectangle.Height);

                    if (link.IsAnchor)
                    {
                        // create link to another page in the document
                        var anchorRect = container.GetElementRectangle(link.AnchorId);

                        if (!anchorRect.HasValue) continue;

                        var anchorPageNumber = 1;
                        var top = anchorRect.Value.Top;

                        while (top > pageSize.Height)
                        {
                            top -= pageSize.Height;
                            anchorPageNumber++;
                        }

                        document.AddNamedDestination(link.AnchorId, anchorPageNumber, PdfNamedDestinationParameters.CreateFitVertically(top));
                        document.Pages[i].AddDocumentLink(new PdfRectangle(xRect), link.AnchorId);
                    }
                    else
                    {
                        // create link to URL
                        document.Pages[i].AddWebLink(new PdfRectangle(xRect), link.Href);
                    }
                }
            }
        }

        #endregion
    }
}