using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>css-text-4: <c>white-space-collapse</c>, <c>text-wrap-mode</c> and <c>text-wrap-style</c> on the box tree.</summary>
    public class TextWrapLayoutTests
    {
        private const string Long = "alpha beta gamma delta epsilon zeta eta theta iota kappa lambda mu nu xi omicron";

        private static async Task<PeachBrackets.Html.Dom.CssBox> LayoutDiv(string style, string text = Long)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap($"<div id='d' style='width:200pt;font-size:12pt;font-family:monospace;{style}'>{text}</div>"));
            return LayoutHarness.FindById(root, "d")!;
        }

        [Fact]
        public async Task TextWrapModeNowrap_KeepsOneLine()
        {
            Assert.Single((await LayoutDiv("text-wrap-mode:nowrap")).LineBoxes);
        }

        [Fact]
        public async Task TextWrapShorthandNowrap_KeepsOneLine()
        {
            Assert.Single((await LayoutDiv("text-wrap:nowrap")).LineBoxes);
        }

        [Fact]
        public async Task Wrap_WrapsLongText()
        {
            Assert.True((await LayoutDiv("")).LineBoxes.Count > 1);
        }

        [Fact]
        public async Task PreserveBreaks_KeepsNewlinesButCollapsesSpaces()
        {
            var d = await LayoutDiv("white-space-collapse:preserve-breaks", "a   b\nc");
            Assert.Equal(2, d.LineBoxes.Count);
            Assert.DoesNotContain(d.LineBoxes[0].Words, w => w.IsSpaces);
        }

        [Fact]
        public async Task Preserve_KeepsRunsOfSpaces()
        {
            var d = await LayoutDiv("white-space-collapse:preserve", "a   b");
            Assert.Contains(d.LineBoxes[0].Words, w => w.IsSpaces);
        }

        [Fact]
        public async Task PreserveSpaces_KeepsSpacesButNotNewlines()
        {
            var d = await LayoutDiv("white-space-collapse:preserve-spaces", "a   b\nc");
            Assert.Single(d.LineBoxes);
            Assert.Contains(d.LineBoxes[0].Words, w => w.IsSpaces);
        }

        [Fact]
        public async Task BreakSpaces_PreservesSpacesAndNewlines()
        {
            var d = await LayoutDiv("white-space-collapse:break-spaces", "a   b\nc");
            Assert.Equal(2, d.LineBoxes.Count);
            Assert.Contains(d.LineBoxes[0].Words, w => w.IsSpaces);
        }

        [Fact]
        public async Task PreserveSpaces_NewlineBecomesAPreservedSpace()
        {
            var d = await LayoutDiv("white-space-collapse:preserve-spaces", "a\nb");
            Assert.Single(d.LineBoxes);
            Assert.Equal(3, d.LineBoxes[0].Words.Count);
            Assert.True(d.LineBoxes[0].Words[1].IsSpaces);
        }

        [Fact]
        public async Task BreakSpaces_PreservedSpacesDoNotHang()
        {
            // pre-wrap lets the trailing space hang past the edge; break-spaces wraps it onto the next line.
            var hang = await LayoutDiv("width:60pt;white-space:pre-wrap", "aaaaa     bbbbb");
            var breaking = await LayoutDiv("width:60pt;white-space:break-spaces", "aaaaa     bbbbb");
            Assert.True(breaking.LineBoxes.Count >= hang.LineBoxes.Count);
        }

        [Fact]
        public async Task Balance_KeepsLineCountAndEvensOutLines()
        {
            var text = "alpha beta gamma delta epsilon zeta eta theta iota kappa lambda";
            var greedy = await LayoutDiv("width:260pt", text);
            var balanced = await LayoutDiv("width:260pt;text-wrap:balance", text);

            Assert.Equal(greedy.LineBoxes.Count, balanced.LineBoxes.Count);
            Assert.True(greedy.LineBoxes.Count >= 2);

            static double LastLineWidth(PeachBrackets.Html.Dom.CssBox b) => b.LineBoxes[^1].Words.Max(w => w.Right) - b.LineBoxes[^1].Words.Min(w => w.Left);
            Assert.True(LastLineWidth(balanced) > LastLineWidth(greedy));
            Assert.Equal(greedy.ActualBottom, balanced.ActualBottom, 3);
        }

        [Fact]
        public async Task Balance_SingleLine_IsUnchanged()
        {
            var d = await LayoutDiv("text-wrap:balance", "short");
            Assert.Single(d.LineBoxes);
        }

        [Fact]
        public async Task Pretty_And_Stable_LayOutAsAuto()
        {
            var auto = await LayoutDiv("");
            Assert.Equal(auto.LineBoxes.Count, (await LayoutDiv("text-wrap-style:pretty")).LineBoxes.Count);
            Assert.Equal(auto.LineBoxes.Count, (await LayoutDiv("text-wrap-style:stable")).LineBoxes.Count);
        }
    }
}
