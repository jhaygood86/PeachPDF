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

using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using System.Collections.Generic;
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
            if (!Content.Trim().Equals(Keywords.Normal, System.StringComparison.OrdinalIgnoreCase)) return;
            if (ParentBox is not CssBox owner) return;

            if (owner.ListStyleImage is not null)
            {
                ContentImage = owner.ListStyleImage;
                _ownsContentImage = false;
                return;
            }

            var listStyleType = owner.ListStyleType;

            if (listStyleType.Equals(Keywords.Disc, System.StringComparison.OrdinalIgnoreCase) ||
                listStyleType.Equals(Keywords.Circle, System.StringComparison.OrdinalIgnoreCase) ||
                listStyleType.Equals(Keywords.Square, System.StringComparison.OrdinalIgnoreCase))
            {
                MarkerShape = listStyleType.ToLowerInvariant();

                // An outside marker's shape is explicitly centered within the owner's line box
                // (PerformLayoutImp's own MarkerShape branch below). An inside marker never reaches
                // that code - it's positioned by the ordinary inline vertical-align algorithm instead
                // (CssLayoutEngine's own line-box pass), which defaults every box to baseline. A small
                // vector shape baseline-aligned like a text glyph sits noticeably high relative to the
                // adjacent text; middle-align it against the line the same way the outside path does.
                VerticalAlign = CssProperty<CssKeywordOrValue<VerticalAlignment, LengthOrCalc>>.FromValue(
                    Keywords.Middle, new CssKeywordOrValue<VerticalAlignment, LengthOrCalc>(VerticalAlignment.Middle, null));
                return;
            }

            if (listStyleType == Keywords.None) return; // no marker at all

            if (listStyleType.Equals(Keywords.DisclosureOpen, System.StringComparison.OrdinalIgnoreCase) ||
                listStyleType.Equals(Keywords.DisclosureClosed, System.StringComparison.OrdinalIgnoreCase))
            {
                // CSS Counter Styles Level 3 §6.3: a fixed symbol, not a counted numbering style - every
                // item gets the same glyph regardless of its list-item index, so the counter value
                // passed in is irrelevant (FormatCounterValue ignores it for these two styles) and isn't
                // worth a real counter lookup. Suffix is a plain space, not the "." every other named
                // style gets below.
                Text = CssCounterEngine.FormatCounterValue(0, listStyleType) + " ";
                return;
            }

            // list-style-type: <string> - a literal marker (e.g. list-style-type: "-> ") rather than a
            // named counter style: no counting, and per CSS Lists Level 3 no automatic suffix is
            // appended (unlike every named style below, which get "."). ListStyleType stores the raw
            // CSS-OM serialization of the declared value, so a string value still carries its quotes -
            // only tokenize the (rare) case that could actually be one, rather than paying tokenizer
            // cost for every ordinary keyword value.
            if (listStyleType.Length > 0 && (listStyleType[0] == '"' || listStyleType[0] == '\''))
            {
                using var pooledTokens = CssValueParser.GetCssTokensPooled(listStyleType);
                List<Token> tokens = pooledTokens;
                if (tokens.Count == 1 && tokens[0] is { Type: TokenType.String } literalMarker)
                {
                    Text = literalMarker.Data.ToString();
                    return;
                }
            }

            var index = CssCounterEngine.GetCounter(this, Keywords.ListItem)?.Value ?? 1;

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

            if (ListStylePosition.Value != ListStylePositionMode.Outside) return;
            if (ParentBox is not CssBox owner) return;

            var word = Words.Count > 0 ? Words[0] : null;

            var width = word?.Width ?? 0;
            var height = word?.Height ?? owner.ActualFont.Height;

            // CSS 2.1 §10.8.1 / css-lists-3 §3.5: the marker sits on the baseline of the item's own
            // first line, not at its content-box top. The two coincide only while the marker's font
            // matches the item's - a ::marker { font-size } override otherwise leaves the marker's own
            // baseline below the text it is numbering, by the difference of the two ascents. The line
            // has already been through CssLayoutEngine.ApplyVerticalAlignment by the time this runs (see
            // this method's own remarks on why the marker is positioned after LayoutContents), so its
            // BaselineY is final.
            var contentTop = owner.Location.Y + owner.ActualBorderTopWidth + owner.ActualPaddingTop;
            var baselineY = OwnFirstLineBaselineOf(owner);

            double top;

            if (MarkerShape is not null)
            {
                // A vector shape has no baseline of its own; centre the (much smaller) glyph on the
                // line's own middle, so it sits level with the adjacent text the way a disc does in a
                // browser. Measured from the baseline when there is one, so it tracks a line the
                // marker's own font-size grew rather than assuming the item's font governs it.
                top = baselineY is { } shapeBaseline
                    ? shapeBaseline - owner.ActualFont.Ascent + (owner.ActualFont.Height - height) / 2
                    : contentTop + (owner.ActualFont.Height - height) / 2;
            }
            else
            {
                top = baselineY is { } textBaseline ? textBaseline - ActualFont.Ascent : contentTop;
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

        /// <summary>
        /// The baseline of the first line box the owning list item laid out <i>itself</i>, or null when it
        /// laid out none — an item whose content is block-level, or one holding nothing but replaced
        /// content.
        /// </summary>
        /// <remarks>
        /// Deliberately its own lines only, not the first baseline anywhere in its subtree (which is what
        /// <see cref="BaselineAlignment.GetItemBaselineOffset"/> gives the flex and grid engines). A
        /// descendant block's line boxes are not reliable evidence here: a column-fill attempt
        /// this item is later abandoned by still leaves lines behind in it, and a marker positioned
        /// against one of those lands in a fragmentainer that no longer holds its item, so nothing claims
        /// it and it paints on no page at all
        /// (<c>StraddlingListMarkerTests.ABlockContentItemAColumnFillAttemptAbandons_ClaimsItsMarkerExactlyOnce</c>
        /// states this directly, and fails on the wider walk). An item whose content is block-level
        /// therefore keeps the content-box top it has always used — the two coincide whenever the marker's
        /// font matches the item's, which is every case but an explicit <c>::marker</c> font override.
        /// </remarks>
        private static double? OwnFirstLineBaselineOf(CssBox owner)
        {
            foreach (var lineBox in owner.LineBoxes)
            {
                if (lineBox.BaselineY is { } baselineY) return baselineY;
            }

            return null;
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
