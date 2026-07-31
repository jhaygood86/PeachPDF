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
using PeachPDF.Html.Core.Utils;
using System.Threading.Tasks;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// CSS box for a synthesized <c>::marker</c> pseudo-element. Owns its own content resolution,
    /// sizing and positioning - a real, cascaded box mirroring how <c>::before</c>/<c>::after</c>
    /// already work, and following the same "replaced-element subclass owns one phantom word plus its
    /// own <see cref="PerformLayoutImp"/>" pattern as <see cref="CssBoxImage"/>/<see cref="CssBoxSvg"/>.
    /// Its content is drawn by <c>MarkerFragmentPainter</c>.
    /// </summary>
    internal sealed class CssBoxMarker : CssBox
    {
        /// <summary>
        /// The default marker shape ("disc"/"circle"/"square") when the effective content is the
        /// procedural default (<c>content: normal</c>) and the owning list item's
        /// <see cref="ListArea.ListStyleType"/> is one of those three - vector-drawn directly
        /// by <c>MarkerFragmentPainter</c>, not literal text. Null for text/image markers.
        /// </summary>
        internal string? MarkerShape { get; private set; }

        /// <summary>
        /// Whether <see cref="CssBox.ContentImage"/> is owned by this box (and should be
        /// disposed with it) or merely borrowed from the owning list item's own
        /// <see cref="ListArea.ListStyleImage"/> (whose lifecycle belongs to that list item,
        /// not this marker) - set false only by <see cref="ResolveDefaultContent"/>'s procedural
        /// <c>list-style-image</c> case; an author <c>content: url(...)</c> override (resolved by
        /// <see cref="CssContentEngine"/> before this box's own content ever needs to be considered)
        /// is owned by this box, exactly like <c>::before</c>/<c>::after</c>. It also decides how the
        /// image is aligned within the marker box - see <c>MarkerFragmentPainter</c>.
        /// </summary>
        internal bool OwnsContentImage => _ownsContentImage;

        private bool _ownsContentImage = true;

        public CssBoxMarker(CssBox parent)
            : base(parent, null)
        {
            IsMarkerPseudoElement = true;
        }

        /// <summary>
        /// Resolves the marker's default (<c>content: normal</c>) representation from the owning list
        /// item's own <see cref="ListArea.ListStyleType"/>/<see cref="ListArea.ListStyleImage"/>
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

            Text = CssCounterEngine.FormatCounterValue(index, listStyleType) + ".";
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
        /// Positions an <c>outside</c> marker (the CSS default) relative to its owner, once the frame above
        /// the owner has assigned its <c>Location</c> - see the call site, <c>CssBox.LayoutOutsideMarker</c>,
        /// and its remarks for why that is the pass the item <i>starts</i> in rather than the one it ends in.
        /// Per CSS2.1 12.5.1 / CSS Lists Level 3, an outside marker must not affect the layout of the
        /// rest of the list item, so it's never part of the owner's own inline flow (excluded in
        /// <c>CssLayoutEngine.FlowBox</c>) - its geometry is entirely self-computed here instead, the
        /// same way a floated or absolutely-positioned box computes its own position from its
        /// containing block rather than from generic inline flow. An <c>inside</c> marker needs none of
        /// this - it's simply the owner's first inline child, positioned by the ordinary inline-layout
        /// algorithm like any other flowed content.
        /// </summary>
        protected override async ValueTask PerformLayoutImp(RGraphics g, CssBox frame, bool framePlacesChild)
        {
            // This box's own pass never routes through CssBox.BeginBlockPass (there is no prologue/
            // placement/content split for a marker), so nothing else clears CssBox._awaitingRefill for it -
            // without this, a container resetting this marker once and then genuinely laying it out again
            // here would leave a later reset skipped as a stale no-op.
            _awaitingRefill = false;

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
