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

using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Network;
using SixLabors.ImageSharp;
using System;
using System.IO;
using System.Threading.Tasks;

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
        /// Must be open as long as the image is in use
        /// </summary>
        private FileStream? _imageFileStream;

        /// <summary>
        /// flag to indicate if to release the image object on box dispose (only if image was loaded by the box)
        /// </summary>
        private bool _releaseImageObject;

        /// <summary>
        /// is the handler has been disposed
        /// </summary>
        private bool _disposed;

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
        /// Sets image of this image box from a CSS image definition
        /// </summary>
        /// <param name="image"></param>
        /// <returns></returns>
        public async ValueTask LoadImage(CssImage image)
        {
            if (image.Kind == CssImage.CssImageKind.Url) await LoadImage(image.Url!);
        }

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
        /// Load image from path of image file or URL.
        /// </summary>
        /// <param name="path">the file path or uri to load image from</param>
        private async ValueTask SetImageFromPath(string path)
        {
            var uri = new RUri(path, UriKind.RelativeOrAbsolute);

            if (!uri.IsAbsoluteUri)
            {
                var baseElement = DomUtils.GetBoxByTagName(_htmlContainer.Root, "base");
                var baseUrl = "";

                if (baseElement is not null)
                {
                    baseUrl = baseElement.HtmlTag?.TryGetAttribute("href", "");
                }

                var baseUri = string.IsNullOrWhiteSpace(baseUrl) ? _htmlContainer.Adapter.BaseUri : new RUri(baseUrl);
                uri = baseUri is null ? uri : new RUri(baseUri, uri);
            }

            if (uri is { IsAbsoluteUri: true, Scheme: not "file" })
            {
                await SetImageFromUrl(uri);
            }
            else
            {
                var fileInfo = CommonUtils.TryGetFileInfo(path);
                if (fileInfo != null)
                {
                    SetImageFromFile(fileInfo);
                }
                else
                {
                    ImageLoadComplete();
                    _htmlContainer.ReportError(HtmlRenderErrorType.Image, "Failed load image, invalid source: " + path);
                }
            }
        }

        /// <summary>
        /// Load the image file on thread-pool thread and calling <see cref="ImageLoadComplete"/> after.
        /// </summary>
        /// <param name="source">the file path to get the image from</param>
        private void SetImageFromFile(FileInfo source)
        {
            if (source.Exists)
            {
                LoadImageFromFile(source.FullName);
            }
            else
            {
                ImageLoadComplete();
            }
        }

        /// <summary>
        /// Load the image file on thread-pool thread and calling <see cref="ImageLoadComplete"/> after.<br/>
        /// Calling <see cref="ImageLoadComplete"/> on the main thread and not thread-pool.
        /// </summary>
        /// <param name="source">the file path to get the image from</param>
        private void LoadImageFromFile(string source)
        {
            try
            {
                var imageFileStream = File.Open(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                _imageFileStream = imageFileStream;

                if (_disposed is false)
                {
                    LoadImageFromStream(imageFileStream);
                    _releaseImageObject = true;
                }

                ImageLoadComplete();
            }
            catch (Exception ex)
            {
                ImageLoadComplete();
                _htmlContainer.ReportError(HtmlRenderErrorType.Image, "Failed to load image from disk: " + source, ex);
            }
        }

        private void LoadImageFromStream(Stream stream)
        {
            try
            {
                Image = _htmlContainer.Adapter.ImageFromStream(stream);
            }
            catch (UnknownImageFormatException)
            {
                Image = null;
            }
        }

        /// <summary>
        /// Load image from the given URI by downloading it.<br/>
        /// Create local file name in temp folder from the URI, if the file already exists use it as it has already been downloaded.
        /// If not download the file.
        /// </summary>
        private async ValueTask SetImageFromUrl(RUri source)
        {
            if (source is { IsAbsoluteUri: true, IsFile: true })
            {
                var filePath = CommonUtils.GetLocalFileName(source);

                if (filePath is { Exists: true, Length: > 0 })
                {
                    SetImageFromFile(filePath);
                }

                return;
            }

            var networkResponse = await _htmlContainer.Adapter.GetResourceStream(source);

            if (networkResponse?.ResourceStream is not null)
            {
                LoadImageFromStream(networkResponse.ResourceStream);
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

            if (_imageFileStream == null) return;

            _imageFileStream.Dispose();
            _imageFileStream = null;
        }

        #endregion
    }
}