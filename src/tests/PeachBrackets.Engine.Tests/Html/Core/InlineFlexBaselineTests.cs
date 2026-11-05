using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Rendering;

namespace PeachBrackets.Engine.Tests.Html.Core
{
    /// <summary>
    /// An inline flex (or grid) container sits on its line by its first item's first-line baseline (css-flexbox-1 section 8.5), as an inline-block does by its last
    /// line's: a badge or button beside text lines its label up with the text however much padding the box has.
    /// </summary>
    public class InlineFlexBaselineTests
    {
        private static IEnumerable<CssBox> Descend(CssBox box)
        {
            foreach (var child in box.Boxes)
            {
                yield return child;
                foreach (var deeper in Descend(child))
                    yield return deeper;
            }
        }

        private static async Task<(double Text, double Label)> BaselinesAsync(string display, string style)
        {
            var html = $"<!DOCTYPE html><html><body style='margin:0;font:16px/24px Arial'><p style='margin:0'>Hello <span style='display:{display};{style}'>Badge</span> world</p></body></html>";
            var renderContext = new HtmlRenderContext();
            var container = new HtmlContainerInt(renderContext) { MediaType = MediaType.Screen };
            await container.SetHtml(html, null);

            var size = new Size(600, 842);
            container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
            await container.PerformLayout(graphics);

            double Baseline(string text)
            {
                var word = Descend(container.Root!).SelectMany(b => b.Words).First(w => w.Text?.Trim() == text);
                return word.Top + word.OwnerBox.ActualFont.Ascent;
            }

            return (Baseline("Hello"), Baseline("Badge"));
        }

        [Theory]
        [InlineData("inline-block", "padding:14px 8px;font-size:12px;line-height:16px")]
        [InlineData("inline-flex", "padding:14px 8px;font-size:12px;line-height:16px")]
        [InlineData("inline-flex", "padding:4px 8px;font-size:12px;line-height:16px")]
        [InlineData("inline-flex", "padding:0 8px;height:60px;font-size:12px")]
        [InlineData("inline-grid", "padding:14px 8px;font-size:12px;line-height:16px")]
        public async Task TheLabelOfTheBoxIsOnTheTextsBaseline(string display, string style)
        {
            var (text, label) = await BaselinesAsync(display, style);

            Assert.True(Math.Abs(text - label) < 0.6, $"{display}: the surrounding text's baseline is at {text * 4 / 3:0.#}px, the box's label's at {label * 4 / 3:0.#}px");
        }
    }
}
