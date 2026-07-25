using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Html.Core.Handlers;
using PeachPDF.Html.Core.Utils;
using System.Threading.Tasks;

namespace PeachPDF.Html.Core.Paint.Content
{
    /// <summary>
    /// Paints a synthesized <c>::marker</c> pseudo-element: a marker image, one of the procedural
    /// <c>disc</c>/<c>circle</c>/<c>square</c> shapes drawn as vector geometry, or the formatted
    /// counter text.
    /// </summary>
    internal sealed class MarkerFragmentPainter : IFragmentContentPainter
    {
        public ValueTask Paint(FragmentPainter painter, RGraphics g, BoxFragment fragment)
        {
            var box = (CssBoxMarker)fragment.Box;

            if (box.ContentImage is not null)
            {
                var rect = fragment.PrimaryRect;
                if (rect is { Width: > 0, Height: > 0 })
                {
                    PaintMarkerImage(g, box, rect);
                }

                return ValueTask.CompletedTask;
            }

            if (box.Words.Count == 0) return ValueTask.CompletedTask; // content: none, or list-style-type: none with no image

            // A marker whose own word fell outside this page's band has nothing to draw here.
            if (!fragment.TryGetWordRect(box.Words[0], out var wordRect)) return ValueTask.CompletedTask;

            if (box.MarkerShape is not null)
            {
                PaintShape(g, box, wordRect);
            }
            else
            {
                g.DrawString(box.Text ?? string.Empty, box.ActualFont, box.ActualColor,
                    new RPoint(wordRect.X, wordRect.Y), new RSize(wordRect.Width, wordRect.Height),
                    box.Direction == CssConstants.Rtl);
            }

            return ValueTask.CompletedTask;
        }

        private static void PaintMarkerImage(RGraphics g, CssBoxMarker box, RRect rect)
        {
            CssImagePainter.Paint(g, box.ContentImage!, layerIndex: 0, isFirst: true,
                originRect: rect, clipRect: rect, roundedClipPath: null,
                // The default (borrowed) list-style-image marker centers the image within its font-
                // height box, matching today's list-style-image rendering; an author content:url(...)
                // override (owned) matches ::before/::after's own top-left content-image alignment.
                positionList: box.OwnsContentImage ? "0% 0%" : "center",
                sizeList: CssConstants.Auto, repeatList: "no-repeat",
                attachmentList: CssConstants.Scroll, viewportRect: rect, box: box,
                drawBrush: brush =>
                {
                    g.DrawRectangle(brush, rect.X, rect.Y, rect.Width, rect.Height);
                    brush.Dispose();
                });
        }

        private static void PaintShape(RGraphics g, CssBoxMarker box, RRect rect)
        {
            if (box.MarkerShape == CssConstants.Square)
            {
                using var squareBrush = g.GetSolidBrush(box.ActualColor);
                g.DrawRectangle(squareBrush, rect.X, rect.Y, rect.Width, rect.Height);
                return;
            }

            var rx = rect.Width / 2;
            var ry = rect.Height / 2;
            using var path = RenderUtils.GetRoundRect(g, rect, rx, ry, rx, ry, rx, ry, rx, ry);

            if (box.MarkerShape == CssConstants.Disc)
            {
                using var brush = g.GetSolidBrush(box.ActualColor);
                g.DrawPath(brush, path);
            }
            else // circle: hollow ring
            {
                var pen = g.GetPen(box.ActualColor);
                g.DrawPath(pen, path);
            }
        }
    }
}
