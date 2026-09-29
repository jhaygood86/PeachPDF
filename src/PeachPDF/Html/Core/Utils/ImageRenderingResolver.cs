using PeachDrawing.Core;
using PeachPDF.CSS;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>Maps the <c>image-rendering</c> keywords onto how a canvas samples an image.</summary>
    internal static class ImageRenderingResolver
    {
        /// <summary>
        /// The sampling for an element's <c>image-rendering</c> (CSS Images 3 §5.3). <c>auto</c> leaves the choice to the caller, whose
        /// <paramref name="whenAuto"/> says what the content wants by default (for instance crisp for tiles that must butt together
        /// without a seam).
        /// </summary>
        /// <param name="mode">the computed <c>image-rendering</c></param>
        /// <param name="whenAuto">the sampling to use when the value is <c>auto</c></param>
        public static ImageSampling Resolve(ImageRenderingMode mode, ImageSampling whenAuto = ImageSampling.Automatic) => mode switch
        {
            ImageRenderingMode.Smooth => ImageSampling.Bilinear,
            ImageRenderingMode.HighQuality => ImageSampling.Bicubic,
            ImageRenderingMode.CrispEdges => ImageSampling.Nearest,
            ImageRenderingMode.Pixelated => ImageSampling.Pixelated,
            _ => whenAuto,
        };
    }
}
