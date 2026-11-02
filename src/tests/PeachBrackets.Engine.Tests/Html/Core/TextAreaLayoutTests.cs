using PeachBrackets.Html.Paint.Content;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Rendering;

namespace PeachBrackets.Engine.Tests.Html.Core
{
    public class TextAreaLayoutTests
    {
        private static (Canvas Canvas, Font Font, double Em) Setup()
        {
            var renderContext = new HtmlRenderContext();
            var canvas = PdfCanvas.CreateMeasuring(renderContext, new Size(595, 842));
            var font = renderContext.GetFont("monospace", 10, PaintFontStyle.Regular)!;
            return (canvas, font, canvas.MeasureString("0", font).Width);
        }

        private static string[] Lines(string text, IReadOnlyList<TextAreaLine> lines) => [.. lines.Select(l => text[l.Start..l.End])];

        [Fact]
        public void LinesBreakAtLineBreaksAndKeepEmptyOnes()
        {
            var (canvas, font, _) = Setup();
            const string text = "one\n\ntwo";

            Assert.Equal(new[] { "one", "", "two" }, Lines(text, TextAreaLayout.Wrap(canvas, font, text, 500)));
            Assert.Equal(new[] { "" }, Lines("", TextAreaLayout.Wrap(canvas, font, "", 500)));
        }

        [Fact]
        public void ALongLineWrapsAfterASpace()
        {
            var (canvas, font, em) = Setup();
            const string text = "aaaa bbbb cccc";

            var lines = Lines(text, TextAreaLayout.Wrap(canvas, font, text, em * 10));

            Assert.Equal(new[] { "aaaa bbbb ", "cccc" }, lines);
        }

        [Fact]
        public void AWordWiderThanTheLineIsBrokenInside()
        {
            var (canvas, font, em) = Setup();
            const string text = "abcdefghijkl";

            var lines = Lines(text, TextAreaLayout.Wrap(canvas, font, text, em * 5));

            Assert.True(lines.Length >= 2);
            Assert.Equal(text, string.Concat(lines));
            Assert.All(lines, l => Assert.True(l.Length <= 5));
        }

        [Fact]
        public void APositionBelongsToTheLaterLineAtASoftBreak()
        {
            var (canvas, font, em) = Setup();
            const string text = "aaaa bbbb cccc";
            var lines = TextAreaLayout.Wrap(canvas, font, text, em * 10);

            Assert.Equal(0, TextAreaLayout.LineOf(lines, 3));
            Assert.Equal(1, TextAreaLayout.LineOf(lines, 10));
            Assert.Equal(1, TextAreaLayout.LineOf(lines, text.Length));
        }

        [Fact]
        public void APointFindsTheNearestPositionOnItsLine()
        {
            var (canvas, font, em) = Setup();
            const string text = "abcd\nefgh";
            var lines = TextAreaLayout.Wrap(canvas, font, text, em * 20);

            Assert.Equal(2, TextAreaLayout.IndexAt(canvas, font, text, lines, (em * 2) + 1, 1));
            Assert.Equal(7, TextAreaLayout.IndexAt(canvas, font, text, lines, (em * 2) + 1, font.Height + 1));
            Assert.Equal(4, TextAreaLayout.IndexAt(canvas, font, text, lines, em * 50, 1));
            Assert.Equal(5, TextAreaLayout.IndexAt(canvas, font, text, lines, 0, 999));
            Assert.Equal(0, TextAreaLayout.IndexAt(canvas, font, string.Empty, [], 5, 5));
        }

        [Fact]
        public void TheOffsetOfAPositionIsTheWidthOfTheTextBeforeItOnItsLine()
        {
            var (canvas, font, em) = Setup();
            const string text = "abcd\nefgh";
            var lines = TextAreaLayout.Wrap(canvas, font, text, em * 20);

            Assert.Equal(0, TextAreaLayout.OffsetOf(canvas, font, text, lines[1], 5));
            Assert.Equal(em * 3, TextAreaLayout.OffsetOf(canvas, font, text, lines[1], 8), 3);
            Assert.Equal(em * 4, TextAreaLayout.OffsetOf(canvas, font, text, lines[0], 99), 3);
        }
    }
}
