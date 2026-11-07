using PeachBrackets.Css;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Entities;
using PeachBrackets.Html.Fragments;
using PeachBrackets.Html.Handlers;
using PeachBrackets.Html.Parse;
using PeachDrawing.Core;
using System.Numerics;

namespace PeachBrackets.Html.Paint
{
    /// <summary>
    /// <c>-webkit-box-reflect</c>: the box and everything inside it painted a second time, mirrored about one of its edges and moved away by the
    /// offset, through an optional image mask. Painting is by a fresh painter (a fragment is painted once per painter) that is told not to
    /// reflect this fragment again. The reflection takes no space in layout and is not text a reader can select.
    /// </summary>
    internal sealed partial class FragmentPainter
    {
        /// <summary>The fragment this painter is painting as a reflection, which must not itself be reflected again.</summary>
        private BoxFragment? _reflectionOf;

        private static bool HasReflection(CssBox box) =>
            !string.IsNullOrEmpty(box.BoxReflect) && !box.BoxReflect.Equals(Keywords.None, System.StringComparison.OrdinalIgnoreCase);

        private void PaintReflection(Canvas g, BoxFragment fragment)
        {
            var box = fragment.Box;
            var rect = fragment.WholeBoxRect;
            if (rect is not { Width: > 0, Height: > 0 }) return;
            if (!BoxReflectGrammar.TryParse(box.BoxReflect, out var none, out var direction, out var offsetText, out var imageText) || none) return;

            var horizontal = direction is BoxReflectDirection.Left or BoxReflectDirection.Right;
            var offset = offsetText is null ? 0 : CssValueParser.ParseLength(offsetText, horizontal ? rect.Width : rect.Height, box);

            var matrix = direction switch
            {
                BoxReflectDirection.Above => new Matrix3x2(1, 0, 0, -1, 0, (float)(2 * rect.Y - offset)),
                BoxReflectDirection.Left => new Matrix3x2(-1, 0, 0, 1, (float)(2 * rect.X - offset), 0),
                BoxReflectDirection.Right => new Matrix3x2(-1, 0, 0, 1, (float)(2 * rect.Right + offset), 0),
                _ => new Matrix3x2(1, 0, 0, -1, 0, (float)(2 * rect.Bottom + offset)),
            };

            if (!TryPushTransform(g, matrix)) return;

            var painter = new FragmentPainter(container) { _reflectionOf = fragment, _taggingSuppressed = true, _pageRoot = _pageRoot };

            CssImage? mask = null;
            if (imageText is not null)
            {
                var images = new CssValueParser(container.RenderContext).ParseImages(imageText, container);
                if (images is { Count: > 0 }) mask = images[0];
            }

            var painted = mask is not null && painter.PaintReflectionThroughMask(g, fragment, mask);

            if (!painted)
            {
                g.InvisibleText = true;
                painter.PaintClippedWithEffects(g, fragment);
                g.InvisibleText = false;
            }

            g.PopTransform();
        }

        /// <returns>false when the canvas cannot make tiles (a measure-only pass), so the caller paints the reflection unmasked</returns>
        private bool PaintReflectionThroughMask(Canvas g, BoxFragment fragment, CssImage mask)
        {
            var box = fragment.Box;
            var rect = fragment.WholeBoxRect;
            var tileBounds = new Rect(0, 0, rect.Right, rect.Bottom);

            if (g.CreateTile(tileBounds.Width, tileBounds.Height) is not { } content) return false;
            if (g.CreateTile(tileBounds.Width, tileBounds.Height) is not { } maskTile)
            {
                content.Graphics.Dispose();
                return false;
            }

            content.Graphics.InvisibleText = true;
            content.Graphics.PushClip(rect);
            PaintClippedWithEffects(content.Graphics, fragment);
            content.Graphics.PopClip();
            content.Graphics.Dispose();

            var maskGraphics = maskTile.Graphics;
            CssImagePainter.Paint(maskGraphics, mask, 0, rect, rect, null, "0% 0%", "100% 100%", "no-repeat", Keywords.Scroll, rect, box,
                brush =>
                {
                    maskGraphics.DrawRectangle(brush, rect.X, rect.Y, rect.Width, rect.Height);
                    brush.Dispose();
                });
            maskGraphics.Dispose();

            g.DrawImageAlphaMasked(content.Image, maskTile.Image, tileBounds);
            return true;
        }
    }
}
