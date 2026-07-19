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
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Handlers;
using PeachPDF.Html.Core.Utils;
using System.Globalization;
using System.Threading.Tasks;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// CSS box for a synthesized <c>::marker</c> pseudo-element. Owns its own content resolution,
    /// sizing, positioning and painting - a real, cascaded box mirroring how <c>::before</c>/
    /// <c>::after</c> already work, and following the same "replaced-element subclass owns one phantom
    /// word plus its own <see cref="PerformLayoutImp"/>/<see cref="PaintImpCore"/> overrides" pattern
    /// as <see cref="CssBoxImage"/>/<see cref="CssBoxSvg"/>.
    /// </summary>
    internal sealed class CssBoxMarker : CssBox
    {
        /// <summary>
        /// The default marker shape ("disc"/"circle"/"square") when the effective content is the
        /// procedural default (<c>content: normal</c>) and the owning list item's
        /// <see cref="CssBoxProperties.ListStyleType"/> is one of those three - vector-drawn directly
        /// by <see cref="PaintImpCore"/>, not literal text. Null for text/image markers.
        /// </summary>
        internal string? MarkerShape { get; private set; }

        /// <summary>
        /// Whether <see cref="CssBox.ContentImage"/> is owned by this box (and should be
        /// disposed with it) or merely borrowed from the owning list item's own
        /// <see cref="CssBoxProperties.ListStyleImage"/> (whose lifecycle belongs to that list item,
        /// not this marker) - set false only by <see cref="ResolveDefaultContent"/>'s procedural
        /// <c>list-style-image</c> case; an author <c>content: url(...)</c> override (resolved by
        /// <see cref="CssContentEngine"/> before this box's own content ever needs to be considered)
        /// is owned by this box, exactly like <c>::before</c>/<c>::after</c>.
        /// </summary>
        private bool _ownsContentImage = true;

        public CssBoxMarker(CssBox parent)
            : base(parent, null)
        {
            IsMarkerPseudoElement = true;
        }

        /// <summary>
        /// Resolves the marker's default (<c>content: normal</c>) representation from the owning list
        /// item's own <see cref="CssBoxProperties.ListStyleType"/>/<see cref="CssBoxProperties.ListStyleImage"/>
        /// and the CSS <c>list-item</c> counter (<see cref="CssCounterEngine"/>) - the same generic
        /// counter machinery <c>content: counter(list-item)</c> already uses, so the two are always
        /// consistent by construction. Called from <c>DomParser.CorrectTextBoxes</c>, right after
        /// <see cref="CssContentEngine.ApplyContent"/> - which already fully resolves any actual author
        /// <c>content</c> override (string/counter()/attr()/url()/gradients/none) - so this only ever
        /// needs to act when <c>Content</c> is still the unmodified default, "normal".
        /// </summary>
        internal void ResolveDefaultContent()
        {
            if (!Content.Trim().Equals(CssConstants.Normal, System.StringComparison.OrdinalIgnoreCase)) return;
            if (ParentBox is not CssBox owner) return;

            if (owner.ListStyleImage is not null)
            {
                ContentImage = owner.ListStyleImage;
                _ownsContentImage = false;
                return;
            }

            var listStyleType = owner.ListStyleType;

            if (listStyleType.Equals(CssConstants.Disc, System.StringComparison.OrdinalIgnoreCase) ||
                listStyleType.Equals(CssConstants.Circle, System.StringComparison.OrdinalIgnoreCase) ||
                listStyleType.Equals(CssConstants.Square, System.StringComparison.OrdinalIgnoreCase))
            {
                MarkerShape = listStyleType.ToLowerInvariant();
                return;
            }

            if (listStyleType == CssConstants.None) return; // no marker at all

            var index = CssCounterEngine.GetCounter(this, CssConstants.ListItem)?.Value ?? 1;

            Text = listStyleType.Equals(CssConstants.Decimal, System.StringComparison.OrdinalIgnoreCase)
                ? index.ToString(CultureInfo.InvariantCulture) + "."
                : listStyleType.Equals(CssConstants.DecimalLeadingZero, System.StringComparison.OrdinalIgnoreCase)
                    ? index.ToString("00", CultureInfo.InvariantCulture) + "."
                    : CommonUtils.ConvertToAlphaNumber(index, listStyleType) + ".";
        }

        internal override async ValueTask MeasureWordsSize(RGraphics g)
        {
            if (!_wordsSizeMeasured)
            {
                if (MarkerShape is not null && Words.Count == 0)
                {
                    // Matches the disc/circle/square sizing math list markers have always used -
                    // centered within the line, not top-aligned like a text glyph.
                    var shapeSize = ActualFont.Height * 0.35;
                    Words.Add(new CssRectShape(this) { Width = shapeSize, Height = shapeSize });
                }
                else if (ContentImage is not null && !_ownsContentImage && Words.Count == 0)
                {
                    // Default (procedural) list-style-image marker: sized as a font-height square,
                    // matching the disc/circle/square/text markers, rather than the generic
                    // 20px/CSS-width-driven replaced-element fallback base.MeasureWordsSize would use
                    // for an ordinary content-image box - preserves today's list-style-image sizing.
                    var size = ActualFont.Height;
                    Words.Add(new CssRectImage(this) { Width = size, Height = size });
                }
            }

            await base.MeasureWordsSize(g);
        }

        /// <summary>
        /// Positions an <c>outside</c> marker (the CSS default) relative to its owner, after the
        /// owner's own <c>Location</c> is final - see the call site in <c>CssBox.PerformLayoutImp</c>.
        /// Per CSS2.1 12.5.1 / CSS Lists Level 3, an outside marker must not affect the layout of the
        /// rest of the list item, so it's never part of the owner's own inline flow (excluded in
        /// <c>CssLayoutEngine.FlowBox</c>) - its geometry is entirely self-computed here instead, the
        /// same way a floated or absolutely-positioned box computes its own position from its
        /// containing block rather than from generic inline flow. An <c>inside</c> marker needs none of
        /// this - it's simply the owner's first inline child, positioned by the ordinary inline-layout
        /// algorithm like any other flowed content.
        /// </summary>
        protected override async ValueTask PerformLayoutImp(RGraphics g)
        {
            await MeasureWordsSize(g);

            if (ListStylePosition != CssConstants.Outside) return;
            if (ParentBox is not CssBox owner) return;

            var word = Words.Count > 0 ? Words[0] : null;

            var width = word?.Width ?? 0;
            var height = word?.Height ?? owner.ActualFont.Height;

            var top = owner.Location.Y + owner.ActualPaddingTop;
            if (MarkerShape is not null)
            {
                // Text is drawn top-aligned; center the (much smaller) shape within the owner's line
                // box instead, so it sits level with the middle of the adjacent text.
                top += (owner.ActualFont.Height - height) / 2;
            }

            var left = owner.ClientLeft - width - ActualMarginRight;

            Location = new RPoint(left, top);
            Size = new RSize(width, height);

            if (word is not null)
            {
                word.Left = left;
                word.Top = top;
            }
        }

        protected override async ValueTask PaintImpCore(RGraphics g)
        {
            if (ContentImage is not null)
            {
                var offset = IsFixed ? RPoint.Empty : HtmlContainer!.ScrollOffset;
                var rect = Rectangles.Count == 0 ? Bounds : CommonUtils.GetFirstValueOrDefault(Rectangles);
                rect.Offset(offset);
                if (rect is { Width: > 0, Height: > 0 })
                {
                    PaintMarkerImage(g, rect);
                }

                return;
            }

            if (Words.Count == 0) return; // content: none, or list-style-type: none with no image

            var wordOffset = IsFixed ? RPoint.Empty : HtmlContainer!.ScrollOffset;
            var wordRect = Words[0].Rectangle;
            wordRect.Offset(wordOffset);

            if (MarkerShape is not null)
            {
                PaintShape(g, wordRect);
            }
            else
            {
                g.DrawString(Text ?? string.Empty, ActualFont, ActualColor,
                    new RPoint(wordRect.X, wordRect.Y), new RSize(wordRect.Width, wordRect.Height),
                    Direction == CssConstants.Rtl);
            }

            await ValueTask.CompletedTask;
        }

        private void PaintMarkerImage(RGraphics g, RRect rect)
        {
            CssImagePainter.Paint(g, ContentImage!, layerIndex: 0, isFirst: true,
                originRect: rect, clipRect: rect, roundedClipPath: null,
                // The default (borrowed) list-style-image marker centers the image within its font-
                // height box, matching today's list-style-image rendering; an author content:url(...)
                // override (owned) matches ::before/::after's own top-left content-image alignment.
                positionList: _ownsContentImage ? "0% 0%" : "center",
                sizeList: CssConstants.Auto, repeatList: "no-repeat",
                attachmentList: CssConstants.Scroll, viewportRect: rect, box: this,
                drawBrush: brush =>
                {
                    g.DrawRectangle(brush, rect.X, rect.Y, rect.Width, rect.Height);
                    brush.Dispose();
                });
        }

        private void PaintShape(RGraphics g, RRect rect)
        {
            if (MarkerShape == CssConstants.Square)
            {
                using var brush = g.GetSolidBrush(ActualColor);
                g.DrawRectangle(brush, rect.X, rect.Y, rect.Width, rect.Height);
                return;
            }

            var rx = rect.Width / 2;
            var ry = rect.Height / 2;
            using var path = RenderUtils.GetRoundRect(g, rect, rx, ry, rx, ry, rx, ry, rx, ry);

            if (MarkerShape == CssConstants.Disc)
            {
                using var brush = g.GetSolidBrush(ActualColor);
                g.DrawPath(brush, path);
            }
            else // circle: hollow ring
            {
                var pen = g.GetPen(ActualColor);
                g.DrawPath(pen, path);
            }
        }

        public override void Dispose()
        {
            if (!_ownsContentImage)
            {
                // Borrowed from the owning list item's own ListStyleImage, whose lifecycle belongs to
                // that box - clear the reference here so base.Dispose() doesn't dispose it out from
                // under the owner.
                ContentImage = null;
            }

            base.Dispose();
        }
    }
}
