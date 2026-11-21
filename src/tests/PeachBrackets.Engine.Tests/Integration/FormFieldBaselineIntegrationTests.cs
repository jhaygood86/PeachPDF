using PeachDrawing.Core;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Utils;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// Where a text entry or drop-down sits on its line. HTML renders it as an <c>inline-block</c> depicting a one-line text control, and CSS 2.1 section
    /// 10.8.1 gives an inline-block the baseline of its last line box - here, its line of text - so a label beside it shares that baseline, and the line is
    /// exactly as tall as the control: its border and padding are inside the box, not hanging below the baseline a second time. A checkbox or radio has no text
    /// line and stays on its bottom margin edge.
    /// </summary>
    public class FormFieldBaselineIntegrationTests
    {
        // A 9pt body like the forms showcase: the strut is smaller than the control above and below the baseline, so the control sizes the line.
        const string Page = "<style>body{font:9pt Arial;margin:0}.line{margin:0}label{display:inline-block;width:130pt}</style>";

        static async Task<(CssBox Line, CssBox Label, CssBoxFormField Field, Rect Rect)> LayOut(string line, string fieldId = "f")
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(Page + "<div id='line' class='line'>" + line + "</div><div id='next'>x</div>"));
            var field = Assert.IsType<CssBoxFormField>(LayoutHarness.FindById(root, fieldId));
            return (LayoutHarness.FindById(root, "line")!, LayoutHarness.FindById(root, "l")!, field, CommonUtils.GetFirstValueOrDefault(field.Rectangles, field.Bounds));
        }

        static double HeightOf(CssBox box) => box.ActualBottom - box.Location.Y;

        static CssRect? FirstWordOf(CssBox box)
        {
            if (box.Words.Count > 0) return box.Words[0];
            foreach (var child in box.Boxes)
                if (FirstWordOf(child) is { } word) return word;
            return null;
        }

        [Theory]
        [InlineData("<input id='f' type='text' value='Jane'>")]
        [InlineData("<input id='f' type='password' value='Jane'>")]
        [InlineData("<select id='f'><option>Jane</option></select>")]
        public async Task ALabelBesideATextControlSharesItsTextBaseline(string control)
        {
            var (_, label, field, rect) = await LayOut("<label id='l'>Full name</label>" + control);

            var labelBaseline = FirstWordOf(label)!.Top + label.ActualFont.Ascent;

            Assert.Equal(field.TextLineBaseline(rect), labelBaseline, 0.01);
        }

        [Fact]
        public async Task ALineHoldingATextControlIsAsTallAsTheControl()
        {
            var (line, _, _, rect) = await LayOut("<label id='l'>Full name</label><input id='f' type='text' value='Jane'>");

            Assert.Equal(rect.Height, HeightOf(line), 0.01);
        }

        [Fact]
        public async Task ADeepBottomPaddingAndBorderOfATextControlAreCountedOnce()
        {
            var plain = await LayOut("<label id='l'>Full name</label><input id='f' type='text' value='Jane' style='padding:1pt 2pt;border-width:0.75pt'>");
            var deep = await LayOut("<label id='l'>Full name</label><input id='f' type='text' value='Jane' style='padding:1pt 2pt 12pt;border-width:0.75pt 0.75pt 4pt'>");

            // The extra 14.25pt below is inside the control's box, which still is the whole line - not another 14.25pt under it.
            Assert.Equal(plain.Rect.Height + 14.25, deep.Rect.Height, 0.01);
            Assert.Equal(deep.Rect.Height, HeightOf(deep.Line), 0.01);
        }

        [Theory]
        [InlineData("checkbox")]
        [InlineData("radio")]
        public async Task ACheckboxOrRadioStaysOnItsBottomMarginEdge(string type)
        {
            var (_, label, field, rect) = await LayOut($"<label id='l'>Subscribe</label><input id='f' type='{type}'>");

            // CSS 2.1 section 10.8.1: an inline-block with no line box of its own - "a single checkbox control, with no label" - rests on its bottom margin edge.
            Assert.Equal(rect.Bottom + field.ActualMarginBottom, FirstWordOf(label)!.Top + label.ActualFont.Ascent, 0.01);
        }
    }
}
