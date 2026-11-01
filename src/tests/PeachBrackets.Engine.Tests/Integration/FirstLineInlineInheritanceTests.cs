using PeachBrackets.Html.Dom;
using PeachDrawing.Core;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// The parts of an element on the first line inherit from <c>::first-line</c> (CSS Pseudo-Elements 4 section 2.1.3), so an inline that declares
    /// no font of its own has the <c>::first-line</c> font and sizes the line from it (CSS 2.1 section 10.8.1), while an inline that declares one
    /// keeps its own. Text on later lines is unaffected.
    /// </summary>
    public class FirstLineInlineInheritanceTests
    {
        private const string Rule = "<style>#p::first-line { font-size: 10pt }</style>";

        private static async Task<CssBox> Layout(string inner, string pStyle = "font-size: 40pt; line-height: 1.2; width: 600pt; margin: 0")
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap($"{Rule}<p id='p' style='{pStyle}'>{inner}</p>"));
            return LayoutHarness.FindById(root, "p")!;
        }

        private static double FirstLineHeight(CssBox p) => p.LineBoxes[0].BaselineExtent!.Value.Height;

        // The word "x" held in the span; its text sits in a box of its own below it.
        private static CssRect WordX(CssBox p) => Descendants(p).SelectMany(b => b.Words).First(w => w.Text == "x");

        private static IEnumerable<CssBox> Descendants(CssBox box) =>
            box.Boxes.SelectMany(child => Descendants(child).Prepend(child));

        [Theory]
        [InlineData("text<span>x</span>more")]
        [InlineData("text<span></span>more")]
        [InlineData("text<b><i>x</i></b>more")]
        [InlineData("text<span><span></span></span>more")]
        public async Task AnInlineThatDeclaresNoFont_SizesTheLineFromTheFirstLineFont(string inner)
        {
            var plain = await Layout("text more");
            var withInline = await Layout(inner);

            Assert.Equal(12, FirstLineHeight(plain), 0.5);
            Assert.Equal(FirstLineHeight(plain), FirstLineHeight(withInline), 0.5);
        }

        [Fact]
        public async Task AnInlineThatDeclaresItsOwnFontSize_KeepsIt()
        {
            var p = await Layout("text<span style='font-size: 20pt'>x</span>more");

            // 20pt at the inherited line-height factor 1.2.
            Assert.Equal(24, FirstLineHeight(p), 0.5);

            Assert.Equal(20, WordX(p).FirstLineStyle!.ActualFont.Size, 0.5);
        }

        [Fact]
        public async Task AnInlineThatDeclaresNoFont_HasTheFirstLineFontOnItsWords()
        {
            var p = await Layout("text<span>x</span>more");

            Assert.Equal(10, WordX(p).FirstLineStyle!.ActualFont.Size, 0.5);
        }

        [Fact]
        public async Task LaterLines_AreStillSizedFromTheOrdinaryFont()
        {
            var p = await Layout("first line words here <span>span words</span> and then more words to wrap onto several lines", "font-size: 40pt; line-height: 1.2; width: 300pt; margin: 0");

            Assert.True(p.LineBoxes.Count > 1);
            Assert.Equal(12, FirstLineHeight(p), 0.5);
            Assert.Equal(48, p.LineBoxes[1].BaselineExtent!.Value.Height, 0.5);
        }

        [Fact]
        public async Task WithoutAFirstLineRule_NothingChanges()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<p id='p' style='font-size: 40pt; line-height: 1.2; width: 600pt; margin: 0'>text<span>x</span>more</p>"));
            var p = LayoutHarness.FindById(root, "p")!;

            Assert.Equal(48, FirstLineHeight(p), 0.5);
            Assert.All(Descendants(p), b => Assert.Null(b.FirstLineInlineStyle));
        }

        [Fact]
        public async Task AnInlineThatDeclaresItsOwnColor_KeepsItOnTheFirstLine()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>#p::first-line { color: rgb(255,0,0) }</style>" +
                "<p id='p' style='width: 600pt; margin: 0'>red <span style='color: rgb(0,0,255)'>blue</span> <b>bold</b></p>"));
            var p = LayoutHarness.FindById(root, "p")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, p, g);

            // One draw call per word, in order: the plain word and the inherited bold one take the ::first-line colour, the declared one its own.
            var colours = g.WordDrawStringCalls.Select(c => c.PaintColor).ToList();
            Assert.Equal([PaintColor.FromArgb(255, 0, 0), PaintColor.FromArgb(0, 0, 255), PaintColor.FromArgb(255, 0, 0)], colours);
        }
    }
}
