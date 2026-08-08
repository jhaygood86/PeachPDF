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
        /// The rule's <i>size</i> is its own — a full-width line whose height falls back to a 1px top and
        /// bottom border — but its <i>position</i> is not, so it goes through
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
        /// <c>CssLayoutEngine.FloatBox</c> at its real width. Height still comes after, because the
        /// placement writes <see cref="CssBox.ActualBottom"/> and so clears
        /// <see cref="CssBox.ActualHeight"/> — which is exactly what makes the fallback below
        /// fire on a re-layout instead of reading the previous pass's height.
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

            double height = ActualHeight;
            if (height < 1)
            {
                height = Size.Height + ActualBorderTopWidth + ActualBorderBottomWidth;
            }
            if (height < 1)
            {
                height = 2;
            }
            if (height <= 2 && ActualBorderTopWidth < 1 && ActualBorderBottomWidth < 1)
            {
                BorderTopStyle = BorderBottomStyle = CssProperty<LineStyle>.FromValue(Keywords.Solid, LineStyle.Solid);
                BorderTopWidth = "1px";
                BorderBottomWidth = "1px";
            }

            Size = new RSize(width, height);

            ActualBottom = Location.Y + ActualPaddingTop + ActualPaddingBottom + height;

            return ValueTask.CompletedTask;
        }
    }
}