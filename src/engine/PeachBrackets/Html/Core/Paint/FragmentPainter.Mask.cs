using PeachBrackets.Css;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Fragments;
using PeachBrackets.Html.Handlers;
using PeachBrackets.Html.Utils;
using PeachDrawing.Core;

namespace PeachBrackets.Html.Paint
{
    /// <summary>
    /// <c>mask-image</c> (CSS Masking 1 section 6): the element and everything inside it is painted into a tile, the mask layers
    /// into another, and the first is drawn through the second as a soft mask, so the element's content is revealed where the mask is
    /// opaque (<c>alpha</c>) or bright (<c>luminance</c>).
    /// </summary>
    internal sealed partial class FragmentPainter
    {
        /// <summary>Whether <paramref name="box"/> has at least one mask layer that is an image rather than <c>none</c>.</summary>
        private static bool HasMask(CssBox box)
        {
            if (box.MaskImages is not { Count: > 0 } images) return false;

            foreach (var image in images)
            {
                if (image is not null) return true;
            }

            return false;
        }

        /// <summary>
        /// Paints <paramref name="fragment"/> through its <c>mask-image</c> layers. The mask painting area is the border box
        /// (<c>mask-clip: border-box</c>), so content outside it is not painted. Opacity and <c>mix-blend-mode</c> apply to the
        /// masked result as a whole.
        /// </summary>
        /// <returns>false when the canvas cannot make tiles (a measure-only pass), so the caller paints the box unmasked</returns>
        private bool PaintMasked(Canvas g, BoxFragment fragment)
        {
            var box = fragment.Box;
            var rect = fragment.WholeBoxRect;
            if (rect is not { Width: > 0, Height: > 0 }) return false;

            // Both tiles run from the page origin to the box's far corner, so everything keeps painting at its own coordinates:
            // a tile's initial clip is in the tile's own space, and a shifted canvas would intersect every clip pushed inside it
            // (an overflow clip, a background tile) with the wrong rectangle. The content is cut to the border box instead.
            var tileBounds = new Rect(0, 0, rect.Right, rect.Bottom);

            var content = g.CreateTile(tileBounds.Width, tileBounds.Height);
            if (content is not { } contentTile) return false;

            var maskTile = g.CreateTile(tileBounds.Width, tileBounds.Height);
            if (maskTile is not { } maskLayer)
            {
                contentTile.Graphics.Dispose();
                return false;
            }

            contentTile.Graphics.PushClip(rect);
            PaintTagged(contentTile.Graphics, fragment);
            contentTile.Graphics.PopClip();
            contentTile.Graphics.Dispose();

            PaintMaskLayers(maskLayer.Graphics, box, rect);
            maskLayer.Graphics.Dispose();

            var luminance = UsesLuminance(box);

            if (box.IsOpaque && box.ActualMixBlendMode == BlendMode.Normal)
            {
                DrawThroughMask(g, contentTile.Image, maskLayer.Image, tileBounds, luminance);
                return true;
            }

            // The masked result is one group, which opacity and the blend mode then apply to.
            using var layer = g.BeginLayer(new LayerOptions(box.ActualOpacity, ToRBlendMode(box.ActualMixBlendMode)));
            DrawThroughMask(layer?.Canvas ?? g, contentTile.Image, maskLayer.Image, tileBounds, luminance);
            return true;
        }

        private static void DrawThroughMask(Canvas target, Image content, Image mask, Rect rect, bool luminance)
        {
            if (luminance) target.DrawImageMasked(content, mask, rect);
            else target.DrawImageAlphaMasked(content, mask, rect);
        }

        /// <summary>
        /// <c>mask-mode</c> of the first image layer. <c>match-source</c> is <c>alpha</c> for an image; one mode is used for the whole
        /// mask, since the layers are painted into a single tile.
        /// </summary>
        private static bool UsesLuminance(CssBox box)
        {
            var modes = BackgroundLayerResolver.SplitLayers(box.MaskMode);
            for (var i = 0; i < box.MaskImages!.Count; i++)
            {
                if (box.MaskImages[i] is null) continue;

                return BackgroundLayerResolver.LayerAt(modes, i).Trim().Equals("luminance", System.StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private static void PaintMaskLayers(Canvas g, CssBox box, Rect rect)
        {
            var originLayers = BackgroundLayerResolver.SplitLayers(box.MaskOrigin);
            var images = box.MaskImages!;

            // Last layer is bottom-most, like backgrounds; layers composite with add, which painting them in order into one tile gives.
            for (var layerIndex = images.Count - 1; layerIndex >= 0; layerIndex--)
            {
                if (images[layerIndex] is not { } image) continue;

                var origin = BackgroundLayerResolver.LayerAt(originLayers, layerIndex).Trim() switch
                {
                    Keywords.ContentBox => new Rect(
                        rect.X + box.ActualBorderLeftWidth + box.ActualPaddingLeft,
                        rect.Y + box.ActualBorderTopWidth + box.ActualPaddingTop,
                        rect.Width - box.ActualBorderLeftWidth - box.ActualBorderRightWidth - box.ActualPaddingLeft - box.ActualPaddingRight,
                        rect.Height - box.ActualBorderTopWidth - box.ActualBorderBottomWidth - box.ActualPaddingTop - box.ActualPaddingBottom),
                    Keywords.PaddingBox => new Rect(
                        rect.X + box.ActualBorderLeftWidth,
                        rect.Y + box.ActualBorderTopWidth,
                        rect.Width - box.ActualBorderLeftWidth - box.ActualBorderRightWidth,
                        rect.Height - box.ActualBorderTopWidth - box.ActualBorderBottomWidth),
                    _ => rect,
                };

                if (origin is not { Width: > 0, Height: > 0 }) continue;

                CssImagePainter.Paint(g, image, layerIndex, origin, rect, null, box.MaskPosition, box.MaskSize, box.MaskRepeat,
                    Keywords.Scroll, rect, box,
                    brush =>
                    {
                        g.DrawRectangle(brush, rect.X, rect.Y, rect.Width, rect.Height);
                        brush.Dispose();
                    });
            }
        }
    }
}
