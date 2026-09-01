using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Html.Core.Dom
{
    /// <summary>
    /// Regression coverage for issue #575: <c>CssBidiParagraphResolver.Flatten</c> used to append a box's
    /// own <c>BidiIsolateOverride</c> to the shared overrides list only after recursing into that box's
    /// children, so when a nested box's override shared its parent's exact <c>Start</c> index (no
    /// character of the parent's own preceding the child), the child's override landed *before* its
    /// parent's in the list <c>BidiResolver.ResolveExplicitLevels</c> pushes in order. For two boxes with
    /// opposite directions, that pushed the wrong override on top of the stack while the shared index's
    /// character was processed, computing a numerically wrong embedding level rather than merely a
    /// misordered one.
    /// </summary>
    public class CssBidiParagraphResolverTests
    {
        [Fact]
        public async Task NestedOppositeDirectionSpans_SharingStartIndex_ResolveOuterToInnerLevels()
        {
            // <span id="outer" dir="ltr"> has no text of its own before <span id="inner" dir="rtl">, so
            // both spans' synthetic isolate overrides share the exact same Start index (right after "A") -
            // the precise shape #575 was filed for. "outer"'s override still ends two characters later
            // (after "C"), so only the *opening* order is exercised here, not the closing order.
            var html = LayoutHarness.Wrap(
                """<p id="p">A<span id="outer" dir="ltr"><span id="inner" dir="rtl">B</span>C</span></p>""");

            var (root, _) = await LayoutHarness.LayoutAsync(html);

            var textA = LayoutHarness.Descendants(root).First(b => b.Text == "A");
            var textB = LayoutHarness.Descendants(root).First(b => b.Text == "B");
            var textC = LayoutHarness.Descendants(root).First(b => b.Text == "C");

            Assert.NotNull(textA.BidiLevels);
            Assert.NotNull(textB.BidiLevels);
            Assert.NotNull(textC.BidiLevels);

            // "A" sits outside both spans, at the paragraph's own (LTR) base level.
            Assert.Equal(0, textA.BidiLevels![0]);

            // With the outer LTR isolate correctly pushed first and the inner RTL isolate pushed on top
            // of it, "B" resolves inside a level-3 (odd, RTL) scope; UAX#9 I2 then bumps its own strong-L
            // type up by one for being at an odd level, landing it at level 4. The pre-fix bug pushed the
            // inner RTL override first and the outer LTR override second (backwards), so "B" resolved
            // inside a level-2 (even, LTR) scope instead and never got the I2 bump, staying at level 2 -
            // colliding with "C"'s own level below instead of standing apart from it.
            Assert.Equal(4, textB.BidiLevels![0]);

            // "C" sits back in the outer span's own (LTR isolate) scope, one level deeper than "A".
            Assert.Equal(2, textC.BidiLevels![0]);

            // The isolate boundary between "B" and "C" must produce a level change - the collapse of both
            // onto the same level (2) is exactly the symptom a real UA would never show for two elements
            // of opposite `direction`, since it erases the isolate boundary layout/line-breaking logic
            // relies on to tell the two runs apart.
            Assert.NotEqual(textB.BidiLevels![0], textC.BidiLevels![0]);
        }
    }
}
