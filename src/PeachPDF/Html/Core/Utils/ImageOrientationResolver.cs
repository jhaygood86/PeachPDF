using PeachDrawing.Core;
using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using System;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Resolves <c>image-orientation</c> (CSS Images 4 §5.2) for a raster <see cref="Image"/>: the rotation and flip that
    /// turn its stored pixels upright on a given element. <see cref="Image.Width"/>/<see cref="Image.Height"/> stay the
    /// stored raster's dimensions everywhere (the PDF embed and its pixel-size decisions depend on them); the oriented
    /// size is derived here, wherever a raster's intrinsic size is read.
    /// </summary>
    internal static class ImageOrientationResolver
    {
        /// <summary>
        /// The orientation to apply to <paramref name="image"/> on <paramref name="box"/>: the computed
        /// <c>image-orientation</c>, with <c>from-image</c> replaced by the raster's own Exif orientation. Never
        /// <see cref="ImageOrientation.FromImage"/>. The identity for a null image or a non-raster one.
        /// </summary>
        public static ImageOrientation Effective(CssBox box, Image? image)
        {
            if (image is null) return ImageOrientation.Upright;

            var declared = Computed(box);
            return declared.FromImage ? ImageOrientation.FromExif(ExifOrientationOf(image)) : declared;
        }

        /// <summary>The raster's width and height after orientation (swapped for a quarter-turn rotation).</summary>
        public static (double Width, double Height) OrientedSize(CssBox box, Image image)
        {
            var orientation = Effective(box, image);
            return orientation.SwapsAxes ? (image.Height, image.Width) : (image.Width, image.Height);
        }

        /// <summary>The stored Exif Orientation of <paramref name="image"/> (1 when it has none, or is not a decoded raster).</summary>
        public static int ExifOrientationOf(Image image) => image is ImageAdapter adapter ? adapter.ExifOrientation : 1;

        private static ImageOrientation Computed(CssBox box)
        {
            var text = box.ImageOrientation;
            if (string.IsNullOrEmpty(text) || string.Equals(text, "from-image", StringComparison.OrdinalIgnoreCase))
                return ImageOrientation.FromImageValue;

            // An invalid stored value cannot normally arrive (the declaration is validated), but fall back to the initial value.
            return ImageOrientation.TryParse(text) ?? ImageOrientation.FromImageValue;
        }
    }
}
