using PeachPDF.Tests.TestSupport;
using static PeachPDF.Tests.TestSupport.LayoutHarness;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// A float that runs as its own fragmentainer passes is laid out again on every layout generation, and fragmentation
/// state has drifted between the measure and final passes before. Laying the same tree out repeatedly has to give
/// the same words in the same places every time.
/// </summary>
public class FloatRepeatedLayoutStabilityTests
{
    private const int Passes = 5;

    private static string Lines(string prefix, int count) =>
        string.Concat(Enumerable.Range(1, count).Select(i => $"<div class='l'>{prefix}{i}</div>"));

    private static string Doc(string body) =>
        Wrap("<style>body { margin:0; font:10pt/20pt sans-serif } .l { height:20pt } p { margin:0 0 6pt }</style>" + body);

    public static TheoryData<string, string> Documents => new()
    {
        { "text beside a tall left float", Doc($"<div style='float:left;width:100pt'>{Lines("F", 20)}</div><div>{Lines("T", 12)}</div>") },
        { "text beside a tall right float", Doc($"<div style='float:right;width:100pt'>{Lines("F", 20)}</div><div>{Lines("T", 12)}</div>") },
        { "block that clears a tall float", Doc($"<div style='float:left;width:100pt'>{Lines("F", 20)}</div><div style='clear:left'>{Lines("A", 3)}</div>") },
        { "cleared text after a tall float", Doc($"<div style='float:left;width:100pt'>{Lines("F", 20)}</div><div style='clear:left'>a1 a2 a3</div>") },
        { "avoid box holding a tall float", Doc($"<div>{Lines("B", 2)}</div><div style='break-inside:avoid'><div style='float:left;width:100pt'>{Lines("F", 20)}</div><div>{Lines("T", 6)}</div></div>") },
        { "floats on both sides", Doc($"<div style='float:left;width:90pt'>{Lines("F", 18)}</div><div style='float:right;width:60pt;margin:8pt'>{Lines("R", 9)}</div><div>{Lines("T", 14)}</div>") },
        { "float inside a list item", Doc($"<ul><li><div style='float:right;width:70pt;margin:8pt'>{Lines("F", 16)}</div>{Lines("L", 6)}</li><li>{Lines("M", 2)}</li></ul>") },
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

        Assert.False(results[0].StartsWith("pages=1|"), $"{name} was expected to span several pages");
        Assert.True(results[0].Split('|').Length > 8, $"{name} drew almost nothing");
        Assert.All(results.Skip(1), later => Assert.Equal(results[0], later));
    }
}
