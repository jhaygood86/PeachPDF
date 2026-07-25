using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Html.Core.Handlers;
using PeachPDF.Html.Core.Utils;
using System;
using System.Threading.Tasks;

namespace PeachPDF.Html.Core.Paint.Content
{
    /// <summary>
    /// The shared shape of painting a replaced element: clip to any ancestor <c>overflow</c>, paint the
    /// box's own background and borders over its principal rectangle, then draw the replacement content
    /// into the content box of the phantom word that carries it. Subclasses supply only where the
    /// content comes from and how it is drawn.
    /// </summary>
    internal abstract class ReplacedFragmentPainter : IFragmentContentPainter
    {
        public async ValueTask Paint(FragmentPainter painter, RGraphics g, BoxFragment fragment)
        {
            var box = fragment.Box;

            await EnsureContentResolved(box);

            if (!IsReplaced(box))
            {
                // An <object> whose data did not resolve to a supported image is not a replaced element
                // at all: it stays an ordinary container and paints its fallback DOM children.
                await painter.PaintBoxContent(g, fragment);
                return;
            }

            var rect = fragment.PrimaryRect;

            var clipped = RenderUtils.ClipGraphicsByOverflow(g, box, fragment.OriginY);

            FragmentPainter.PaintBackground(g, box, rect, isFirst: true);
            BordersDrawHandler.DrawBoxBorders(g, box, rect, true, true);

            DrawContent(g, box, ContentRect(box, fragment, rect));

            if (clipped)
                g.PopClip();
        }

        /// <summary>
        /// The rectangle the replacement content is drawn into: the phantom word's own fragment
        /// rectangle (the box's principal rectangle if the word did not land on this page), deflated by
        /// the box's border and padding and snapped to whole units.
        /// </summary>
        private RRect ContentRect(CssBox box, BoxFragment fragment, RRect fallback)
        {
            var word = ContentWord(box);

            if (word is null || !fragment.TryGetWordRect(word, out var r))
                r = fallback;

            r.Height -= box.ActualBorderTopWidth + box.ActualBorderBottomWidth + box.ActualPaddingTop + box.ActualPaddingBottom;
            r.Y += box.ActualBorderTopWidth + box.ActualPaddingTop;
            r.X = Math.Floor(r.X);
            r.Y = Math.Floor(r.Y);

            return r;
        }

        /// <summary>
        /// Resolves the box's content if it has not been resolved already. Content is normally resolved
        /// during measurement, so this is a no-op by default.
        /// </summary>
        protected virtual ValueTask EnsureContentResolved(CssBox box) => ValueTask.CompletedTask;

        /// <summary>
        /// Whether this box really is a replaced element. Only <c>&lt;object&gt;</c> can answer no —
        /// see the HTML replacement algorithm in <see cref="CssBoxObject"/>.
        /// </summary>
        protected virtual bool IsReplaced(CssBox box) => true;

        /// <summary>The phantom word whose rectangle positions the replacement content.</summary>
        protected abstract CssRect? ContentWord(CssBox box);

        /// <summary>Draws the replacement content into its resolved content box.</summary>
        protected abstract void DrawContent(RGraphics g, CssBox box, RRect rect);
    }
}
