using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// A float nearly as tall as the page leaves no room below it for the line that cannot sit beside it. Such a line stays beside the
/// float rather than going to the next page with it, where the same thing would happen again.
/// </summary>
public class FloatNearlyAsTallAsThePageTests
{
    private static string Page(string body) =>
        "<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page { size: 300pt 250pt; margin: 20pt } " +
        "body { margin:0; font: 10pt/12pt Arial }</style></head><body>" + body + "</body></html>";

    [Theory]
    [InlineData(127, 208)]
    [InlineData(60, 208)]
    public async Task NestedContainerWithAFloatOfNearlyThePageHeight_Finishes(int floatWidth, int floatHeight)
    {
        var words = string.Join(" ", Enumerable.Range(3, 10).Select(i => $"w1_{i}"));
        var body = "<div style='columns:3;column-gap:8pt'><div style='columns:3;column-gap:8pt'>" +
                   $"<div style='float:left;width:{floatWidth}pt;height:{floatHeight}pt'>w1_1 w1_2</div>{words} </div></div>";

        // Before the fix the container was deferred from page to page up to the driver's 100,000-pass cap, so a render
        // that does not return is the failure.
        var positions = await Task.Run(() => PaintedWords.PositionsAsync(Page(body)))
            .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        for (var i = 1; i <= 12; i++)
        {
            Assert.True(positions.ContainsKey($"w1_{i}"), $"w1_{i} is in no fragment");
        }
    }
}