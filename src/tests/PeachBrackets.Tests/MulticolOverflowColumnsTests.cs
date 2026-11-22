namespace PeachBrackets.Tests;

/// <summary>On a continuous surface content that does not fit the columns of a container goes in overflow columns to its right (css-multicol-1 section 3.3), not on a page that does not exist.</summary>
public class MulticolOverflowColumnsTests
{
    private static Task<(PeachDrawing.RasterRenderContext Context, HtmlDocument Document)> LoadAsync(string body)
    {
        var options = new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = 400, ViewportHeightPoints = 300 };
        options.SetMargins(0);
        return TestDocument.LoadAsync(TestDocument.Wrap(body, "<style>body { margin: 0 }</style>"), options);
    }

    [Fact]
    public async Task MoreForcedColumnBreaksThanColumns_MakeColumnsToTheRight_AndTheContainerIsAsTallAsTheTallest()
    {
        var (_, document) = await LoadAsync(
            "<div id='m' style='columns: 2; column-fill: auto; width: 100pt; background: green'>" +
            "<div id='a' style='height: 20pt'></div><div id='b' style='break-before: column; height: 40pt'></div><div id='c' style='break-before: column; height: 100pt'></div></div>");

        var m = document.GetElementBounds("m")!.Value;
        var a = document.GetElementBounds("a")!.Value;
        var b = document.GetElementBounds("b")!.Value;
        var c = document.GetElementBounds("c")!.Value;

        Assert.Equal(m.Y, c.Y, 1);
        Assert.True(b.X > a.X && c.X > b.X, $"the columns are at {a.X}, {b.X}, {c.X}");
        Assert.True(c.X >= m.Right - 0.5, $"the overflow column starts at {c.X}, the container ends at {m.Right}");
        Assert.Equal(100, m.Height, 1);
    }

    [Fact]
    public async Task ContentThatDoesNotFitADefiniteHeight_GoesInOverflowColumns()
    {
        var items = string.Concat(Enumerable.Range(0, 6).Select(i => $"<div id='i{i}' style='height: 30pt'></div>"));
        var (_, document) = await LoadAsync($"<div id='m' style='columns: 2; column-fill: auto; width: 100pt; height: 60pt'>{items}</div>");

        var m = document.GetElementBounds("m")!.Value;
        var last = document.GetElementBounds("i5")!.Value;

        // Two 30pt boxes per 60pt column: the sixth box is in the third column, to the right of the container, and still level with its top.
        Assert.Equal(60, m.Height, 1);
        Assert.True(last.X >= m.Right - 0.5, $"the third column starts at {last.X}, the container ends at {m.Right}");
        Assert.True(last.Y < m.Y + 40);
    }

    [Fact]
    public async Task ABalancedContainerWithMoreContentThanThePageBand_KeepsItsColumnCount()
    {
        // Content taller than the surface's band is not short of columns: it goes on below, as it always has, and never makes columns beside the two.
        var items = string.Concat(Enumerable.Range(0, 40).Select(i => $"<div id='i{i}' style='height: 30pt'></div>"));
        var (_, document) = await LoadAsync($"<div id='m' style='columns: 2; width: 100pt'>{items}</div>");

        var m = document.GetElementBounds("m")!.Value;

        for (var i = 0; i < 40; i++)
            Assert.True(document.GetElementBounds($"i{i}")!.Value.Right <= m.Right + 0.5, $"box {i} is outside the container");
    }
}
