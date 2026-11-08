using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>A table in a <c>direction: rtl</c> context runs its columns from the right (CSS Tables 3), so the first cell of a row is the rightmost.</summary>
public class TableRtlColumnsTests
{
    [Theory]
    [InlineData("<div dir='rtl'>", "</div>", "")]
    [InlineData("<div>", "</div>", " dir='rtl'")]
    public async Task TheFirstCellIsToTheRightOfTheSecond(string open, string close, string tableAttributes)
    {
        var html = LayoutHarness.Wrap(open + "<table id='t' width='300' cellspacing='0' cellpadding='0'" + tableAttributes +
                                      "><tr><td id='a'>t1</td><td id='b'>t2</td></tr></table>" + close);
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);
        var a = LayoutHarness.FindById(root, "a")!;
        var b = LayoutHarness.FindById(root, "b")!;
        var t = LayoutHarness.FindById(root, "t")!;

        Assert.True(a.Location.X > b.Location.X, $"t1 at {a.Location.X}, t2 at {b.Location.X}");
        Assert.Equal(t.ActualRight, a.ActualRight, 1);
        Assert.Equal(t.Location.X, b.Location.X, 1);
    }

    [Fact]
    public async Task ColumnsOfDifferentWidthsAndSpacing_AreMirroredAboutTheTable()
    {
        var html = LayoutHarness.Wrap("<div dir='rtl'><table id='t' width='400' cellspacing='4' cellpadding='0'>" +
                                      "<tr><td id='a' width='100'>1</td><td id='b'>2</td></tr><tr><td id='c' colspan='2'>3</td></tr></table></div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);
        var a = LayoutHarness.FindById(root, "a")!;
        var b = LayoutHarness.FindById(root, "b")!;
        var t = LayoutHarness.FindById(root, "t")!;

        Assert.Equal(t.ActualRight - 3, a.ActualRight, 1);
        Assert.Equal(a.Location.X - 3, b.ActualRight, 1);
        Assert.Equal(t.Location.X + 3, b.Location.X, 1);
    }

    [Fact]
    public async Task ATableInALeftToRightContext_IsUnchanged()
    {
        var html = LayoutHarness.Wrap("<table id='t' width='300' cellspacing='0' cellpadding='0'><tr><td id='a'>t1</td><td id='b'>t2</td></tr></table>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);

        Assert.True(LayoutHarness.FindById(root, "a")!.Location.X < LayoutHarness.FindById(root, "b")!.Location.X);
    }

    [Fact]
    public async Task ACollapsedBorderRtlTableWithSpansAndAHeader_LaysOut()
    {
        var html = LayoutHarness.Wrap("<div dir='rtl'><table id='t' style='border-collapse:collapse' border='1'><thead><tr><th id='h' colspan='2'>H</th></tr></thead>" +
                                      "<tr><td id='a' rowspan='2'>1</td><td id='b'>2</td></tr><tr><td id='c'>3</td></tr></table></div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);

        Assert.True(LayoutHarness.FindById(root, "a")!.Location.X > LayoutHarness.FindById(root, "b")!.Location.X);
        Assert.Equal(LayoutHarness.FindById(root, "b")!.Location.X, LayoutHarness.FindById(root, "c")!.Location.X, 1);
    }
}
