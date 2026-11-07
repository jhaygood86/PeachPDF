using PeachDrawing;

namespace PeachBrackets.Tests;

/// <summary>
/// <c>background-blend-mode</c>, <c>isolation</c>, <c>color-scheme</c>, <c>light-dark()</c> and <c>print-color-adjust</c>, written the way a host
/// uses them: a document painted onto a bitmap and read back by pixel.
/// </summary>
public class BlendAndColorSchemeTests
{
    private const string Reset = "<style>@page { margin: 0 } html, body { margin: 0 }</style>";

    private static async Task<RasterCanvas> PaintAsync(string body, string head = "", ColorSchemePreference scheme = ColorSchemePreference.Light)
    {
        var (context, document) = await TestDocument.LoadAsync(
            // The transparent block gives the page its extent whatever the test draws.
            TestDocument.Wrap(body + "<div style='position:absolute;left:0;top:0;width:200pt;height:200pt'></div>", Reset + head),
            new HtmlDocumentOptions { MediaType = MediaType.Screen, ColorScheme = scheme });
        return await TestDocument.PaintAsync(context, document);
    }

    private static (int R, int G, int B) Rgb(RasterCanvas canvas, int x, int y)
    {
        var pixel = TestDocument.Pixel(canvas, x, y);
        return (pixel.R, pixel.G, pixel.B);
    }

    private const string Box = "<div id=a style='position:absolute;left:10pt;top:10pt;width:60pt;height:40pt;{0}'></div>";

    // ── background-blend-mode ──────────────────────────────────────────────

    [Fact]
    public async Task ABackgroundImageIsDrawnOverTheColour_ByDefault()
    {
        var page = await PaintAsync(string.Format(Box, "background-color: rgb(255,255,0); background-image: linear-gradient(rgb(0,255,255), rgb(0,255,255))"));

        Assert.Equal((0, 255, 255), Rgb(page, 40, 40));
    }

    [Fact]
    public async Task BackgroundBlendModeMultipliesTheImageWithTheColour()
    {
        var page = await PaintAsync(string.Format(Box, "background-color: rgb(255,255,0); background-image: linear-gradient(rgb(0,255,255), rgb(0,255,255)); background-blend-mode: multiply"));

        // yellow x cyan = green.
        Assert.Equal((0, 255, 0), Rgb(page, 40, 40));
    }

    [Fact]
    public async Task TheBackgroundOfABlendedElementDoesNotBlendWithThePageBehindIt()
    {
        // The multiply stays inside the element's own background: over a red page, the cyan image still multiplies with the yellow colour only.
        var page = await PaintAsync(
            "<div style='position:absolute;left:0;top:0;width:200pt;height:200pt;background:rgb(255,0,0)'></div>"
            + string.Format(Box, "background-color: rgb(255,255,0); background-image: linear-gradient(rgb(0,255,255), rgb(0,255,255)); background-blend-mode: multiply"));

        Assert.Equal((0, 255, 0), Rgb(page, 40, 40));
    }

    [Fact]
    public async Task BackgroundBlendModeListRepeatsAcrossTheLayers()
    {
        // Two image layers, one blend mode: it applies to both. The upper layer (cyan) multiplies with what is under it (yellow, then the lower layer).
        var page = await PaintAsync(string.Format(Box,
            "background-color: rgb(255,255,0); background-image: linear-gradient(rgb(0,255,255), rgb(0,255,255)), linear-gradient(rgb(255,0,255), rgb(255,0,255)); background-blend-mode: multiply"));

        // yellow x magenta = red; red x cyan = black.
        Assert.Equal((0, 0, 0), Rgb(page, 40, 40));
    }

    [Fact]
    public async Task ANormalLayerInTheListDrawsOverTheOthers()
    {
        var page = await PaintAsync(string.Format(Box,
            "background-color: rgb(255,255,0); background-image: linear-gradient(rgb(0,255,255), rgb(0,255,255)), linear-gradient(rgb(255,0,255), rgb(255,0,255)); background-blend-mode: normal, multiply"));

        // The upper layer (cyan) is normal: it covers everything beneath it.
        Assert.Equal((0, 255, 255), Rgb(page, 40, 40));
    }

    [Fact]
    public async Task BackgroundBlendModeIsNotResetByTheBackgroundShorthand_AndDoesNotInherit()
    {
        var page = await PaintAsync(
            "<div style='background-blend-mode: multiply'><div id=a style='position:absolute;left:10pt;top:10pt;width:60pt;height:40pt;background: linear-gradient(rgb(0,255,255), rgb(0,255,255)) rgb(255,255,0)'></div></div>");

        // The child does not inherit the mode, so its image covers its colour.
        Assert.Equal((0, 255, 255), Rgb(page, 40, 40));

        // The shorthand that follows does not reset it: the image multiplies with the colour.
        var own = await PaintAsync(string.Format(Box, "background-blend-mode: multiply; background: linear-gradient(rgb(0,255,255), rgb(0,255,255)) rgb(255,255,0)"));
        Assert.Equal((0, 255, 0), Rgb(own, 40, 40));
    }

    // ── isolation ──────────────────────────────────────────────────────────

    private static string BlendingChild(string parentStyle) =>
        "<div style='position:absolute;left:0;top:0;width:200pt;height:200pt;background:rgb(255,255,0)'></div>"
        + $"<div id=parent style='position:absolute;left:10pt;top:10pt;width:100pt;height:100pt;{parentStyle}'>"
        + "<div style='width:50pt;height:50pt;background:rgb(0,255,255);mix-blend-mode:multiply'></div></div>";

    [Fact]
    public async Task ABlendingChildBlendsWithThePageBehindItsParent()
    {
        var page = await PaintAsync(BlendingChild(""));

        // cyan x yellow = green.
        Assert.Equal((0, 255, 0), Rgb(page, 30, 30));
    }

    [Fact]
    public async Task IsolationIsolateKeepsAChildsBlendingInsideTheParent()
    {
        var page = await PaintAsync(BlendingChild("isolation: isolate"));

        // The parent is an isolated group with nothing painted in it, so the child multiplies with transparent: it stays cyan.
        Assert.Equal((0, 255, 255), Rgb(page, 30, 30));
        Assert.Equal((255, 255, 0), Rgb(page, 150, 150));
    }

    [Fact]
    public async Task IsolationIsolateBlendsTheChildWithTheParentsOwnBackground()
    {
        var page = await PaintAsync(BlendingChild("isolation: isolate; background: rgb(255,0,255)"));

        // cyan x magenta = blue.
        Assert.Equal((0, 0, 255), Rgb(page, 30, 30));
    }

    [Fact]
    public async Task IsolationAutoLeavesTheBlendWithThePage()
    {
        var page = await PaintAsync(BlendingChild("isolation: auto"));

        Assert.Equal((0, 255, 0), Rgb(page, 30, 30));
    }

    [Fact]
    public async Task IsolationDoesNotInherit()
    {
        var page = await PaintAsync(
            "<div style='position:absolute;left:0;top:0;width:200pt;height:200pt;background:rgb(255,255,0)'></div>"
            + "<div style='isolation:isolate'><div style='position:absolute;left:10pt;top:10pt;width:100pt;height:100pt'>"
            + "<div style='width:50pt;height:50pt;background:rgb(0,255,255);mix-blend-mode:multiply'></div></div></div>");

        // The isolated group is the outer div; its child is not isolated itself, but the blend still happens inside the outer group, which is empty.
        Assert.Equal((0, 255, 255), Rgb(page, 30, 30));
    }

    // ── color-scheme and light-dark() ──────────────────────────────────────

    private const string LightDark = "background: light-dark(rgb(255,0,0), rgb(0,0,255))";

    [Fact]
    public async Task LightDarkTakesTheLightArmByDefault()
    {
        var page = await PaintAsync(string.Format(Box, LightDark), scheme: ColorSchemePreference.Light);

        Assert.Equal((255, 0, 0), Rgb(page, 40, 40));
    }

    [Fact]
    public async Task LightDarkTakesTheLightArmWhenTheElementHasNoColorScheme_EvenIfTheUserPrefersDark()
    {
        var page = await PaintAsync(string.Format(Box, LightDark), scheme: ColorSchemePreference.Dark);

        Assert.Equal((255, 0, 0), Rgb(page, 40, 40));
    }

    [Fact]
    public async Task LightDarkTakesTheDarkArmUnderColorSchemeDark()
    {
        var page = await PaintAsync(string.Format(Box, LightDark + "; color-scheme: dark"));

        Assert.Equal((0, 0, 255), Rgb(page, 40, 40));
    }

    [Fact]
    public async Task ColorSchemeLightDarkFollowsTheUsersPreference()
    {
        var light = await PaintAsync(string.Format(Box, LightDark + "; color-scheme: light dark"), scheme: ColorSchemePreference.Light);
        var dark = await PaintAsync(string.Format(Box, LightDark + "; color-scheme: light dark"), scheme: ColorSchemePreference.Dark);

        Assert.Equal((255, 0, 0), Rgb(light, 40, 40));
        Assert.Equal((0, 0, 255), Rgb(dark, 40, 40));
    }

    [Fact]
    public async Task ColorSchemeInherits_AndAChildCanOverrideIt()
    {
        var page = await PaintAsync(
            "<div style='color-scheme: dark'>"
            + "<div style='position:absolute;left:10pt;top:10pt;width:60pt;height:40pt;" + LightDark + "'></div>"
            + "<div style='position:absolute;left:100pt;top:10pt;width:60pt;height:40pt;color-scheme: light;" + LightDark + "'></div>"
            + "</div>");

        Assert.Equal((0, 0, 255), Rgb(page, 40, 40));
        Assert.Equal((255, 0, 0), Rgb(page, 130, 40));
    }

    [Fact]
    public async Task LightDarkWorksInsideColorMix_AndOnAnyColorProperty()
    {
        var page = await PaintAsync(string.Format(Box,
            "color-scheme: dark; border: 6pt solid light-dark(rgb(255,0,0), rgb(0,255,0)); background: color-mix(in srgb, light-dark(rgb(0,0,0), rgb(0,0,255)) 100%, white 0%)"));

        Assert.Equal((0, 255, 0), Rgb(page, 12, 40));
        Assert.Equal((0, 0, 255), Rgb(page, 40, 40));
    }

    [Fact]
    public async Task LightDarkWorksInAGradient()
    {
        var page = await PaintAsync(string.Format(Box,
            "color-scheme: dark; background: linear-gradient(light-dark(rgb(255,0,0), rgb(0,0,255)), light-dark(rgb(255,0,0), rgb(0,0,255)))"));

        Assert.Equal((0, 0, 255), Rgb(page, 40, 40));
    }

    [Fact]
    public async Task ADarkRootDrawsTheDarkCanvas_AndTheDarkDefaultTextColour()
    {
        var page = await PaintAsync(
            "<div style='position:absolute;left:10pt;top:10pt;width:60pt;height:40pt;background: CanvasText'></div>",
            "<style>html { color-scheme: dark }</style>");

        Assert.Equal((18, 18, 18), Rgb(page, 150, 150));
        Assert.Equal((255, 255, 255), Rgb(page, 40, 40));
    }

    [Fact]
    public async Task ALightRootLeavesThePageWhite()
    {
        var page = await PaintAsync(
            "<div style='position:absolute;left:10pt;top:10pt;width:60pt;height:40pt;background: CanvasText'></div>",
            "<style>html { color-scheme: light }</style>");

        Assert.Equal((255, 255, 255), Rgb(page, 150, 150));
        Assert.Equal((0, 0, 0), Rgb(page, 40, 40));
    }

    [Fact]
    public async Task ADeclaredCanvasBackgroundPaintsOverTheDarkCanvas()
    {
        var page = await PaintAsync("<div></div>", "<style>html { color-scheme: dark; background: rgb(10, 200, 30) }</style>");

        Assert.Equal((10, 200, 30), Rgb(page, 150, 150));
    }

    [Fact]
    public async Task TheMetaElementSetsTheRootsColorScheme()
    {
        var (context, document) = await TestDocument.LoadAsync(
            "<!doctype html><html><head><meta name='color-scheme' content='dark'>" + Reset + "</head><body><div style='position:absolute;left:10pt;top:10pt;width:60pt;height:40pt;" + LightDark + "'></div><div style='position:absolute;left:0;top:0;width:200pt;height:200pt'></div></body></html>",
            new HtmlDocumentOptions { MediaType = MediaType.Screen });
        var page = await TestDocument.PaintAsync(context, document);

        Assert.Equal((0, 0, 255), Rgb(page, 40, 40));
        Assert.Equal((18, 18, 18), Rgb(page, 150, 150));
    }

    [Fact]
    public async Task OnlyKeepsTheSchemeTheListNames()
    {
        var page = await PaintAsync(string.Format(Box, LightDark + "; color-scheme: only light"), scheme: ColorSchemePreference.Dark);

        Assert.Equal((255, 0, 0), Rgb(page, 40, 40));
    }

    [Fact]
    public async Task AnInvalidColorSchemeIsIgnored()
    {
        // `normal` cannot be combined with a scheme, so the declaration is dropped and the inherited one (dark, from the parent) stays.
        var page = await PaintAsync("<div style='color-scheme: dark'>" + string.Format(Box, LightDark + "; color-scheme: normal light") + "</div>");

        Assert.Equal((0, 0, 255), Rgb(page, 40, 40));
    }

    [Fact]
    public async Task AnInvalidLightDarkDeclarationIsIgnored()
    {
        var page = await PaintAsync(string.Format(Box, "background: rgb(0,255,0); background: light-dark(rgb(255,0,0))"));

        Assert.Equal((0, 255, 0), Rgb(page, 40, 40));
    }

    // ── @supports, print-color-adjust ──────────────────────────────────────

    [Theory]
    [InlineData("isolation: isolate")]
    [InlineData("background-blend-mode: multiply")]
    [InlineData("print-color-adjust: exact")]
    [InlineData("-webkit-print-color-adjust: exact")]
    [InlineData("color-adjust: exact")]
    [InlineData("color-scheme: light dark")]
    [InlineData("color: light-dark(red, blue)")]
    public async Task TheNewPropertiesAreSupported(string declaration)
    {
        var page = await PaintAsync(string.Format(Box, ""),
            $"<style>#a {{ background: rgb(0,255,0) }} @supports ({declaration}) {{ #a {{ background: rgb(0,0,255) }} }}</style>");

        Assert.Equal((0, 0, 255), Rgb(page, 40, 40));
    }

    [Theory]
    [InlineData("isolation: bogus")]
    [InlineData("print-color-adjust: always")]
    [InlineData("background-blend-mode: nope")]
    public async Task InvalidValuesAreNotSupported(string declaration)
    {
        var page = await PaintAsync(string.Format(Box, ""),
            $"<style>#a {{ background: rgb(0,255,0) }} @supports ({declaration}) {{ #a {{ background: rgb(0,0,255) }} }}</style>");

        Assert.Equal((0, 255, 0), Rgb(page, 40, 40));
    }

    [Theory]
    [InlineData("print-color-adjust: exact")]
    [InlineData("print-color-adjust: economy")]
    [InlineData("-webkit-print-color-adjust: exact")]
    public async Task PrintColorAdjustPaintsEveryColourAsWritten(string declaration)
    {
        var page = await PaintAsync(string.Format(Box, declaration + "; background: rgb(255,0,0); border: 4pt solid rgb(0,0,255)"));

        Assert.Equal((255, 0, 0), Rgb(page, 40, 40));
        Assert.Equal((0, 0, 255), Rgb(page, 11, 40));
    }
}
