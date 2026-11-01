using PeachBrackets.Engine.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// CSS 2.1 section 17.2.1, rule 2.1: a child of a <c>table</c> or <c>inline-table</c> that is not a proper table child gets an
    /// anonymous table-row (and rule 2.3 an anonymous table-cell inside it). It was only generated for <c>table</c>, so the content of
    /// an <c>inline-table</c> holding text, spans or blocks directly was never laid out or painted.
    /// </summary>
    public class InlineTableAnonymousBoxesTests
    {
        [Theory]
        [InlineData("TXT")]
        [InlineData("<span>TXT</span>")]
        [InlineData("<div>TXT</div>")]
        public async Task InlineTable_WithNonTableChildren_HoldsTheContentInAnAnonymousRowAndCell(string content)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"A[<span id='t' style='display:inline-table'>{content}</span>]B"));

            var table = LayoutHarness.FindById(root, "t")!;

            var row = Assert.Single(table.Boxes);
            Assert.Equal("table-row", row.DerivedStyle.ActualDisplay);
            var cell = Assert.Single(row.Boxes);
            Assert.Equal("table-cell", cell.DerivedStyle.ActualDisplay);
            Assert.True(table.Size.Width > 0, "the table has the width of its content");
            Assert.True(table.Size.Height > 0, "the table has the height of its content");
        }

        [Fact]
        public async Task InlineTable_WithNonTableChildren_SitsOnTheLineBetweenTheTextAroundIt()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "A[<span id='t' style='display:inline-table'>TXT</span>]B"));

            var table = LayoutHarness.FindById(root, "t")!;
            var parent = table.ParentBox!;

            // Same line as the surrounding text: the table is an atomic inline, not a block that breaks the line.
            Assert.Equal(1, parent.LineBoxes.Count);
            Assert.True(table.Location.X > parent.Location.X);
        }

        [Fact]
        public async Task InlineTable_WithOnlyGeneratedContent_IsWrappedToo()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>.g::before{content:'GEN'}</style>A[<span id='t' style='display:inline-table'><span class='g'></span></span>]B"));

            var table = LayoutHarness.FindById(root, "t")!;

            Assert.Equal("table-row", Assert.Single(table.Boxes).DerivedStyle.ActualDisplay);
        }

        [Fact]
        public async Task InlineTable_WithAnExplicitRow_IsNotWrappedAgain()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<span id='t' style='display:inline-table'><span id='r' style='display:table-row'>TXT</span></span>"));

            var table = LayoutHarness.FindById(root, "t")!;

            Assert.Same(LayoutHarness.FindById(root, "r"), Assert.Single(table.Boxes));
        }
    }
}
