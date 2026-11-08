using PeachBrackets.Css;
using PeachDrawing.Core;
using PeachBrackets.Html.Parse;
using PeachBrackets.Html.Utils;
using System;
using System.Threading.Tasks;

namespace PeachBrackets.Html.Dom
{
    /// <summary>
    /// The writing-mode and direction half of the grid engine (css-grid-2 section 1.2: the grid's axes are
    /// <i>logical</i>). Columns are the inline axis and rows the block axis, so the track algorithm in
    /// <c>CssLayoutEngineGrid.cs</c> works on logical offsets and sizes; this file is the layer between that
    /// and the physical boxes, in two parts:
    /// <list type="bullet">
    /// <item><description><b><c>direction: rtl</c> under <c>horizontal-tb</c></b> is a mirror of the inline
    /// axis. The algorithm still runs left to right, and an item's margin box is reflected about the
    /// content box when it is placed (<see cref="MirrorMarginBoxX"/>), which keeps each margin on its own
    /// physical side. The physical alignment keywords <c>left</c>/<c>right</c> are mapped to the logical
    /// <c>end</c>/<c>start</c> that lands them on that side after the reflection
    /// (<see cref="LogicalizePhysicalKeyword"/>).</description></item>
    /// <item><description><b>A vertical writing mode</b> swaps which physical dimension each axis is: the
    /// inline axis is the container's height and the block axis its width. Items are sized through
    /// <c>Height</c> (inline) and <c>Width</c> (block) and positioned through
    /// <see cref="WritingModeFrame.ToPhysical(Rect)"/>, the same conversion every other vertical box uses,
    /// so the block-start edge (right for <c>vertical-rl</c>, left for <c>vertical-lr</c>) and the
    /// inline-start edge (top, or bottom under <c>direction: rtl</c>) come out of one derivation.</description></item>
    /// </list>
    /// </summary>
    internal sealed partial class CssLayoutEngineGrid
    {
        /// <summary>
        /// The logical inline offset of the physical keyword <paramref name="value"/> under
        /// <c>direction: rtl</c>: the algorithm lays out left to right and reflects afterwards, so the
        /// physical <c>left</c> must be asked for as the inline end (it is on the right of the unreflected
        /// layout) and <c>right</c> as the inline start. Every other value is already logical.
        /// </summary>
        private string LogicalizePhysicalKeyword(string value)
        {
            if (!_rtl) return value;
            if (value.Isi(Keywords.Left)) return Keywords.End;
            if (value.Isi(Keywords.Right)) return Keywords.Start;
            return value;
        }

        /// <summary>
        /// Reflects a margin box's left edge about the content box for <c>direction: rtl</c>.
        /// </summary>
        private double MirrorMarginBoxX(double x, double outerWidth) =>
            _gridBox.ClientLeft + _mirrorRight - x - outerWidth;

        /// <summary>The logical inline origin tracks are positioned from.</summary>
        private double InlineOrigin => _vertical ? 0 : _gridBox.ClientLeft;

        /// <summary>The logical block origin tracks are positioned from.</summary>
        private double BlockOrigin => _vertical ? 0 : _gridBox.ClientTop;

        /// <summary>The content box's extent along the block axis (its height, or its width when vertical).</summary>
        private double BlockContentSize => _vertical
            ? _gridBox.ClientRight - _gridBox.ClientLeft
            : _gridBox.ClientBottom - _gridBox.ClientTop;

        // ─── Vertical item geometry ────────────────────────────────────────────────

        private static double MarginsInline(CssBox box) => box.ActualMarginTop + box.ActualMarginBottom;

        private static double MarginsBlock(CssBox box) => box.ActualMarginLeft + box.ActualMarginRight;

        private static double PaddingBorderInline(CssBox box) =>
            box.ActualPaddingTop + box.ActualPaddingBottom + box.ActualBorderTopWidth + box.ActualBorderBottomWidth;

        /// <summary>
        /// Replaces an item's percentage <c>height</c>, <c>min-height</c> and <c>max-height</c> - the
        /// inline-size properties under a vertical writing mode - with the lengths they resolve to against
        /// its grid area (css-grid-2 section 6.2). The vertical twin of <c>ResolveAreaPercentages</c>.
        /// </summary>
        private static Action ResolveAreaInlinePercentages(CssBox box, double areaInlineContent)
        {
            var height = box.Height;
            var minHeight = box.MinHeight;
            var maxHeight = box.MaxHeight;

            string Resolve(string value) =>
                value.Contains('%') && CssValueParser.IsValidLength(value)
                    ? FormatLayoutUnits(CssValueParser.ParseLength(value, areaInlineContent, box), box)
                    : value;

            box.Height = Resolve(height);
            box.MinHeight = Resolve(minHeight);
            box.MaxHeight = Resolve(maxHeight);

            return () =>
            {
                box.Height = height;
                box.MinHeight = minHeight;
                box.MaxHeight = maxHeight;
            };
        }

        /// <summary>
        /// The vertical twin of <c>MeasureItemHeight</c>: the item's outer extent along the block axis (its
        /// width) once it is laid out with <paramref name="cellInline"/> of inline space, without leaving it
        /// placed.
        /// </summary>
        private async ValueTask<double> MeasureItemBlockSizeVertical(Canvas g, CssBox box, double cellInline)
        {
            var inlineContent = Math.Max(0, cellInline - MarginsInline(box) - box.ActualBoxSizeIncludedHeight);
            var restorePercentages = ResolveAreaInlinePercentages(box, inlineContent);
            var savedHeight = box.Height;

            if (box.Height == Keywords.Auto)
                box.Height = FormatLayoutUnits(inlineContent, box);

            box.Location = new PaintPoint(_gridBox.ClientLeft, _gridBox.ClientTop);
            box.ActualBottom = box.Location.Y;
            box.RectanglesReset();
            await PerformLayoutBlockified(g, box);

            box.Height = savedHeight;
            restorePercentages();
            return box.ActualBoxSizingWidth + MarginsBlock(box);
        }

        /// <summary>
        /// The vertical twin of <c>PlaceItemInCell</c>. <paramref name="justify"/> aligns along the inline
        /// axis (top to bottom) and <paramref name="align"/> along the block axis; the cell is given in
        /// logical coordinates and converted to a physical rectangle by the container's frame.
        /// </summary>
        private async ValueTask PlaceItemInCellVertical(Canvas g, CssBox box, double cellInlineStart, double cellBlockStart,
            double cellInline, double cellBlock, string justify, string align)
        {
            var autoInline = box.Height == Keywords.Auto;
            var autoBlock = box.Width == Keywords.Auto;
            var stretchInline = autoInline && IsStretch(justify);
            var stretchBlock = autoBlock && IsStretch(align);

            var inlineMargins = MarginsInline(box);
            var restorePercentages = ResolveAreaInlinePercentages(
                box, Math.Max(0, cellInline - inlineMargins - PaddingBorderInline(box)));
            var savedWidth = box.Width;
            var savedHeight = box.Height;

            if (autoInline)
            {
                double borderBox;
                if (stretchInline)
                {
                    borderBox = Math.Max(0, cellInline - inlineMargins);
                }
                else
                {
                    // fit-content: the max-content extent, no smaller than the min-content one and no larger
                    // than the area unless the min-content one is.
                    var (min, max) = await MeasureInlineExtentVertical(g, box);
                    borderBox = Math.Min(max, Math.Max(min, cellInline - inlineMargins));
                }

                box.Height = FormatLayoutUnits(Math.Max(0, borderBox - box.ActualBoxSizeIncludedHeight), box);
            }

            if (stretchBlock)
            {
                box.Width = FormatLayoutUnits(
                    Math.Max(0, cellBlock - MarginsBlock(box) - box.ActualBoxSizeIncludedWidth), box);
            }

            box.Location = new PaintPoint(_gridBox.ClientLeft, _gridBox.ClientTop);
            box.ActualBottom = box.Location.Y;
            box.RectanglesReset();
            await PerformLayoutBlockified(g, box);

            if (autoInline) box.Height = savedHeight;
            if (stretchBlock) box.Width = savedWidth;
            restorePercentages();

            var outerInline = box.ActualBoxSizingHeight + inlineMargins;
            var outerBlock = box.ActualBoxSizingWidth + MarginsBlock(box);

            var logical = new Rect(
                cellInlineStart + AlignmentOffset(justify, cellInline, outerInline),
                cellBlockStart + AlignmentOffset(align, cellBlock, outerBlock),
                outerInline, outerBlock);
            var physical = _frame.ToPhysical(logical);

            var dx = physical.X + box.ActualMarginLeft - box.Location.X;
            var dy = physical.Y + box.ActualMarginTop - box.Location.Y;
            if (Math.Abs(dx) > 0.01) box.OffsetLeft(dx);
            if (Math.Abs(dy) > 0.01) box.OffsetTop(dy);
        }

        /// <summary>
        /// An item's border-box min-content and max-content extents along the inline axis of a vertical
        /// grid (its height), measured by laying it out: unconstrained it settles at its max-content
        /// extent, and pinned to nothing every line holds one word, so the content reaches as far as the
        /// longest unbreakable run. An explicit <c>height</c> supplies both.
        /// </summary>
        private async ValueTask<(double Min, double Max)> MeasureInlineExtentVertical(Canvas g, CssBox box)
        {
            if (CssValueParser.IsValidLength(box.Height))
            {
                var declared = CssValueParser.ParseLength(box.Height, 0, box) + box.ActualBoxSizeIncludedHeight;
                return (declared, declared);
            }

            var savedHeight = box.Height;

            async ValueTask LayOutAsync()
            {
                box.Location = new PaintPoint(_gridBox.ClientLeft, _gridBox.ClientTop);
                box.ActualBottom = box.Location.Y;
                box.RectanglesReset();
                await PerformLayoutBlockified(g, box);
            }

            await LayOutAsync();
            var max = box.ActualBoxSizingHeight;

            double min;
            if (!box.WhiteSpacePermitsWrapping)
            {
                min = max;
            }
            else
            {
                box.Height = FormatLayoutUnits(0, box);
                await LayOutAsync();
                var reach = CssBox.GetMaximumBottom(box, box.Location.Y) - box.Location.Y;
                min = Math.Min(max, reach + box.ActualPaddingBottom + box.ActualBorderBottomWidth);
                box.Height = savedHeight;
            }

            return (min, max);
        }

        /// <summary>
        /// The vertical twin of <c>MeasureItemContribution</c>: an item's outer min-content and max-content
        /// contributions to its column track, which a vertical grid measures along the item's height.
        /// </summary>
        private async ValueTask<(double Min, double Max)> MeasureInlineContributionVertical(Canvas g, CssBox box)
        {
            var (min, max) = await MeasureInlineExtentVertical(g, box);
            var margins = MarginsInline(box);
            return (min + margins, max + margins);
        }

        /// <summary>
        /// The vertical twin of <c>AutoMinimumOf</c>: an item's minimum contribution to a track whose minimum
        /// sizing function is <c>auto</c>, using <c>min-height</c> (the inline-size minimum) and the item's
        /// vertical margin, padding and border.
        /// </summary>
        private static double AutoMinimumOfVertical(CssBox box, double minContentOuter, double basis)
        {
            var outerExtra = MarginsInline(box) + PaddingBorderInline(box);

            if (box.Overflow.Value != Css.Overflow.Visible) return outerExtra;

            if (!string.Equals(box.MinHeight, Keywords.Auto, StringComparison.OrdinalIgnoreCase))
                return CssValueParser.IsValidLength(box.MinHeight)
                    ? CssValueParser.ParseLength(box.MinHeight, basis, box) + outerExtra
                    : minContentOuter;

            return minContentOuter;
        }
    }
}
