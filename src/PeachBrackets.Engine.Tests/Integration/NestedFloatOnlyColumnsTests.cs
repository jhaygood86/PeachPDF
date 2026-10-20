using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// A multi-column container that holds only a float, nested in an outer column, lays the float out whole: the float's lines
/// do not break at the outer column's band, where nothing would ever resume them.
/// </summary>
public class NestedFloatOnlyColumnsTests
{
    [Theory]
    [InlineData("right", 46)]
    [InlineData("left", 46)]
    [InlineData("right", 70)]
    public async Task FloatInANestedContainerWithNoOtherContent_KeepsEveryLine(string side, int height)
    {
        var words = string.Join(" ", Enumerable.Range(1, 5).Select(i => $"w1_{i}"));
        var body = $"<div style='columns:3;column-gap:8pt'><div style='columns:3;column-gap:8pt'><div style='float:{side};width:54pt;height:{height}pt'>{words} </div></div></div>";
        var html = "<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page { size: 300pt 250pt; margin: 20pt } " +
                   "body { margin:0; font: 10pt/12pt Arial }</style></head><body>" + body + "</body></html>";

        var positions = await PaintedWords.PositionsAsync(html);

        foreach (var word in Enumerable.Range(1, 5).Select(i => $"w1_{i}"))
        {
            Assert.True(positions.ContainsKey(word), $"{word} is in no fragment");
        }
    }
}