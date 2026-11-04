using PeachDrawing;

namespace PeachBrackets.Tests;

public class MeterPaintTests
{
    private const string Styles = "<style>@page { margin: 0 } progress, meter { position: absolute; left: 10pt; top: 10pt; width: 100pt; height: 20pt; border: 0; padding: 0 }</style>";

    private static async Task<RasterCanvas> PaintAsync(string body)
    {
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap(body, Styles), new HtmlDocumentOptions { MediaType = MediaType.Screen, ViewportHeightPoints = 200 });
        return await TestDocument.PaintAsync(context, document);
    }

    private static int Count(RasterCanvas canvas, Func<(byte R, byte G, byte B, byte A), bool> match)
    {
        var count = 0;
        for (var y = 8; y < 32; y++)
        {
            for (var x = 8; x < 112; x++)
            {
                if (match(TestDocument.Pixel(canvas, x, y)))
                    count++;
            }
        }

        return count;
    }

    private static bool IsRed((byte R, byte G, byte B, byte A) p) => p.R > 200 && p.G < 60 && p.B < 60;

    private static bool IsTrack((byte R, byte G, byte B, byte A) p) => p is { R: > 215 and < 235, G: > 215 and < 235, B: > 215 and < 235 };

    [Fact]
    public async Task AProgressBarIsFilledToItsValueInTheAccentColor()
    {
        var half = await PaintAsync("<progress value=50 max=100 style='accent-color:red'></progress>");
        var full = await PaintAsync("<progress value=100 max=100 style='accent-color:red'></progress>");

        var halfRed = Count(half, IsRed);
        var fullRed = Count(full, IsRed);
        Assert.InRange(halfRed / (double)fullRed, 0.4, 0.6);
        Assert.True(Count(half, IsTrack) > 400, "the rest of the bar is the track");
        Assert.True(Count(full, IsTrack) < Count(half, IsTrack) / 4, "a full bar shows almost no track");
    }

    [Fact]
    public async Task AProgressBarWithNoValueIsOnlyATrack()
    {
        var canvas = await PaintAsync("<progress style='accent-color:red'></progress>");

        Assert.Equal(0, Count(canvas, IsRed));
        Assert.True(Count(canvas, IsTrack) > 1000);
    }

    [Fact]
    public async Task AMeterTakesItsRangeAndTheColorOfItsRegion()
    {
        var good = await PaintAsync("<meter min=0 max=100 low=30 high=70 optimum=90 value=80></meter>");
        var ok = await PaintAsync("<meter min=0 max=100 low=30 high=70 optimum=90 value=50></meter>");
        var bad = await PaintAsync("<meter min=0 max=100 low=30 high=70 optimum=90 value=10></meter>");

        Assert.True(Count(good, p => p.G > 140 && p.R < 80) > 800, "green in the optimum region");
        Assert.True(Count(ok, p => p.R > 220 && p.G > 170 && p.B < 80) > 500, "yellow next to it");
        Assert.True(Count(bad, p => p.R > 190 && p.G < 80 && p.B < 80) > 150, "red beyond it");
    }

    [Fact]
    public async Task TheFallbackTextIsNotDrawn()
    {
        var canvas = await PaintAsync("<progress value=0 max=100 style='color:black'>BIG FALLBACK TEXT</progress>");

        Assert.Equal(0, Count(canvas, p => p.R < 100 && p.G < 100 && p.B < 100));
    }

    [Fact]
    public async Task ABarTakesTheSizeOfTenEmsByOneEmUnlessItIsStyled()
    {
        var (_, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap("<p style='font-size:10pt'><progress id=p value=1></progress></p>", "<style>@page { margin: 0 }</style>"), new HtmlDocumentOptions { MediaType = MediaType.Screen, ViewportHeightPoints = 200 });

        var bounds = document.GetElementBounds("p")!.Value;
        Assert.Equal(100, bounds.Width, 1);
        Assert.Equal(10, bounds.Height, 1);
    }
}
