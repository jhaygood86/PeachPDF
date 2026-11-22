using PeachDrawing;

namespace PeachBrackets.Tests;

/// <summary>
/// An absolutely positioned box whose containing block is a positioned inline split over the columns of a multi-column container
/// continues into each column (css-position-3 §2.1, css-break-3): one slice per column, each against the part of the inline there.
/// </summary>
public class FragmentedInlineContainingBlockTests
{
    private static readonly (byte, byte, byte) Green = (0, 128, 0);

    // Two 100px columns of three 20px lines; the span starts after "XX " on the first line and holds six lines.
    private static string Body(string direction) =>
        $"<div style='font:20px/20px Arial;margin-top:10px;width:200px;height:66px;columns:2;column-gap:0;column-fill:auto;direction:{direction}'>" +
        "XX <span style='position:relative'>XXXX<br>XXXXX<br>XXXXXX<br>XXXXX<br>XXXXXX<br>XXXXXX" +
        "<div style='position:absolute;inset:0;background:green'></div></span></div>";

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public async Task TheBoxHasASlicePerColumn_AndNoneCrossesTheColumnEdge(string direction)
    {
        var canvas = await PageRaster.RenderAsync(Body(direction), 200, 80);

        foreach (var y in new[] { 15, 40, 65 })
        {
            var runs = PageRaster.RunsOf(canvas, y, 200, Green);

            Assert.True(runs.Count == 2, $"row {y}: " + string.Join(",", runs));
            Assert.True(runs[0].Last < 100, $"row {y}: the first column's slice ends at {runs[0].Last}");
            Assert.True(runs[1].First >= 100, $"row {y}: the second column's slice starts at {runs[1].First}");
        }
    }

    [Fact]
    public async Task AnInlineInOneColumn_KeepsItsSingleContainingBlock()
    {
        var canvas = await PageRaster.RenderAsync(
            "<div style='font:20px/20px Arial;width:200px;height:60px;columns:2;column-gap:0'>" +
            "<span style='position:relative'>XXX<br>XXX<div style='position:absolute;inset:0;background:green'></div></span></div>", 200, 60);

        Assert.Single(PageRaster.RunsOf(canvas, 10, 200, Green));
    }
}
