using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Html.Core.Parse
{
    /// <summary>
    /// Whitespace-only text boxes between tags are kept only where CSS 2.1 §16.6.1 can make them visible. The pass that decides
    /// removes them in one compaction instead of shifting the child list for each, so these pin which survive.
    /// </summary>
    public class CorrectTextBoxesTests
    {
        private static async Task<List<string>> ChildKinds(string innerHtml)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap($"<div id='d'>{innerHtml}</div>"));
            var div = LayoutHarness.FindById(root, "d")!;

            return div.Boxes.Select(b => b.HtmlTag?.Name ?? (string.IsNullOrWhiteSpace(b.Text) ? "ws" : "text")).ToList();
        }

        [Fact]
        public async Task WhitespaceBetweenTwoInlineBoxes_IsKept()
        {
            Assert.Equal(["span", "ws", "span"], await ChildKinds("<span>a</span> <span>b</span>"));
        }

        [Fact]
        public async Task WhitespaceBesideABlockBox_IsDropped()
        {
            // The inline run beside a block is wrapped in an anonymous box (no tag, so "ws" here); the gap itself is gone.
            Assert.Equal(["ws", "p"], await ChildKinds("<span>a</span> <p>b</p>"));
            Assert.Equal(["p", "ws"], await ChildKinds("<p>b</p> <span>a</span>"));
            Assert.Equal(["p", "p"], await ChildKinds("<p>a</p>\n  <p>b</p>\n"));
        }

        [Fact]
        public async Task SeveralWhitespaceRunsInOneParent_AreDecidedIndependently()
        {
            // inline gap inline | block gap block | inline gap block: only the first gap survives, inside the anonymous
            // wrapper of the leading inline run; the two inline runs beside blocks are wrapped the same way.
            Assert.Equal(
                ["ws", "p", "p", "ws", "p"],
                await ChildKinds("<span>a</span> <span>b</span> <p>c</p> <p>d</p> <span>e</span> <p>f</p> "));
        }

        [Fact]
        public async Task ALongRunOfWhitespaceSeparatedInlineBoxes_KeepsEveryGap()
        {
            var html = string.Concat(Enumerable.Range(0, 200).Select(i => $"<b>{i}</b> "));

            var kinds = await ChildKinds(html);

            Assert.Equal(200, kinds.Count(k => k == "b"));
            Assert.Equal(199, kinds.Count(k => k == "ws"));
        }

        [Fact]
        public async Task WhitespaceInAPreformattedParent_IsKept()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap("<pre id='p'><p>a</p> <p>b</p></pre>"));
            var pre = LayoutHarness.FindById(root, "p")!;

            // <p>, the whitespace between the paragraphs (in an anonymous box of its own), <p>.
            Assert.Equal(3, pre.Boxes.Count);
        }
    }
}
