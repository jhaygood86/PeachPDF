using PeachBrackets.Html.Dom;
using System.Threading.Tasks;
using static PeachBrackets.Engine.Tests.TestSupport.LayoutHarness;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// A row in which no item grows is floored at each item's automatic minimum size like any other (CSS Flexbox 1 sections 4.5 and 9.7),
    /// measured against headless Edge.
    /// </summary>
    public class FlexRowAutomaticMinimumTests
    {
        private static double Width(CssBox box) => box.ActualRight - box.Location.X;

        [Fact]
        public async Task AnItemShrunkToNothingIsRaisedToItsMinimumAndTheOtherGivesTheRoomBack()
        {
            // Bases 360pt and 0pt fit in 400pt, but the second item cannot be narrower than its 150pt of content: 250pt and 150pt (Edge).
            var (root, _) = await LayoutAsync(Wrap(
                "<style>body{margin:0}div div{height:10pt}</style>"
                + "<div style='display:flex;width:400pt'><div id='a' style='width:90%'></div>"
                + "<div id='b' style='flex:0 1 0'><i style='display:inline-block;width:150pt'></i></div></div>"));

            Assert.Equal(250, Width(FindById(root, "a")!), 1);
            Assert.Equal(150, Width(FindById(root, "b")!), 1);
        }

        [Fact]
        public async Task ARaisedItemThatStillFitsKeepsItsMinimumWithoutShrinkingTheOthers()
        {
            // 100pt + 0pt basis in 400pt: the second is raised to 150pt, the sum (250pt) fits, so nothing else changes.
            var (root, _) = await LayoutAsync(Wrap(
                "<style>body{margin:0}div div{height:10pt}</style>"
                + "<div style='display:flex;width:400pt'><div id='a' style='width:100pt'></div>"
                + "<div id='b' style='flex:0 1 0'><i style='display:inline-block;width:150pt'></i></div></div>"));

            Assert.Equal(100, Width(FindById(root, "a")!), 1);
            Assert.Equal(150, Width(FindById(root, "b")!), 1);
        }

        [Fact]
        public async Task AnAuthoredZeroMinimumStillShrinksTheItemToNothing()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<style>body{margin:0}div div{height:10pt}</style>"
                + "<div style='display:flex;width:400pt'><div id='a' style='width:90%'></div>"
                + "<div id='b' style='flex:0 1 0;min-width:0'><i style='display:inline-block;width:150pt'></i></div></div>"));

            Assert.Equal(360, Width(FindById(root, "a")!), 1);
            Assert.Equal(0, Width(FindById(root, "b")!), 1);
        }

        [Fact]
        public async Task ARowOfItemsThatAlreadyFitTheirContentIsUnchanged()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<style>body{margin:0}div div{height:10pt}</style>"
                + "<div style='display:flex;width:400pt'><div id='a' style='width:100pt'></div><div id='b' style='width:120pt'></div></div>"));

            Assert.Equal(100, Width(FindById(root, "a")!), 1);
            Assert.Equal(120, Width(FindById(root, "b")!), 1);
        }
    }
}
