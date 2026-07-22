using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using System;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Resolves how a replaced element's content (an <c>&lt;img&gt;</c>'s image) is sized and placed
    /// inside its content box for [`object-fit`](https://developer.mozilla.org/en-US/docs/Web/CSS/object-fit)
    /// and [`object-position`](https://developer.mozilla.org/en-US/docs/Web/CSS/object-position)
    /// (CSS Images Module Level 3 §5-6). The fit geometry is the same as <c>background-size</c>
    /// <c>contain</c>/<c>cover</c> and the placement is the same grammar as <c>background-position</c>,
    /// so both are delegated to <see cref="BackgroundLayerResolver"/> rather than re-implemented.
    /// </summary>
    internal static class ObjectFitResolver
    {
        private const double Epsilon = 0.01;

        /// <summary>
        /// Computes the destination rectangle to draw the content into, and whether the result overflows
        /// <paramref name="contentBox"/> (so the caller should clip to it, e.g. for <c>cover</c>). The
        /// intrinsic size is in the same units as <paramref name="contentBox"/> (PDF points).
        /// </summary>
        public static (RRect Destination, bool NeedsClip) Compute(
            RRect contentBox,
            double naturalWidth, double naturalHeight,
            string objectFit, string objectPosition,
            CssBoxProperties box)
        {
            // fill (the initial value) and the no-known-intrinsic-size case both stretch to the content
            // box - identical to the pre-object-fit behavior, so the common path is unchanged.
            if (naturalWidth <= 0 || naturalHeight <= 0
                || string.Equals(objectFit, CssConstants.Fill, StringComparison.OrdinalIgnoreCase))
            {
                return (contentBox, false);
            }

            var containerWidth = contentBox.Width;
            var containerHeight = contentBox.Height;
            var ratio = naturalWidth / naturalHeight;

            var (objectWidth, objectHeight) = objectFit.ToLowerInvariant() switch
            {
                CssConstants.Contain =>
                    BackgroundLayerResolver.ResolveSize(CssConstants.Contain, containerWidth, containerHeight, naturalWidth, naturalHeight, ratio, box),
                CssConstants.Cover =>
                    BackgroundLayerResolver.ResolveSize(CssConstants.Cover, containerWidth, containerHeight, naturalWidth, naturalHeight, ratio, box),
                CssConstants.None =>
                    (naturalWidth, naturalHeight),
                CssConstants.ScaleDown =>
                    ScaleDown(containerWidth, containerHeight, naturalWidth, naturalHeight, ratio, box),
                _ => // any unrecognized value falls back to fill
                    (containerWidth, containerHeight)
            };

            var (offsetX, offsetY) = BackgroundLayerResolver.ResolvePosition(
                objectPosition, containerWidth, containerHeight, objectWidth, objectHeight, box);

            var destination = new RRect(contentBox.X + offsetX, contentBox.Y + offsetY, objectWidth, objectHeight);
            var needsClip = objectWidth > containerWidth + Epsilon || objectHeight > containerHeight + Epsilon;
            return (destination, needsClip);
        }

        // scale-down uses whichever of `none` (intrinsic size) and `contain` produces the smaller
        // concrete object size - i.e. it behaves like `none` unless that would overflow, in which case
        // it behaves like `contain`. Both preserve the intrinsic aspect ratio, so comparing widths is
        // sufficient.
        private static (double Width, double Height) ScaleDown(
            double containerWidth, double containerHeight,
            double naturalWidth, double naturalHeight,
            double ratio, CssBoxProperties box)
        {
            var contain = BackgroundLayerResolver.ResolveSize(
                CssConstants.Contain, containerWidth, containerHeight, naturalWidth, naturalHeight, ratio, box);
            return naturalWidth <= contain.Width ? (naturalWidth, naturalHeight) : contain;
        }
    }
}
