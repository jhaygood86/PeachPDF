using PeachPDF.Tests.TestSupport;
using static PeachPDF.Tests.TestSupport.LayoutHarness;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// An overflow container that may break is laid out again on every layout generation, and fragmentation state has
/// drifted between the measure and final passes before. Laying the same tree out repeatedly has to give the same
/// words in the same places every time, for the boxes that now break and for the ones that stay whole.
/// </summary>
public class OverflowContainerRepeatedLayoutStabilityTests
{
    private const int Passes = 5;

    private static string Lines(string prefix, int count) =>
        string.Concat(Enumerable.Range(1, count).Select(i => $"<div class='l'>{prefix}{i}</div>"));

    private static string Doc(string body) =>
        Wrap("<style>body { margin:0; font:10pt/20pt sans-serif } .l { height:20pt }</style>" + body);

    public static TheoryData<string, string> Documents => new()
    {
        { "auto-height overflow:auto panel", Doc($"<div>Intro</div><div style='overflow:auto'>{Lines("P", 20)}</div>") },
        { "auto-height overflow:hidden wrapper", Doc($"<div style='overflow:hidden'>{Lines("H", 20)}</div>") },
        { "overflow:hidden wrapper around a float", Doc($"<div style='overflow:hidden'><div style='float:left;width:90pt'>{Lines("F", 12)}</div>{Lines("T", 12)}</div>") },
        { "sized overflow:auto box stays whole", Doc($"<div>Intro</div><div style='overflow:auto;height:100pt'>{Lines("S", 8)}</div>{Lines("A", 6)}") },
        { "hidden box with a max-height stays whole", Doc($"<div style='overflow:hidden;max-height:120pt'>{Lines("M", 10)}</div>{Lines("A", 8)}") },
        { "overflow panel as a flex item", Doc($"<div style='display:flex'><div style='overflow:auto;flex:1'>{Lines("X", 14)}</div><div style='flex:1'>{Lines("Y", 4)}</div></div>") },
        { "overflow panel inside columns", Doc($"<div style='columns:2;column-gap:10pt'><div style='overflow:auto'>{Lines("C", 16)}</div></div>") },
        { "wrapper holding an absolute box", Doc($"<div style='overflow:auto;position:relative'>{Lines("W", 14)}<div style='position:absolute;top:10pt;left:30pt'>Abs</div></div>") },
    };

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task RepeatedLayout_GivesTheSameWordsInTheSamePlaces(string name, string html)
    {
        var results = await LayoutRepeatedlyAsync(html, Passes, (_, container) =>
        {
            var pages = container.FragmentTree!.Fragmentainers.Count;
            var painted = new List<string> { $"pages={pages}" };

            for (var page = 0; page < pages; page++)
            {
                var graphics = new TestRecordingGraphics();
                FragmentPaintHarness.PaintPage(container, graphics, page);
                painted.AddRange(graphics.DrawStringCalls.Select(w => $"{page}:{w.Text.Trim()}@{w.PaintPoint.X:0.##},{w.PaintPoint.Y:0.##}"));
            }

            return string.Join('|', painted);
        }, pageWidth: 300, pageHeight: 200, margin: 20);

        Assert.True(results[0].Split('|').Length > 4, $"{name} drew almost nothing");
        Assert.All(results.Skip(1), later => Assert.Equal(results[0], later));
    }
}
