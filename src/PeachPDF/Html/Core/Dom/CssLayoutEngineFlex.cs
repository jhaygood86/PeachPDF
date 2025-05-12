using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;

namespace PeachPDF.Html.Core.Dom
{
    internal class CssLayoutEngineFlex(CssBox FlexBox)
    {
        public static async ValueTask PerformLayout(RGraphics g, CssBox flexBox)
        {
            CssLayoutEngineFlex flex = new(flexBox);
            await flex.Layout(g);

        }

        public async Task Layout(RGraphics g)
        {
            var start = GetStartingPoint();

            var flexItemHeight = 0d;

            foreach (var box in FlexBox.Boxes)
            {
                box.Location = await GetNextPoint(g, box, start);

                await box.PerformLayout(g);

                if (box.ActualHeight > flexItemHeight)
                {
                    flexItemHeight = box.ActualHeight;
                }
            }

            await DistributePositiveSpace(g);
            await DistributeNegativeSpace(g);

            FlexBox.Boxes.ForEach(box =>
            {
                box.Size = box.Size with { Height = flexItemHeight - box.ActualBoxSizeIncludedHeight };
            });
        }

        private static async ValueTask<double> SumAsync(IEnumerable<CssBox> boxes, Func<CssBox, ValueTask<double>> expression)
        {
            double sum = 0;

            foreach (var box in boxes)
            {
                var add = await expression(box);
                sum += add;
            }

            return sum;
        }

        private RPoint GetStartingPoint()
        {
            return FlexBox.FlexDirection switch
            {
                CssConstants.Row => new RPoint(FlexBox.ClientLeft, FlexBox.ClientTop),
                CssConstants.RowReverse => new RPoint(FlexBox.ClientRight, FlexBox.ClientTop),
                _ => throw new NotImplementedException($"Unimplemented flex-direction: {FlexBox.FlexDirection}")
            };
        }

        private async ValueTask<RPoint> GetNextPoint(RGraphics g, CssBox box, RPoint currentPoint)
        {
            await box.PerformLayout(g);

            var prevBox = DomUtils.GetPreviousSibling(box);

            if (prevBox is not null)
            {
                currentPoint = FlexBox.FlexDirection switch
                {
                    CssConstants.Row => currentPoint with { X = prevBox.Location.X + prevBox.ActualMarginLeft + prevBox.ActualWidth + prevBox.ActualMarginRight },
                    CssConstants.RowReverse => currentPoint with { X = prevBox.Location.X - prevBox.ActualMarginLeft },
                    _ => throw new NotImplementedException($"Unimplemented flex-direction: {FlexBox.FlexDirection}")
                };
            }

            var left = currentPoint.X;
            var top = currentPoint.Y;

            left = FlexBox.FlexDirection switch
            {
                CssConstants.Row => left + box.ActualMarginLeft,
                CssConstants.RowReverse => left - box.ActualMarginRight - box.ActualWidth - box.ActualMarginLeft,
                CssConstants.Column or CssConstants.ColumnReverse => left,
                _ => throw new NotImplementedException($"Unimplemented flex-direction: {FlexBox.FlexDirection}")
            };

            return new RPoint(left, top);
        }

        private async ValueTask DistributePositiveSpace(RGraphics g)
        {
            var usedWidth = await SumAsync(FlexBox.Boxes, b => GetHypotheticalMainWidth(g,b));

            if (usedWidth >= FlexBox.ContentAreaWidth)
            {
                return;
            }

            foreach (var box in FlexBox.Boxes)
            {
                var flexBaseSize = await GetFlexBasisWidth(g, box);
                var hypotheticalMainSize = await GetHypotheticalMainWidth(g, box);

                if (flexBaseSize > hypotheticalMainSize)
                {
                    box.IsFlexFrozen = true;
                }
            }

            while (FlexBox.Boxes.Any(b => !b.IsFlexFrozen))
            {
                usedWidth = FlexBox.Boxes.Sum(b => b.ActualWidth + b.ActualMarginLeft + b.ActualMarginRight);

                var initialRemainingWidth = FlexBox.ContentAreaWidth - usedWidth;
                var flexGrowSums = FlexBox.Boxes.Where(b => !b.IsFlexFrozen).Sum(b => double.Parse(b.FlexGrow));

                var remainingWidth = flexGrowSums > 1 ? initialRemainingWidth : 

                var start = GetStartingPoint();
            }

            foreach (var box in FlexBox.Boxes)
            {
                var flexGrow = double.Parse(box.FlexGrow);
                box.FlexAdditionalSpace = flexGrow > 0 ? remainingWidth * (flexGrow / flexGrowSums) : 0;

                box.Location = await GetNextPoint(g, box, start);
                await box.PerformLayout(g);
            }
        }

        private async ValueTask DistributeNegativeSpace(RGraphics g)
        {
            var contentAreaWidth = FlexBox.ClientRight - FlexBox.ClientLeft - FlexBox.ActualMarginLeft - FlexBox.ActualMarginRight;
            var usedWidth = FlexBox.Boxes.Sum(b => b.ActualWidth + b.ActualMarginLeft + b.ActualMarginRight);

            if (contentAreaWidth >= usedWidth)
            {
                return;
            }

            var remainingWidth = usedWidth - contentAreaWidth;

            var flexShrinkSums = FlexBox.Boxes.Sum(b => double.Parse(b.FlexShrink) * b.Size.Width);

            var start = GetStartingPoint();

            foreach (var box in FlexBox.Boxes)
            {
                var flexShrink = double.Parse(box.FlexShrink) * box.Size.Width;
                box.FlexAdditionalSpace = flexShrink > 0 ? -remainingWidth * (flexShrink / flexShrinkSums) : 0;

                box.Location = await GetNextPoint(g, box, start);
                await box.PerformLayout(g);
            }
        } 

        private static async ValueTask<double> GetFlexBasisWidth(RGraphics g, CssBox box)
        {
            if (CssValueParser.IsValidLength(box.FlexBasis))
            {
                return CssValueParser.ParseLength(box.FlexBasis, box.ContentAreaWidth, box);
            }

            if (box.FlexBasis is CssConstants.Content || box is { FlexBasis: CssConstants.Auto, Width: CssConstants.Auto })
            {
                return await CssLayoutEngine.GetFitContentWidth(g, box, box.ContentAreaWidth);
            }

            if (box is { FlexBasis: CssConstants.Auto, Width: not CssConstants.Auto })
            {
                return CssValueParser.ParseLength(box.Width, box.ContentAreaWidth, box);
            }

            throw new NotImplementedException($"Unknown flex-basis: {box.FlexBasis}");
        }

        private static async ValueTask<double> GetHypotheticalMainWidth(RGraphics g, CssBox box)
        {
            var flexBasisWidth = await GetFlexBasisWidth(g, box);

            if (box.MinWidth is not CssConstants.Auto)
            {
                var minWidth = CssValueParser.ParseLength(box.MinWidth, box.ContentAreaWidth, box);

                if (minWidth > flexBasisWidth)
                {
                    return minWidth;
                }
            }

            if (box.MaxWidth is not CssConstants.None)
            {
                var maxWidth = CssValueParser.ParseLength(box.MaxWidth, box.ContentAreaWidth, box);

                if (flexBasisWidth > maxWidth)
                {
                    return maxWidth;
                }
            }

            return flexBasisWidth;
        }
    }
}
