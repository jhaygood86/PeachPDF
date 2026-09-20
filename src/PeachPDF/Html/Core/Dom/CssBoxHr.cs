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
using System.Threading.Tasks;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// CSS box for hr element.
    /// </summary>
    internal sealed class CssBoxHr : CssBox
    {
        /// <summary>
        /// Init.
        /// </summary>
        /// <param name="parent">the parent box of this box</param>
        /// <param name="tag">the html tag data of this box</param>
        public CssBoxHr(CssBox? parent, HtmlTag tag)
            : base(parent, tag)
        {
            Display = CssProperty<DisplayMode>.FromValue(Keywords.Block, DisplayMode.Block);
        }

        /// <summary>
        /// Measures the bounds of box and children, recursively.<br/>
        /// Performs layout of the DOM structure creating lines by set bounds restrictions.
        /// </summary>
        /// <param name="g">Device context to use</param>
        /// <param name="frame">the frame driving this pass — unused here; the rule reaches it through
        /// <see cref="CssBox.PlaceAsBlockChild"/>, which is the one dispatcher every block-level box's
        /// position goes through</param>
        /// <param name="framePlacesChild">
        /// unused: the rule replaces the generic block pass rather than being driven through its phases, so
        /// the frame has no placement phase here to skip. It has no prologue either, and its inline size is
        /// a formula of its own rather than <c>CssLayoutEngine.GetBoxWidth</c>'s answer.
        /// </param>
        /// <remarks>
        /// <para>
        /// The rule's <i>size</i> is its own — a full-width line whose height falls back to its own two
        /// horizontal border widths, and to two units when it has none — but its <i>position</i> is not, so
        /// it goes through
        /// <see cref="CssBox.PlaceAsBlockChild"/> like every other block-level box rather than through a
        /// copy of that formula. The copy is what let three placement rules drift: it read the
        /// predecessor's <c>ActualBottom</c> rather than its static one (so a relatively-positioned
        /// sibling dragged the rule along, against
        /// <see href="https://www.w3.org/TR/CSS21/visuren.html#relative-positioning">CSS 2.1 §9.4.3</see>),
        /// it asked for the previous sibling with floats <i>included</i> (so a floated sibling became its
        /// margin-collapse partner, against
        /// <see href="https://www.w3.org/TR/CSS21/box.html#collapsing-margins">§8.3.1</see>), and it added
        /// that sibling's bottom border on top of a value that already contains it.
        /// </para>
        /// <para>
        /// Width is resolved before the placement rather than after it, so a floated rule reaches
        /// <c>CssLayoutEngine.FloatBox</c> at its real width. Height comes after, and must not be read
        /// back off this box: the placement writes <see cref="CssBox.ActualBottom"/>, whose setter
        /// leaves <see cref="CssBox.Size"/>'s height at minus this box's own padding and borders, so
        /// <see cref="CssBox.ActualHeight"/> reads 0 here for every rule. It is resolved through
        /// <c>CssLayoutEngine.GetBoxHeight</c> instead — see the height block below.
        /// </para>
        /// </remarks>
        protected override ValueTask PerformLayoutImp(RGraphics g, CssBox frame, bool framePlacesChild)
        {
            if (DerivedStyle.ActualDisplay == Keywords.None)
                return ValueTask.CompletedTask;

            // The rule has no content to resume into, so the only pass state it carries is a break-before
            // request the frame may have made on an earlier pass. Clearing it here is what lets the
            // resumed pass place the rule instead of asking for the same break again and making no
            // progress — which is the driver's own backstop, and how the omission announced itself.
            BeginLayoutPass();

            // This box's own pass never routes through CssBox.BeginBlockPass (there is no prologue/
            // placement/content split for a rule), so nothing else clears CssBox._awaitingRefill for it -
            // without this, a container resetting this rule once and then genuinely laying it out again
            // here would leave a later reset skipped as a stale no-op.
            _awaitingRefill = false;

            RectanglesReset();

            //width at 100% (or auto)
            double minwidth = GetMinimumWidth();
            double width = ContainingBlock.Size.Width
                           - ContainingBlock.ActualPaddingLeft - ContainingBlock.ActualPaddingRight
                           - ContainingBlock.ActualBorderLeftWidth - ContainingBlock.ActualBorderRightWidth
                           - ActualMarginLeft - ActualMarginRight - ActualBorderLeftWidth - ActualBorderRightWidth;

            //Check width if not auto
            if (Width != Keywords.Auto && !string.IsNullOrEmpty(Width))
            {
                width = CssValueParser.ParseLength(Width, width, this);
            }

            if (width < minwidth || width >= 9999)
                width = minwidth;

            Size = new RSize(width, Size.Height);

            PlaceAsBlockChild();

            // The frame may conclude the break falls before the rule, in which case it has no position
            // in the fragmentainer being filled and nothing more to resolve here.
            if (RequestedBreakBeforeTop is not null)
                return ValueTask.CompletedTask;

            // CssLayoutEngine.GetBoxHeight is the one resolver for a declared height - the same one
            // CssLayoutEngine.ApplyHeight uses in the epilogue - and, unlike ActualHeight, it does not
            // read back Size.Height. That matters here: PlaceAsBlockChild above has just written
            // ActualBottom = Location.Y, whose setter stores Size.Height = value - borders - Location.Y,
            // so Size.Height is exactly -(borderTop + borderBottom) and ActualHeight reads 0 for every
            // rule, declared height or not. Resolving it here rather than leaving it to the epilogue is
            // what puts the real bottom in place before the frame commits the NEXT sibling's offset
            // against it (issue #1229).
            // Border-box, already including this rule's own padding and borders - GetBoxHeight adds
            // ActualBoxSizeIncludedHeight itself. Adding padding again below is what double-counted it.
            double height = CssLayoutEngine.GetBoxHeight(this) ?? 0;

            // No declared height: the rule is exactly its own padding and borders, per CSS 2.1
            // §10.6.3's used content height of 0 for an auto-height block with no in-flow children.
            // The old spelling added the borders to an already-negated Size.Height, which cancels to
            // zero every time - so this branch was unreachable and every rule fell through to the 2
            // below, which is what left the remainder painting as a gap between the two borders
            // (issue #1232).
            //
            // Deliberately a fallback, not a floor. Flooring every height at the box's own edges also
            // looks right, and gives the same answer on every case here bar one: `box-sizing:
            // border-box; height: 5px; padding: 5px`, whose border box is smaller than its own edges.
            // GetBoxHeight returns that height as declared, and ApplyHeight re-runs GetBoxHeight in the
            // epilogue - so a floor applied only here would put the flow at the floored value while the
            // paint stayed at the declared one. Whether GetBoxHeight should clamp that case at all is
            // its own question; what this must not do is answer it differently from the paint.
            var ownEdges = ActualBorderTopWidth + ActualBorderBottomWidth
                           + ActualPaddingTop + ActualPaddingBottom;

            if (height <= 0)
            {
                height = ownEdges;
            }

            // A rule with no height, no border and no padding still needs to exist: a zero-size box
            // emits no fragment at all, so it would not merely be invisible, it would be absent from
            // the fragment tree. Kept at the nominal 2 units it has always had. This is the only branch
            // that still reaches that constant, and the only case where the rule's box (2) and the flow
            // advance it produces (0, since a borderless rule collapses through) disagree - unchanged
            // from before this fix, and invisible, because nothing paints either way.
            //
            // `<= 0`, not the old `< 1`: while ActualHeight was always 0 here that threshold was dead
            // code, but real values flow through it now and `< 1` would round a genuinely small rule
            // (`border: 0; height: 0.5pt`, or a 1px top border alone at 0.75pt) up to 2.
            if (height <= 0)
            {
                height = 2;
            }

            Size = new RSize(width, height);

            ActualBottom = Location.Y + height;

            // GetBoxHeight deliberately does not consider min/max-height, and ApplyHeight's own clamp
            // runs in the epilogue - after the frame has already committed the next sibling's offset
            // against this bottom. Clamping here is what keeps `max-height` out of the flow arithmetic
            // it is supposed to govern (CSS 2.1 §10.7).
            CssLayoutEngine.ClampToMaxHeight(this);

            return ValueTask.CompletedTask;
        }
    }
}