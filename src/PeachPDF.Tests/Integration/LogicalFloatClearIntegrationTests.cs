using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using System.Threading.Tasks;
using Xunit;
using static PeachPDF.Tests.TestSupport.LayoutHarness;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// CSS Logical Properties §2.2 (issue #1285): <c>float</c> and <c>clear</c> accept
    /// <c>inline-start</c>/<c>inline-end</c>, which resolve against the containing block's used
    /// <c>direction</c> - line-left for <c>ltr</c>, line-right for <c>rtl</c> - while the computed value stays
    /// the keyword.
    /// </summary>
    public class LogicalFloatClearIntegrationTests
    {
        // The harness puts the page's content edge at 20pt and the container is 300pt wide.
        private const double Left = 20;
        private const double Right = 320;

        [Theory]
        [InlineData("ltr", "inline-start", true)]
        [InlineData("ltr", "inline-end", false)]
        [InlineData("rtl", "inline-start", false)]
        [InlineData("rtl", "inline-end", true)]
        public async Task Float_InlineKeyword_ResolvesAgainstTheContainingBlocksDirection(string direction, string keyword, bool onLeft)
        {
            var (root, _) = await LayoutAsync(Wrap($@"
                <div style='width:300pt; direction:{direction}'>
                    <div id='f' style='float:{keyword}; width:50pt; height:30pt'></div>
                </div>"));

            var f = FindById(root, "f")!;

            Assert.Equal(onLeft ? Floating.Left : Floating.Right, f.EffectiveFloatSide);
            Assert.True(f.IsFloated);
            Assert.Equal(onLeft ? Left : Right - 50, f.Location.X, 2);
            Assert.Equal(onLeft ? Left + 50 : Right, f.ActualRight, 2);
        }

        [Fact]
        public async Task Float_InlineStart_KeepsTheKeywordAsItsComputedValue()
        {
            // Resolved when layout asks: the containing block's direction is read then, and the property
            // keeps what was declared (which is what getComputedStyle-style reads see).
            var (root, _) = await LayoutAsync(Wrap(@"
                <div style='width:300pt; direction:rtl'>
                    <div id='f' style='float:inline-start; width:50pt; height:30pt'></div>
                </div>"));

            var f = FindById(root, "f")!;

            Assert.Equal(Floating.InlineStart, f.Float.Value);
            Assert.Equal("inline-start", f.Float.ToString());
        }

        [Fact]
        public async Task Float_InlineStart_UsesTheContainingBlocksDirectionNotItsOwn()
        {
            // The mapping "uses the writing mode of the element's containing block": an ltr float inside an
            // rtl parent still goes to the parent's inline-start side, which is the right.
            var (root, _) = await LayoutAsync(Wrap(@"
                <div style='width:300pt; direction:rtl'>
                    <div id='f' style='float:inline-start; direction:ltr; width:50pt; height:30pt'></div>
                </div>"));

            var f = FindById(root, "f")!;

            Assert.Equal(Floating.Right, f.EffectiveFloatSide);
            Assert.Equal(Right - 50, f.Location.X, 2);
        }

        [Theory]
        [InlineData("ltr", "left", "inline-start", true)]
        [InlineData("ltr", "left", "inline-end", false)]
        [InlineData("rtl", "right", "inline-start", true)]
        [InlineData("rtl", "right", "inline-end", false)]
        public async Task Clear_InlineKeyword_ClearsFloatsOnThatSideOnly(string direction, string floatSide, string clear, bool clears)
        {
            var (root, _) = await LayoutAsync(Wrap($@"
                <div style='width:300pt; direction:{direction}'>
                    <div id='f' style='float:{floatSide}; width:50pt; height:40pt'></div>
                    <div id='c' style='clear:{clear}; height:10pt'></div>
                </div>"));

            var f = FindById(root, "f")!;
            var c = FindById(root, "c")!;

            if (clears)
            {
                Assert.Equal(f.ActualBottom, c.Location.Y, 2);
            }
            else
            {
                Assert.True(c.Location.Y < f.ActualBottom - 1, $"c at {c.Location.Y} must not clear a float ending at {f.ActualBottom}");
            }
        }

        [Fact]
        public async Task Clear_InlineStart_ResolvesToTheSideAFloatInlineEndFloatsTo()
        {
            // clear:inline-start and float:inline-end are opposite sides in one containing block, whatever its
            // direction, so comparing the resolved physical sides is what makes the pair line up.
            var (root, _) = await LayoutAsync(Wrap(@"
                <div style='width:300pt; direction:rtl'>
                    <div id='f' style='float:inline-end; width:50pt; height:40pt'></div>
                    <div id='keep' style='clear:inline-start; height:10pt'></div>
                    <div id='clear' style='clear:inline-end; height:10pt'></div>
                </div>"));

            var f = FindById(root, "f")!;
            var keep = FindById(root, "keep")!;
            var clear = FindById(root, "clear")!;

            Assert.True(keep.Location.Y < f.ActualBottom - 1, "inline-start is the other side, so the float is not cleared");
            Assert.Equal(f.ActualBottom, clear.Location.Y, 2);
        }

        [Fact]
        public async Task FloatWithClear_BothInlineKeywords_StacksBelowTheFloatOnTheSameSide()
        {
            var (root, _) = await LayoutAsync(Wrap(@"
                <div style='width:300pt'>
                    <div id='a' style='float:inline-start; width:50pt; height:40pt'></div>
                    <div id='b' style='float:inline-start; clear:inline-start; width:50pt; height:20pt'></div>
                </div>"));

            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;

            Assert.Equal(a.ActualBottom, b.Location.Y, 2);
            Assert.Equal(Left, b.Location.X, 2);
        }

        [Theory]
        [InlineData("ltr", "inline-start", "left", true)]
        [InlineData("rtl", "inline-start", "right", false)]
        [InlineData("rtl", "inline-end", "left", false)]
        public async Task Float_InlineKeyword_WrapsTextExactlyAsThePhysicalFloatDoes(
            string direction, string logicalKeyword, string physicalKeyword, bool textMovesOffTheFloat)
        {
            async Task<double> FirstWordLeft(string keyword)
            {
                var (root, _) = await LayoutAsync(Wrap($@"
                    <div style='width:300pt; direction:{direction}'>
                        <div style='float:{keyword}; width:100pt; height:50pt'></div>
                        <p id='text' style='margin:0'>Hello world</p>
                    </div>"));

                var text = FindById(root, "text")!;
                var word = text.Words.Count > 0 ? text.Words[0] : text.Boxes[0].Words[0];
                return word.Rectangle.Left;
            }

            var logical = await FirstWordLeft(logicalKeyword);
            var physical = await FirstWordLeft(physicalKeyword);

            Assert.Equal(physical, logical, 3);

            if (textMovesOffTheFloat)
            {
                Assert.NotEqual(await FirstWordLeft("none"), logical);
            }
        }
    }
}
