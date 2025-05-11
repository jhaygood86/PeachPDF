using System;
using System.Threading.Tasks;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
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

            FlexBox.Boxes.ForEach(box =>
            {
                box.Size = box.Size with { Height = flexItemHeight - box.ActualBoxSizeIncludedHeight };
            });

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
    }
}
