using PeachDrawing.Core;
using PeachBrackets.Css;
using PeachBrackets.Html.Dom;
using System;

namespace PeachBrackets.Html.Utils
{
    /// <summary>
    /// Resolves <c>image-resolution</c> (CSS Images 4 §6.1) for a raster <see cref="Image"/>: how many of its pixels make a CSS pixel, which
    /// sets the natural size an image takes in layout. The density a file records (<see cref="Image.ResolutionDpiX"/>) is only used when the
    /// property asks for it with <c>from-image</c>; by default a raster is 96 dpi, as before.
    /// </summary>
    internal static class ImageResolutionResolver
    {
        /// <summary>The computed <c>image-resolution</c> of <paramref name="box"/>, the initial <c>1dppx</c> when it is unset or invalid.</summary>
        public static ImageResolution Computed(CssBox box)
        {
            var text = box.ImageResolution;
            if (string.IsNullOrEmpty(text)) return ImageResolution.Initial;

            // An invalid stored value cannot normally arrive (the declaration is validated), but fall back to the initial value.
            return ImageResolution.TryParse(text) ?? ImageResolution.Initial;
        }

        /// <summary>The resolution in dots per CSS pixel along the stored raster's width and height.</summary>
        public static (double X, double Y) Dppx(CssBox box, Image image)
        {
            var resolution = Computed(box);
            var x = resolution.Resolve(image.ResolutionDpiX);
            var y = resolution.Resolve(image.ResolutionDpiY ?? image.ResolutionDpiX);
            return (x, y);
        }

        /// <summary>
        /// The natural size of <paramref name="image"/> on <paramref name="box"/> in CSS pixels: the stored pixel size over the image's
        /// resolution along each axis, then turned upright by <c>image-orientation</c> (swapped for a quarter turn).
        /// </summary>
        public static (double Width, double Height) NaturalSize(CssBox box, Image image)
        {
            var (dx, dy) = Dppx(box, image);
            var width = image.Width / dx;
            var height = image.Height / dy;
            return ImageOrientationResolver.Effective(box, image).SwapsAxes ? (height, width) : (width, height);
        }
    }
}
