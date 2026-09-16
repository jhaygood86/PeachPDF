using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Handlers;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// Paints a CSS <c>background</c> (color, then image/gradient layers back-to-front) against an
    /// arbitrary rectangle with no real, laid-out <c>CssBox</c> to read from - the shared primitive
    /// behind both the <c>@page</c> box's own background (<see cref="PdfGenerator"/>, css-page-3 §3.1)
    /// and a page-margin box's background (<see cref="MarginBoxRenderer"/>, css-page-3 §5.1). Follows
    /// the same no-CssBox pattern <see cref="MarginBoxRenderer.PaintImage"/> already established for a
    /// margin box's <c>content: url(...)</c> - a real <c>CssBox</c> (<see cref="PaintAsync"/>'s
    /// <c>lengthBasisBox</c>, always <c>htmlContainer.Root</c> in practice) stands in purely as the
    /// em/rem length basis <see cref="Utils.BackgroundLayerResolver"/>/gradient resolution needs, never
    /// for its own geometry.
    /// <para>
    /// Deliberately separate from <see cref="Paint.FragmentPainter.PaintBackground"/>, which is tightly
    /// coupled to a real box's border/padding/radius/glyph-outline (<c>background-clip: text</c>) state -
    /// none of which either caller here has. Per CLAUDE.md's "don't write two independent
    /// implementations of the same painting logic" convention, this is the one place both callers share
    /// for the color-then-layers algorithm itself.
    /// </para>
    /// </summary>
    internal static class LayeredBackgroundPainter
    {
        /// <summary>
        /// Paints <paramref name="style"/>'s <c>background-color</c> then <c>background-image</c> layers
        /// (back-to-front, so the first-declared layer ends up on top - matching
        /// <see cref="Paint.FragmentPainter.PaintBackground"/>'s own ordering) into whatever rectangle
        /// <paramref name="resolvePositioningRect"/> resolves each layer's own
        /// <c>background-origin</c>/<c>background-clip</c> value to.
        /// </summary>
        /// <param name="g">the device to draw into</param>
        /// <param name="style">the resolved declaration to read background properties from</param>
        /// <param name="adapter">used to parse colors/images with no box-tree context</param>
        /// <param name="htmlContainer">used only to load a url() image</param>
        /// <param name="lengthBasisBox">
        /// a real, laid-out box standing in for em/rem length resolution only - never consulted for its
        /// own geometry (see this class's own remarks)
        /// </param>
        /// <param name="gradientEmSizePt">
        /// overrides <paramref name="lengthBasisBox"/>'s own em-height for a gradient's stop-position/
        /// explicit-radius resolution (issue #827) - the caller's own resolved <c>font-size</c>, when it
        /// has one, rather than <paramref name="lengthBasisBox"/>'s (which is usually the document root
        /// and so usually different).
        /// </param>
        /// <param name="resolvePositioningRect">
        /// resolves a <c>background-origin</c>/<c>background-clip</c> keyword (<c>border-box</c>/
        /// <c>padding-box</c>/<c>content-box</c>, or empty for the initial value) to the rectangle that
        /// value means for this caller's own box model. A caller with no border/padding concept of its
        /// own (the <c>@page</c> box) can ignore the keyword entirely and always return the same rect.
        /// </param>
        /// <param name="viewportRect">
        /// the positioning area a <c>background-attachment: fixed</c> layer uses instead of its own
        /// origin rect (CSS Backgrounds 3 §3.9)
        /// </param>
        /// <param name="imageCache">
        /// caches parsed+loaded image layers by raw <c>background-image</c> declaration text, since the
        /// same declaration (the same image) repeats identically on every page/every instance of a
        /// margin box - mirrors the existing per-document <c>marginBoxImageCache</c> rationale in
        /// <see cref="PdfGenerator"/>. Caller-owned and disposed by the caller once the whole document is
        /// painted.
        /// </param>
        internal static async Task PaintAsync(
            RGraphics g,
            StyleDeclaration? style,
            RAdapter adapter,
            HtmlContainerInt htmlContainer,
            CssBox lengthBasisBox,
            double? gradientEmSizePt,
            Func<string, RRect> resolvePositioningRect,
            RRect viewportRect,
            Dictionary<string, IReadOnlyList<CssImage>?> imageCache)
        {
            if (style is null) return;

            // background-color is always the bottom-most layer (CSS Backgrounds 3 §3.6/§3.8), painted
            // before any image/gradient layer so those appear on top of it, not hidden beneath it.
            var colorStr = style.BackgroundColor;
            if (!string.IsNullOrEmpty(colorStr))
            {
                var color = new CssValueParser(adapter).GetActualColor(colorStr);
                if (RenderUtils.IsColorVisible(color))
                {
                    var colorRect = resolvePositioningRect(style.BackgroundClip);
                    if (colorRect is { Width: > 0, Height: > 0 })
                    {
                        var brush = g.GetSolidBrush(color);
                        g.DrawRectangle(brush, colorRect.X, colorRect.Y, colorRect.Width, colorRect.Height);
                        brush.Dispose();
                    }
                }
            }

            var imageValue = style.BackgroundImage;
            if (string.IsNullOrWhiteSpace(imageValue) ||
                imageValue.Equals(Keywords.None, StringComparison.OrdinalIgnoreCase))
                return;

            if (!imageCache.TryGetValue(imageValue, out var images))
            {
                images = new CssValueParser(adapter).ParseImages(imageValue);
                if (images != null)
                {
                    foreach (var image in images)
                        await image.EnsureLoadedAsync(htmlContainer);
                }

                imageCache[imageValue] = images;
            }

            if (images is not { Count: > 0 }) return;

            var positionList = style.BackgroundPosition;
            var sizeList = style.BackgroundSize;
            var repeatList = style.BackgroundRepeat;
            var attachmentList = style.BackgroundAttachment;
            var originLayers = BackgroundLayerResolver.SplitLayers(style.BackgroundOrigin);
            var clipLayers = BackgroundLayerResolver.SplitLayers(style.BackgroundClip);

            // Back-to-front (last in the comma-list = bottom-most image layer, but still on top of the
            // solid color) so the first-declared layer ends up visually on top - same ordering
            // FragmentPainter.PaintBackground uses.
            for (var layerIndex = images.Count - 1; layerIndex >= 0; layerIndex--)
            {
                var originValue = BackgroundLayerResolver.LayerAt(originLayers, layerIndex);
                var clipValue = BackgroundLayerResolver.LayerAt(clipLayers, layerIndex);
                var originRect = resolvePositioningRect(originValue);
                var clipRect = resolvePositioningRect(clipValue);
                if (clipRect is not { Width: > 0, Height: > 0 }) continue;

                CssImagePainter.Paint(g, images[layerIndex], layerIndex, originRect, clipRect,
                    roundedClipPath: null, positionList, sizeList, repeatList, attachmentList,
                    viewportRect, lengthBasisBox,
                    drawBrush: brush =>
                    {
                        g.DrawRectangle(brush, clipRect.X, clipRect.Y, clipRect.Width, clipRect.Height);
                        brush.Dispose();
                    },
                    gradientEmSizePt: gradientEmSizePt);
            }
        }
    }
}
