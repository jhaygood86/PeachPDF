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
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Utilities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Org.BouncyCastle.Bcpg.OpenPgp;

namespace PeachPDF
{
    /// <summary>
    /// Low level handling of Html Renderer logic, this class is used by <see cref="PdfGenerator"/>.
    /// </summary>
    /// <seealso cref="HtmlContainerInt"/>
    public sealed class HtmlContainer : IDisposable
    {
        #region Fields and Consts

        /// <summary>
        /// The internal core html container
        /// </summary>
        private readonly HtmlContainerInt _htmlContainerInt;

        #endregion


        /// <summary>
        /// Init.
        /// </summary>
        internal HtmlContainer(PdfSharpAdapter adapter)
        {
            _htmlContainerInt = new HtmlContainerInt(adapter);
        }

        /// <summary>
        /// The internal core html container
        /// </summary>
        internal HtmlContainerInt HtmlContainerInt => _htmlContainerInt;

        /// <summary>
        /// The scroll offset of the html.<br/>
        /// This will adjust the rendered html by the given offset so the content will be "scrolled".<br/>
        /// </summary>
        /// <example>
        /// Element that is rendered at location (50,100) with offset of (0,200) will not be rendered as it
        /// will be at -100 therefore outside the client rectangle.
        /// </example>
        public XPoint ScrollOffset
        {
            get => Utils.Convert(_htmlContainerInt.ScrollOffset, PixelsPerPoint);
            set => _htmlContainerInt.ScrollOffset = Utils.Convert(value, PixelsPerPoint);
        }

        /// <summary>
        /// The top-left most location of the rendered html.<br/>
        /// This will offset the top-left corner of the rendered html.
        /// </summary>
        public XPoint Location
        {
            get => Utils.Convert(_htmlContainerInt.Location, PixelsPerPoint);
            set => _htmlContainerInt.Location = Utils.Convert(value, PixelsPerPoint);
        }

        /// <summary>
        /// The max width and height of the rendered html.<br/>
        /// The max width will effect the html layout wrapping lines, resize images and tables where possible.<br/>
        /// The max height does NOT effect layout, but will not render outside it (clip).<br/>
        /// <see cref="ActualSize"/> can be exceed the max size by layout restrictions (unwrappable line, set image size, etc.).<br/>
        /// Set zero for unlimited (width\height separately).<br/>
        /// </summary>
        public XSize MaxSize
        {
            get => Utils.Convert(_htmlContainerInt.MaxSize, PixelsPerPoint);
            set => _htmlContainerInt.MaxSize = Utils.Convert(value, PixelsPerPoint);
        }

        /// <summary>
        /// The actual size of the rendered html (after layout)
        /// </summary>
        public XSize ActualSize
        {
            get => Utils.Convert(_htmlContainerInt.ActualSize, PixelsPerPoint);
            internal set => _htmlContainerInt.ActualSize = Utils.Convert(value, PixelsPerPoint);
        }

        public XSize PageSize {
            get => Utils.Convert(_htmlContainerInt.PageSize, PixelsPerPoint);
            set => _htmlContainerInt.PageSize = Utils.Convert(value, PixelsPerPoint);
        }

        /// <summary>
        /// the top margin between the page start and the text
        /// </summary>
        public double MarginTop
        {
            get => _htmlContainerInt.MarginTop / PixelsPerPoint;
            set
            {
                if (value > -1)
                    _htmlContainerInt.MarginTop = value * PixelsPerPoint;
            }
        }

        /// <summary>
        /// the bottom margin between the page end and the text
        /// </summary>
        public double MarginBottom
        {
            get => _htmlContainerInt.MarginBottom / PixelsPerPoint;
            set
            {
                if (value > -1)
                    _htmlContainerInt.MarginBottom = value * PixelsPerPoint;
            }
        }

        /// <summary>
        /// the left margin between the page start and the text
        /// </summary>
        public double MarginLeft
        {
            get => _htmlContainerInt.MarginLeft / PixelsPerPoint;
            set
            {
                if (value > -1)
                    _htmlContainerInt.MarginLeft = value * PixelsPerPoint;
            }
        }

        /// <summary>
        /// the right margin between the page end and the text
        /// </summary>
        public double MarginRight
        {
            get => _htmlContainerInt.MarginRight / PixelsPerPoint;
            set
            {
                if (value > -1)
                    _htmlContainerInt.MarginRight = value * PixelsPerPoint;
            }
        }

        internal double PixelsPerPoint => ((PdfSharpAdapter)_htmlContainerInt.Adapter).PixelsPerPoint;

        /// <summary>
        /// Init with optional document and stylesheet.
        /// </summary>
        /// <param name="htmlSource">the html to init with, init empty if not given</param>
        /// <param name="baseCssData">optional: the stylesheet to init with, init default if not given</param>
        public async Task SetHtml(string htmlSource, CssData baseCssData = null)
        {
            await _htmlContainerInt.SetHtml(htmlSource, baseCssData);
        }

        /// <summary>
        /// Get all the links in the HTML with the element rectangle and href data.
        /// </summary>
        /// <returns>collection of all the links in the HTML</returns>
        public List<LinkElementData<XRect>> GetLinks()
        {
            var linkElements = new List<LinkElementData<XRect>>();

            var baseElement = DomUtils.GetBoxByTagName(HtmlContainerInt.Root, "base");
            var baseUrl = "";

            if (baseElement is not null)
            {
                baseUrl = baseElement.HtmlTag.TryGetAttribute("href", "");
            }

            var baseUri = string.IsNullOrWhiteSpace(baseUrl) ? HtmlContainerInt.Adapter.BaseUri : new Uri(baseUrl);

            foreach (var link in HtmlContainerInt.GetLinks())
            {
                var href = link.Href.StartsWith('#') || baseUri is null ? link.Href : new Uri(baseUri, link.Href).AbsoluteUri;
                linkElements.Add(new LinkElementData<XRect>(link.Id, href, Utils.Convert(link.Rectangle, PixelsPerPoint)));
            }

            return linkElements;
        }

        /// <summary>
        /// Get the rectangle of html element as calculated by html layout.<br/>
        /// Element if found by id (id attribute on the html element).<br/>
        /// Note: to get the screen rectangle you need to adjust by the hosting control.<br/>
        /// </summary>
        /// <param name="elementId">the id of the element to get its rectangle</param>
        /// <returns>the rectangle of the element or null if not found</returns>
        public XRect? GetElementRectangle(string elementId)
        {
            var r = _htmlContainerInt.GetElementRectangle(elementId);
            return r.HasValue ? Utils.Convert(r.Value, PixelsPerPoint) : (XRect?)null;
        }

        /// <summary>
        /// Measures the bounds of box and children, recursively.
        /// </summary>
        /// <param name="g">Device context to draw</param>
        public async ValueTask PerformLayout(XGraphics g)
        {
            ArgumentNullException.ThrowIfNull(g);

            using var ig = new GraphicsAdapter(_htmlContainerInt.Adapter, g, PixelsPerPoint);
            await _htmlContainerInt.PerformLayout(ig);
        }

        /// <summary>
        /// Render the html using the given device.
        /// </summary>
        /// <param name="g">the device to use to render</param>
        public async ValueTask PerformPaint(XGraphics g)
        {
            ArgumentNullException.ThrowIfNull(g);

            using var ig = new GraphicsAdapter(_htmlContainerInt.Adapter, g, PixelsPerPoint);
            await _htmlContainerInt.PerformPaint(ig);
        }

        public void Dispose()
        {
            _htmlContainerInt.Dispose();
        }
    }
}