using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Tests;

public class ForcedColorsTests
{
    // A red box on a white page, with a box shadow and a link, drawn at known places.
    private const string Styles = """
        <style>
        @page { margin: 0 }
        #box { position: absolute; left: 20pt; top: 20pt; width: 60pt; height: 40pt; background: rgb(255, 0, 0); border: 4pt solid rgb(0, 0, 255); box-shadow: 30pt 0 0 rgb(0, 255, 0) }
        #kept { position: absolute; left: 20pt; top: 120pt; width: 60pt; height: 40pt; background: rgb(255, 0, 0); forced-color-adjust: none }
        #clear { position: absolute; left: 140pt; top: 20pt; width: 40pt; height: 40pt }
        </style>
        """;

    private const string Body = "<div id=box></div><div id=kept></div><div id=clear></div>";

    private static async Task<RasterCanvasProbe> PaintAsync(UserPreferences? preferences)
    {
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap(Body, Styles), new HtmlDocumentOptions { Preferences = preferences, Media = "screen" });
        return new RasterCanvasProbe(await TestDocument.PaintAsync(context, document));
    }

    private sealed record RasterCanvasProbe(RasterCanvas Canvas)
    {
        public (byte R, byte G, byte B, byte A) At(int x, int y) => TestDocument.Pixel(Canvas, x, y);
    }

    [Fact]
    public async Task ByDefaultTheDocumentKeepsItsOwnColours()
    {
        var page = await PaintAsync(null);

        Assert.Equal((255, 0, 0), Rgb(page.At(50, 40)));
        Assert.Equal((0, 0, 255), Rgb(page.At(21, 40)));
        Assert.Equal((0, 255, 0), Rgb(page.At(100, 40)));
    }

    [Fact]
    public async Task ForcedColorsDrawTheBackgroundAndBorderInThePalette_AndDropTheShadow()
    {
        var palette = SystemColorPalette.Dark;
        var page = await PaintAsync(new UserPreferences { ForcedColors = true, ForcedPalette = palette });

        Assert.Equal(Rgb(palette.Canvas), Rgb(page.At(50, 40)));
        Assert.Equal(Rgb(palette.CanvasText), Rgb(page.At(21, 40)));

        // The shadow to the right of the box is gone; what shows there is the page.
        Assert.Equal((255, 255, 255), Rgb(page.At(100, 40)));
    }

    [Fact]
    public async Task ATransparentBackgroundStaysTransparent()
    {
        var page = await PaintAsync(new UserPreferences { ForcedColors = true, ForcedPalette = SystemColorPalette.Dark });

        Assert.Equal((255, 255, 255), Rgb(page.At(150, 40)));
    }

    [Fact]
    public async Task ForcedColorAdjustNoneKeepsTheElementsOwnColours()
    {
        var page = await PaintAsync(new UserPreferences { ForcedColors = true, ForcedPalette = SystemColorPalette.Dark });

        Assert.Equal((255, 0, 0), Rgb(page.At(50, 140)));
    }

    [Fact]
    public async Task ForcedColorsRecolourTextAndLinks()
    {
        const string body = "<p id=text style='position:absolute;left:10px;top:10px;font-size:40px;margin:0'>HHH</p>"
            + "<a id=link href='https://example.com/' style='position:absolute;left:10px;top:80px;font-size:40px;text-decoration:none'>HHH</a>";
        var palette = SystemColorPalette.Dark;
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap(body, "<style>@page { margin: 0 } html{background:black}</style>"), new HtmlDocumentOptions
        {
            Preferences = new UserPreferences { ForcedColors = true, ForcedPalette = palette },
            Media = "screen",
        });
        var canvas = await TestDocument.PaintAsync(context, document);

        Assert.True(InkOfColour(canvas, 10, 10, 100, 50, palette.CanvasText), "the text should be drawn in CanvasText");
        Assert.True(InkOfColour(canvas, 10, 80, 100, 50, palette.LinkText), "the link should be drawn in LinkText");
    }

    [Fact]
    public async Task TheSystemColourKeywordsAreColoursAStyleSheetCanUse()
    {
        var (context, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap("<div style='position:absolute;left:10px;top:10px;width:50px;height:50px;background:CanvasText'></div>"),
            new HtmlDocumentOptions { Media = "screen" });
        var canvas = await TestDocument.PaintAsync(context, document);

        Assert.Equal((0, 0, 0), Rgb(TestDocument.Pixel(canvas, 30, 30)));
    }

    [Fact]
    public async Task FontScaleMakesRelativelySizedTextLarger_ButNotTextSetInPixels()
    {
        const string body = "<div id=em style='font-size:1em;line-height:1'>Hello</div><div id=px style='font-size:16px;line-height:1'>Hello</div>";

        async Task<(double Em, double Px)> HeightsAsync(double scale)
        {
            var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap(body), new HtmlDocumentOptions { FontScale = scale, Media = "screen" });
            return (document.GetElementBounds("em")!.Value.Height, document.GetElementBounds("px")!.Value.Height);
        }

        var normal = await HeightsAsync(1);
        var large = await HeightsAsync(2);

        Assert.Equal(normal.Em * 2, large.Em, 1);
        Assert.Equal(normal.Px, large.Px, 1);
    }

    [Fact]
    public async Task ControlsMarksOutlinesAndRulesTakeTheirOwnPaletteColours()
    {
        const string body = "<mark style='position:absolute;left:10pt;top:10pt;width:40pt;height:30pt;background:red;display:block'></mark>"
            + "<button style='position:absolute;left:10pt;top:60pt;width:40pt;height:30pt;background:red;border:3pt solid blue;padding:0'></button>"
            + "<input style='position:absolute;left:10pt;top:110pt;width:40pt;height:30pt;background:red;border:3pt solid blue;padding:0'>"
            + "<div style='position:absolute;left:80pt;top:10pt;width:40pt;height:30pt;outline:3pt solid red'></div>"
            + "<div style='position:absolute;left:10pt;top:160pt;width:60pt;height:30pt;column-count:2;column-rule:4pt solid red;column-gap:20pt'>a b c d e f g h</div>";
        var palette = SystemColorPalette.Dark;
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap(body, "<style>@page { margin: 0 }</style>"), new HtmlDocumentOptions
        {
            Preferences = new UserPreferences { ForcedColors = true, ForcedPalette = palette },
            Media = "screen",
        });
        var canvas = await TestDocument.PaintAsync(context, document);

        Assert.True(InkOfColour(canvas, 10, 10, 40, 30, palette.Mark), "marked text takes the Mark background");
        Assert.True(InkOfColour(canvas, 10, 60, 40, 30, palette.ButtonBorder), "a button takes the ButtonBorder border");
        Assert.True(InkOfColour(canvas, 10, 110, 40, 30, palette.ButtonBorder), "a field takes the ButtonBorder border");
        Assert.True(InkOfColour(canvas, 75, 5, 55, 45, palette.CanvasText), "an outline takes CanvasText");
        Assert.False(InkOfColour(canvas, 75, 5, 55, 45, PaintColor.FromArgb(255, 0, 0)), "the outline is no longer red");
    }

    private static (int R, int G, int B) Rgb((byte R, byte G, byte B, byte A) pixel) => (pixel.R, pixel.G, pixel.B);

    private static (int R, int G, int B) Rgb(PaintColor color) => (color.R, color.G, color.B);

    private static bool InkOfColour(RasterCanvas canvas, int x, int y, int width, int height, PaintColor colour)
    {
        for (var j = y; j < y + height; j++)
        {
            for (var i = x; i < x + width; i++)
            {
                if (Rgb(TestDocument.Pixel(canvas, i, j)) == Rgb(colour))
                    return true;
            }
        }

        return false;
    }
}
