using System;
using System.Linq;
using System.Threading.Tasks;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Utils;

namespace PeachPDF.Html.Core.Dom
{
    internal class CssLayoutEngineFlow(CssBox FlowBox)
    {

        public static async ValueTask PerformLayout(RGraphics g, CssBox flowBox)
        {
            ArgumentNullException.ThrowIfNull(g);
            ArgumentNullException.ThrowIfNull(flowBox);

            CssLayoutEngineFlow flow = new(flowBox);
            await flow.Layout(g);
        }

        private async ValueTask Layout(RGraphics g)
        {
            //If there's just inline boxes, create LineBoxes
            if (DomUtils.ContainsInlinesOnly(FlowBox))
            {
                FlowBox.ActualBottom = FlowBox.Location.Y;
                await CssLayoutEngine.CreateLineBoxes(g, FlowBox); //This will automatically set the bottom of this block

#if DEBUG
                foreach (var lineBox in FlowBox.LineBoxes)
                {
                    Console.WriteLine($"layout linebox: {lineBox} [h: {lineBox.LineBottom}]");
                }
#endif

            }
            else if (FlowBox.Boxes.Count > 0)
            {
                foreach (var childBox in FlowBox.Boxes)
                {
                    await childBox.PerformLayout(g);
                }

                FlowBox.ActualRight = CalculateActualRight();

                if (FlowBox.Boxes.Any(b => !b.IsFloated))
                {
                    FlowBox.ActualBottom = MarginBottomCollapse();
                }
            }
        }

        /// <summary>
        /// Calculate the actual right of the box by the actual right of the child boxes if this box actual right is not set.
        /// </summary>
        /// <returns>the calculated actual right value</returns>
        private double CalculateActualRight()
        {
            if (!(FlowBox.ActualRight > 90999)) return FlowBox.ActualRight;

            var maxRight = 0d;

            double additionalMarginRight;

            foreach (var box in FlowBox.Boxes)
            {
                additionalMarginRight = box.BoxSizing switch
                {
                    CssConstants.ContentBox => 0,
                    CssConstants.BorderBox => box.ActualMarginRight,
                    _ => throw new HtmlRenderException("Unknown BoxSizing", HtmlRenderErrorType.Layout)
                };

                maxRight = Math.Max(maxRight, box.ActualRight + additionalMarginRight);
            }

            additionalMarginRight = FlowBox.BoxSizing switch
            {
                CssConstants.ContentBox => 0,
                CssConstants.BorderBox => FlowBox.ActualMarginRight,
                _ => throw new HtmlRenderException("Unknown BoxSizing", HtmlRenderErrorType.Layout)
            };

            return maxRight + FlowBox.ActualPaddingRight + additionalMarginRight + FlowBox.ActualBorderRightWidth;

        }

        /// <summary>
        /// Gets the result of collapsing the vertical margins of the two boxes
        /// </summary>
        /// <returns>Resulting bottom margin</returns>
        private double MarginBottomCollapse()
        {
            var lastNonFloatingBox = FlowBox.Boxes.Last(b => !b.IsFloated);

            double margin = 0;
            if (FlowBox.ParentBox == null || FlowBox.ParentBox.Boxes.IndexOf(FlowBox) != FlowBox.ParentBox.Boxes.Count - 1 ||
                !(FlowBox.ParentBox!.ActualMarginBottom < 0.1))
                return Math.Max(FlowBox.ActualBottom,
                    lastNonFloatingBox.ActualBottom + margin + FlowBox.ActualPaddingBottom + FlowBox.ActualBorderBottomWidth);

            var lastChildBottomMargin = lastNonFloatingBox.ActualMarginBottom;
            margin = FlowBox.Height is CssConstants.Auto ? Math.Max(FlowBox.ActualMarginBottom, lastChildBottomMargin) : lastChildBottomMargin;
            return Math.Max(FlowBox.ActualBottom, lastNonFloatingBox.ActualBottom + margin + FlowBox.ActualPaddingBottom + FlowBox.ActualBorderBottomWidth);
        }
    }
}
