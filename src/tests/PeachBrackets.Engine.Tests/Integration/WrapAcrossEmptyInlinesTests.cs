using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// A collapsible space is a soft wrap opportunity, and an inline box boundary adds or removes none (CSS Text 3 sections 4.1.1 and 5.5), so a space
    /// between two empty inlines still lets the bare text after them wrap onto the next line.
    /// </summary>
    public class WrapAcrossEmptyInlinesTests
    {
        private const string Style = "font-size:10pt;width:120pt;margin:0;font-family:monospace";
        private const string Long = "bbbbbbbbbbbbbbbbbbbb";

        private static async Task<int> LineCount(string inner)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap($"<p id='p' style='{Style}'>{inner}</p>"));
            return LayoutHarness.FindById(root, "p")!.LineBoxes.Count;
        }

        [Theory]
        [InlineData("aaaaaaaaaaaa<span></span> <span></span>" + Long)]
        [InlineData("aaaaaaaaaaaa<span></span> " + Long)]
        [InlineData("aaaaaaaaaaaa <span></span>" + Long)]
        [InlineData("aaaaaaaaaaaa<span></span> <span></span><span></span>" + Long)]
        [InlineData("aaaaaaaaaaaa<span></span> <b><span></span>" + Long + "</b>")]
        [InlineData("aaaaaaaaaaaa<b></b> <b>" + Long + "</b>")]
        public async Task ASpaceAmongEmptyInlines_IsAWrapOpportunity(string inner) => Assert.Equal(2, await LineCount(inner));

        [Fact]
        public async Task WithoutASpace_TheWordsStayWelded()
        {
            Assert.Equal(1, await LineCount("aaaaaaaaaaaa<span></span><span></span>" + Long));
        }
    }
}
