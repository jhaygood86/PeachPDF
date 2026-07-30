using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <c>text-align</c>'s initial value is <c>start</c> (CSS Text 3 §7.1), which resolves against the
    /// box's own <c>direction</c> at layout time (<c>CssLayoutEngine.ApplyHorizontalAlignment</c>) - not
    /// always to <c>left</c>, the legacy/incorrect initial value this replaces.
    /// </summary>
    public class TextAlignStartEndIntegrationTests
    {
        private static CssRect FirstWord(CssBox box) =>
            LayoutHarness.Descendants(box).SelectMany(b => b.Words).First(w => !w.IsSpaces);

        [Fact]
        public async Task Start_InLtrBlock_PacksTextAgainstTheLeftEdge()
        {
            var html = LayoutHarness.Wrap(
                """<p id="p" style="text-align: start; width: 200pt">hi</p>""");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var p = LayoutHarness.FindById(root, "p")!;
            var word = FirstWord(p);

            Assert.Equal(p.ClientLeft, word.Left, 2);
        }

        [Fact]
        public async Task Start_InRtlBlock_PacksTextAgainstTheRightEdge()
        {
            var html = LayoutHarness.Wrap(
                """<p id="p" dir="rtl" style="text-align: start; width: 200pt">hi</p>""");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var p = LayoutHarness.FindById(root, "p")!;
            var word = FirstWord(p);

            Assert.Equal(p.ClientRight, word.Right, 2);
        }

        [Fact]
        public async Task End_InLtrBlock_PacksTextAgainstTheRightEdge()
        {
            var html = LayoutHarness.Wrap(
                """<p id="p" style="text-align: end; width: 200pt">hi</p>""");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var p = LayoutHarness.FindById(root, "p")!;
            var word = FirstWord(p);

            Assert.Equal(p.ClientRight, word.Right, 2);
        }

        [Fact]
        public async Task End_InRtlBlock_PacksTextAgainstTheLeftEdge()
        {
            var html = LayoutHarness.Wrap(
                """<p id="p" dir="rtl" style="text-align: end; width: 200pt">hi</p>""");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var p = LayoutHarness.FindById(root, "p")!;
            var word = FirstWord(p);

            Assert.Equal(p.ClientLeft, word.Left, 2);
        }

        [Fact]
        public async Task DefaultsToStart_UnsetTextAlign_BehavesLikeLeftInLtr()
        {
            var html = LayoutHarness.Wrap("""<p id="p" style="width: 200pt">hi</p>""");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var p = LayoutHarness.FindById(root, "p")!;
            var word = FirstWord(p);

            Assert.Equal(p.ClientLeft, word.Left, 2);
        }
    }
}
