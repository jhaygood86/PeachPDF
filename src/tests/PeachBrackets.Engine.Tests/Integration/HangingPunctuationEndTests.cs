using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// <c>hanging-punctuation: allow-end | force-end | last</c> (CSS Text 3 section 8.1): a stop, comma,
    /// closing bracket or closing quote at a line's end edge is not measured, so it overhangs the edge.
    /// Widths are stated in units of one monospace character, measured rather than assumed.
    /// </summary>
    public class HangingPunctuationEndTests
    {
        private static async Task<double> CharWidth() => (await Layout("", "a", "200pt")).Words[0].Width;

        private static string Pt(double units, double u) => $"{(units * u).ToString(System.Globalization.CultureInfo.InvariantCulture)}pt";

        private static async Task<(CssBox Box, List<CssRect> Words)> Layout(string style, string html, string width = "66pt")
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='d' style='width:{width};font-size:10pt;font-family:monospace;margin:0;padding:0;{style}'>{html}</div>"));
            var box = LayoutHarness.FindById(root, "d")!;
            var words = new List<CssRect>();
            Collect(box, words);
            return (box, words);
        }

        private static void Collect(CssBox box, List<CssRect> words)
        {
            words.AddRange(box.Words.Where(w => !w.IsSpaces));
            foreach (var child in box.Boxes) Collect(child, words);
        }

        private static int LineCount(CssBox box) => box.LineBoxes.Count;

        [Fact]
        public async Task WithoutTheProperty_ACommaThatOverflowsWraps()
        {
            var (box, words) = await Layout("", "aa bbbbbbbb,", Pt(11.5, await CharWidth()));
            Assert.Equal(2, LineCount(box));
            Assert.Equal(0d, words[1].HungEndWidth);
        }

        [Theory]
        [InlineData("allow-end")]
        [InlineData("force-end")]
        public async Task AStopThatWouldOverflowByItself_HangsAndTheWordStaysOnTheLine(string value)
        {
            var (box, words) = await Layout($"hanging-punctuation:{value}", "aa bbbbbbbb,", Pt(11.5, await CharWidth()));

            Assert.Equal(1, LineCount(box));
            Assert.Equal(await CharWidth(), words[1].HungEndWidth, 0.01);
            Assert.True(words[1].Right > box.ClientRight + 1, $"right {words[1].Right}, edge {box.ClientRight}");
        }

        [Fact]
        public async Task AWordWhoseBodyOverflows_StillWraps()
        {
            var (box, _) = await Layout("hanging-punctuation:force-end", "aa bbbbbbbbb,", Pt(11.5, await CharWidth()));
            Assert.Equal(2, LineCount(box));
        }

        [Fact]
        public async Task AllowEnd_DoesNotHangAStopThatFits()
        {
            var (_, words) = await Layout("hanging-punctuation:allow-end", "aa bb, cc", "200pt");
            Assert.All(words, w => Assert.Equal(0d, w.HungEndWidth));
        }

        [Fact]
        public async Task ForceEnd_HangsTheStopEndingAFittingLine_AndRightAlignmentLeavesItOut()
        {
            var (box, words) = await Layout("hanging-punctuation:force-end;text-align:right", "aa bb.", "200pt");

            Assert.Equal(await CharWidth(), words[^1].HungEndWidth, 0.01);
            Assert.Equal(box.ClientRight + await CharWidth(), words[^1].Right, 0.01);
        }

        [Fact]
        public async Task ForceEnd_ADotInTheMiddleOfALineDoesNotHang()
        {
            var (_, words) = await Layout("hanging-punctuation:force-end;text-align:right", "a.b c.d", "200pt");
            Assert.All(words, w => Assert.Equal(0d, w.HungEndWidth));
        }

        [Fact]
        public async Task ForceEnd_JustificationStretchesToTheEdgeWithoutTheHungStop()
        {
            var (box, words) = await Layout("hanging-punctuation:force-end;text-align:justify",
                "aa bb cc dd ee ff gg hh ii jj kk ll mm nn oo.", "100pt");

            var firstLineEnd = words.Where(w => w.Top == words[0].Top).Max(w => w.Right);
            Assert.Equal(box.ClientRight, firstLineEnd, 0.01);
            Assert.Equal(await CharWidth(), words[^1].HungEndWidth, 0.01);
        }

        [Fact]
        public async Task Last_HangsAClosingBracketOfTheLastLineOnly()
        {
            var (box, words) = await Layout("hanging-punctuation:last;text-align:right",
                "(aa) bb (cc) dd (ee) ff (gg)", "60pt");

            Assert.True(LineCount(box) > 1);
            Assert.Equal(await CharWidth(), words[^1].HungEndWidth, 0.01);
            Assert.Equal(box.ClientRight + await CharWidth(), words[^1].Right, 0.01);
            Assert.All(words.Take(words.Count - 1), w => Assert.Equal(0d, w.HungEndWidth));
        }

        [Theory]
        [InlineData("\"hi\"")]
        [InlineData("“hi”")]
        public async Task Last_HangsAClosingQuote(string text)
        {
            var (box, words) = await Layout("hanging-punctuation:last;text-align:right", text, "100pt");
            Assert.Equal(await CharWidth(), words[^1].HungEndWidth, 0.01);
            Assert.Equal(box.ClientRight + await CharWidth(), words[^1].Right, 0.01);
        }

        [Fact]
        public async Task ABorderClosingTheInline_BlocksHanging()
        {
            var (_, words) = await Layout("hanging-punctuation:force-end",
                "aa <span style='padding-right:3pt'>bb,</span>", "200pt");
            Assert.All(words, w => Assert.Equal(0d, w.HungEndWidth));
        }

        [Fact]
        public async Task RightToLeftLines_AreNotHung()
        {
            var (_, words) = await Layout("hanging-punctuation:force-end;direction:rtl", "aa bb.", "200pt");
            Assert.All(words, w => Assert.Equal(0d, w.HungEndWidth));
        }
    }
}
