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

using MimeKit;
using PeachPDF;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Network;
using PeachPDF.Svg;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace PeachPDF.Html.Core.Handlers
{
    /// <summary>
    /// Handler for all loading image logic.<br/>
    /// <p>
    /// Loading by file path.<br/>
    /// Loading by URI.<br/>
    /// </p>
    /// </summary>
    /// <remarks>
    /// <para>
    /// Supports sync and async image loading.
    /// </para>
    /// <para>
    /// If the image object is created by the handler on calling dispose of the handler the image will be released, this
    /// makes release of unused images faster as they can be large.<br/>
    /// Disposing image load handler will also cancel download of image from the web.
    /// </para>
    /// </remarks>
    internal sealed class ImageLoadHandler : IDisposable
    {
        #region Fields and Consts

        /// <summary>
        /// the container of the html to handle load image for
        /// </summary>
        private readonly HtmlContainerInt _htmlContainer;

        /// <summary>
        /// The resource stream the image was decoded from; disposed when the handler is released.
        /// </summary>
        private Stream? _imageStream;

        /// <summary>
        /// flag to indicate if to release the image object on box dispose (only if image was loaded by the box)
        /// </summary>
        private bool _releaseImageObject;

        /// <summary>
        /// is the handler has been disposed
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// True if the src's file extension suggests an SVG image - computed once up front from the
        /// original source string, before any URI resolution, and reused across the file/network paths.
        /// </summary>
        private bool _srcHintsSvg;

        #endregion


        /// <summary>
        /// Init.
        /// </summary>
        /// <param name="htmlContainer">the container of the html to handle load image for</param>
        public ImageLoadHandler(HtmlContainerInt htmlContainer)
        {
            ArgumentNullException.ThrowIfNull(htmlContainer);

            _htmlContainer = htmlContainer;
        }

        /// <summary>
        /// the image instance of the loaded image
        /// </summary>
        public RImage? Image { get; private set; }

        /// <summary>
        /// the parsed SVG scene graph, set instead of <see cref="Image"/> when the source was detected
        /// to be an SVG image (by file extension or <c>Content-Type: image/svg+xml</c>).
        /// </summary>
        public SvgDocument? SvgDocument { get; private set; }

        /// <summary>
        /// Set image of this image box by analyzing the src attribute.<br/>
        /// Load the image from inline base64 encoded string.<br/>
        /// Or from calling property/method on the bridge object that returns image or URL to image.<br/>
        /// Or from file path<br/>
        /// Or from URI.
        /// </summary>
        /// <remarks>
        /// File path and URI image loading is executed async and after finishing calling <see cref="ImageLoadComplete"/>
        /// on the main thread and not thread-pool.
        /// </remarks>
        /// <param name="src">the source of the image to load</param>
        /// <returns>the image object (null if failed)</returns>
        public async ValueTask LoadImage(string src)
        {
            try
            {
                if (!string.IsNullOrEmpty(src))
                {
                    _srcHintsSvg = src.Split('?', '#')[0].EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                        || src.StartsWith("data:image/svg+xml", StringComparison.OrdinalIgnoreCase);
                    await SetImageFromPath(src);
                }
                else
                {
                    ImageLoadComplete();
                }
            }
            catch (Exception ex)
            {
                ImageLoadComplete();
                _htmlContainer.ReportError(HtmlRenderErrorType.Image, "Exception in handling image source", ex);
            }
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            _disposed = true;
            ReleaseObjects();
        }


        #region Private methods

        /// <summary>
        /// Load image from path of image file or URL. The source is resolved to an absolute URI against
        /// the document base (a <c>&lt;base href&gt;</c> element or the adapter's base URI, which defaults
        /// to the current working directory as a <c>file:</c> URI) and then fetched through the network
        /// loader — local files are served by <see cref="Network.FileUriNetworkLoader"/> exactly like a
        /// remote resource, so there is no separate file-system code path.
        /// </summary>
        /// <param name="path">the file path or uri to load image from</param>
        private async ValueTask SetImageFromPath(string path)
        {
            var uri = CommonUtils.ResolveAgainstDocumentBase(_htmlContainer, path);

            if (uri is not { IsAbsoluteUri: true })
            {
                // An unresolvable source (e.g. a malformed URL) is skipped gracefully - a broken image
                // shows nothing rather than aborting the whole render - matching how a missing resource is
                // already handled downstream.
                ImageLoadComplete();
                return;
            }

            await SetImageFromUrl(uri);
        }

        private void LoadImageFromStream(Stream stream, bool isSvg)
        {
            if (isSvg)
            {
                LoadSvgFromStream(stream);
                return;
            }

            try
            {
                Image = _htmlContainer.Adapter.ImageFromStream(stream);
            }
            catch (InvalidOperationException)
            {
                Image = null;
            }
        }

        /// <summary>
        /// Parses an SVG image eagerly (unlike raster images, which <see cref="RAdapter.ImageFromStream"/>
        /// reads lazily) into <see cref="SvgDocument"/>, via a standalone XML parse
        /// (<see cref="XElementSvgSourceNode"/>) rather than the HTML tokenizer, since a fetched SVG
        /// resource is expected to be a standalone, well-formed XML document.
        /// </summary>
        private void LoadSvgFromStream(Stream stream)
        {
            try
            {
                var xdoc = XDocument.Load(stream);

                if (xdoc.Root is not null)
                {
                    SvgDocument = SvgTreeBuilder.Build(new XElementSvgSourceNode(xdoc.Root), _htmlContainer.Adapter);
                }
            }
            catch (XmlException ex)
            {
                SvgDocument = null;
                _htmlContainer.ReportError(HtmlRenderErrorType.Image, "Failed to parse SVG image", ex);
            }
        }

        /// <summary>
        /// Fetch the image from the given absolute URI through the network loader (which serves
        /// <c>file:</c>, <c>data:</c>, HTTP and archive resources uniformly) and decode it. SVG is
        /// detected from the source extension or a <c>Content-Type: image/svg+xml</c> response header.
        /// </summary>
        private async ValueTask SetImageFromUrl(RUri source)
        {
            var networkResponse = await _htmlContainer.Adapter.GetResourceStream(source);

            if (networkResponse?.ResourceStream is not null)
            {
                _imageStream = networkResponse.ResourceStream;

                var contentTypeHintsSvg = networkResponse.ResponseHeaders?.TryGetValue("Content-Type", out var contentTypeValues) == true
                    && contentTypeValues.Select(ContentType.Parse).Any(ct => ct.IsMimeType("image", "svg+xml"));

                if (_disposed is false)
                {
                    LoadImageFromStream(_imageStream, _srcHintsSvg || contentTypeHintsSvg);

                    if (Image is not null)
                    {
                        _releaseImageObject = true;
                    }
                }
            }

            ImageLoadComplete();
        }

        /// <summary>
        /// Flag image load complete and request refresh for re-layout and invalidate.
        /// </summary>
        private void ImageLoadComplete()
        {
            // can happen if some operation return after the handler was disposed
            if (_disposed)
                ReleaseObjects();
        }

        /// <summary>
        /// Release the image and client objects.
        /// </summary>
        private void ReleaseObjects()
        {
            if (_releaseImageObject && Image != null)
            {
                Image.Dispose();
                Image = null;
            }

            if (_imageStream == null) return;

            _imageStream.Dispose();
            _imageStream = null;
        }

        #endregion
    }
}