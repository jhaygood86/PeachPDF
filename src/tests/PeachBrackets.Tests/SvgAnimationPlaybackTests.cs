using PeachBrackets.Updates;
using PeachDrawing;

namespace PeachBrackets.Tests;

/// <summary>SMIL animation elements, and CSS animations of the elements of an inline svg, played by the clock (SVG Animations; SMIL Animation 3).</summary>
public class SvgAnimationPlaybackTests
{
    private static HtmlDocumentOptions Playing() => new() { MediaType = MediaType.Screen, PageWidthPoints = 400, ViewportHeightPoints = 300, AnimationsEnabled = true };

    private static async Task<(RasterRenderContext Context, HtmlDocument Document)> LoadAsync(string svg, string css = "", HtmlDocumentOptions? options = null) =>
        await TestDocument.LoadAsync(TestDocument.Wrap($"<svg id=s width=200 height=100 viewBox=\"0 0 200 100\">{svg}</svg>", $"<style>body {{ margin: 0 }} {css}</style>"), options ?? Playing());

    private static async Task<(int R, int G, int B)> PixelAsync(RasterRenderContext context, HtmlDocument document, double x, double y)
    {
        var box = document.GetElementBounds("s")!.Value;
        using var canvas = await TestDocument.PaintAsync(context, document);
        var pixel = TestDocument.Pixel(canvas, (int)(box.X + x), (int)(box.Y + y));
        return (pixel.R, pixel.G, pixel.B);
    }

    private static AnimationFrame At(HtmlDocument document, double seconds) => document.AdvanceAnimations(TimeSpan.FromSeconds(seconds));

    private static bool IsRed((int R, int G, int B) p) => p.R > 200 && p.G < 60 && p.B < 60;

    private static bool IsWhite((int R, int G, int B) p) => p.R > 240 && p.G > 240 && p.B > 240;

    private static bool IsGreen((int R, int G, int B) p) => p.R < 60 && p.G > 200 && p.B < 60;

    [Fact]
    public async Task AnimateColor_MixesFromAndTo_AndFreezesAtTheEnd()
    {
        var (context, document) = await LoadAsync(
            "<rect width=200 height=100 fill=\"rgb(255,0,0)\"><animate attributeName=fill from=\"rgb(255,0,0)\" to=\"rgb(0,255,0)\" dur=2s fill=freeze /></rect>");

        At(document, 0);
        Assert.True(IsRed(await PixelAsync(context, document, 10, 10)));

        At(document, 1);
        var middle = await PixelAsync(context, document, 10, 10);
        Assert.InRange(middle.R, 90, 165);
        Assert.InRange(middle.G, 90, 165);

        At(document, 3);
        Assert.True(IsGreen(await PixelAsync(context, document, 10, 10)));
    }

    [Fact]
    public async Task WithoutFreeze_TheValueGoesBackWhenTheAnimationEnds()
    {
        var (context, document) = await LoadAsync(
            "<rect width=200 height=100 fill=\"rgb(255,0,0)\"><animate attributeName=fill from=\"rgb(0,255,0)\" to=\"rgb(0,255,0)\" dur=1s /></rect>");

        At(document, 0);
        At(document, 0.5);
        Assert.True(IsGreen(await PixelAsync(context, document, 10, 10)));

        At(document, 2);
        Assert.True(IsRed(await PixelAsync(context, document, 10, 10)));
    }

    [Fact]
    public async Task AnimatingAGeometryAttribute_MovesTheEdge()
    {
        var (context, document) = await LoadAsync(
            "<rect width=20 height=100 fill=\"rgb(255,0,0)\"><animate attributeName=width from=20 to=180 dur=2s fill=freeze /></rect>");

        At(document, 0);
        Assert.True(IsWhite(await PixelAsync(context, document, 100, 50)));

        At(document, 1);
        Assert.True(IsRed(await PixelAsync(context, document, 60, 50)));
        Assert.True(IsWhite(await PixelAsync(context, document, 150, 50)));

        At(document, 2.5);
        Assert.True(IsRed(await PixelAsync(context, document, 120, 50)));
    }

    [Fact]
    public async Task Set_AppliesFromItsBegin()
    {
        var (context, document) = await LoadAsync(
            "<rect width=200 height=100 fill=\"rgb(255,0,0)\"><set attributeName=fill to=\"rgb(0,255,0)\" begin=1s /></rect>");

        At(document, 0);
        At(document, 0.5);
        Assert.True(IsRed(await PixelAsync(context, document, 10, 10)));

        At(document, 1.5);
        Assert.True(IsGreen(await PixelAsync(context, document, 10, 10)));
    }

    [Fact]
    public async Task ABeginThatNamesAnotherAnimationsEnd_WaitsForIt()
    {
        var (context, document) = await LoadAsync(
            "<rect width=200 height=100 fill=\"rgb(255,0,0)\">" +
            "<set id=first attributeName=fill to=\"rgb(0,0,255)\" begin=0s dur=1s />" +
            "<set attributeName=fill to=\"rgb(0,255,0)\" begin=\"first.end+0.5s\" fill=freeze />" +
            "</rect>");

        At(document, 0);
        At(document, 0.5);
        Assert.Equal(255, (await PixelAsync(context, document, 10, 10)).B);

        At(document, 1.25);
        Assert.True(IsRed(await PixelAsync(context, document, 10, 10)));

        At(document, 1.75);
        Assert.True(IsGreen(await PixelAsync(context, document, 10, 10)));
    }

    [Fact]
    public async Task AnimateTransform_TranslatesTheElement()
    {
        var (context, document) = await LoadAsync(
            "<rect width=40 height=40 fill=\"rgb(255,0,0)\"><animateTransform attributeName=transform type=translate from=\"0 0\" to=\"100 0\" dur=2s fill=freeze /></rect>");

        At(document, 0);
        Assert.True(IsRed(await PixelAsync(context, document, 10, 10)));

        At(document, 2.5);
        Assert.True(IsWhite(await PixelAsync(context, document, 10, 10)));
        Assert.True(IsRed(await PixelAsync(context, document, 90, 10)));
    }

    [Fact]
    public async Task AnimateMotion_FollowsThePath()
    {
        var (context, document) = await LoadAsync(
            "<rect width=40 height=40 fill=\"rgb(255,0,0)\"><animateMotion path=\"M0,0 L100,0\" dur=2s fill=freeze /></rect>");

        At(document, 0);
        Assert.True(IsRed(await PixelAsync(context, document, 10, 10)));

        At(document, 1);
        Assert.True(IsRed(await PixelAsync(context, document, 50, 10)));
        Assert.True(IsWhite(await PixelAsync(context, document, 10, 10)));

        At(document, 3);
        Assert.True(IsRed(await PixelAsync(context, document, 90, 10)));
    }

    [Fact]
    public async Task ARepeatingAnimation_KeepsTheClockRunning_AndAFinishedOneStopsIt()
    {
        var (_, repeating) = await LoadAsync(
            "<rect width=20 height=20><animate attributeName=width from=20 to=100 dur=1s repeatCount=indefinite /></rect>");
        At(repeating, 0);
        Assert.True(At(repeating, 5).IsRunning);
        Assert.True(repeating.HasRunningAnimations);

        var (_, finite) = await LoadAsync(
            "<rect width=20 height=20><animate attributeName=width from=20 to=100 dur=1s fill=freeze /></rect>");
        At(finite, 0);
        Assert.True(At(finite, 0.5).IsRunning);
        Assert.NotEmpty(At(finite, 1.5).DirtyRegions);

        var after = At(finite, 2);
        Assert.False(after.IsRunning);
        Assert.Empty(after.DirtyRegions);
        Assert.False(finite.HasRunningAnimations);
    }

    [Fact]
    public async Task AFrame_RepaintsOnlyTheSvg()
    {
        var (_, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap("<p style=\"height:60pt\">text</p><svg id=s width=200 height=100><rect width=20 height=100><animate attributeName=width from=20 to=100 dur=2s repeatCount=indefinite /></rect></svg>",
                "<style>body { margin: 0 }</style>"), Playing());

        At(document, 0);
        var frame = At(document, 0.5);
        var svg = document.GetElementBounds("s")!.Value;

        Assert.NotEmpty(frame.DirtyRegions);
        Assert.All(frame.DirtyRegions, region => Assert.True(region.Bounds.Y >= svg.Y - 6));
    }

    [Fact]
    public async Task WithoutAnimationsEnabled_TheGraphicIsStatic()
    {
        var options = Playing();
        options.AnimationsEnabled = false;
        var (context, document) = await LoadAsync(
            "<rect width=200 height=100 fill=\"rgb(255,0,0)\"><set attributeName=fill to=\"rgb(0,255,0)\" begin=0s /></rect>", options: options);

        Assert.True(IsRed(await PixelAsync(context, document, 10, 10)));
    }

    [Fact]
    public async Task AStillFrame_SamplesTheAnimationAtItsFractionOfTheSpan()
    {
        const string svg = "<rect width=200 height=100 fill=\"rgb(255,0,0)\"><animate attributeName=fill from=\"rgb(255,0,0)\" to=\"rgb(0,255,0)\" dur=4s fill=freeze /></rect>";

        var (startContext, start) = await LoadAsync(svg, options: new HtmlDocumentOptions { AnimationProgress = 0 });
        Assert.True(IsRed(await PixelAsync(startContext, start, 10, 10)));

        var (endContext, end) = await LoadAsync(svg, options: new HtmlDocumentOptions { AnimationProgress = 1 });
        Assert.True(IsGreen(await PixelAsync(endContext, end, 10, 10)));
    }

    [Fact]
    public async Task ACssAnimationOnAnSvgElement_PlaysOnTheClock()
    {
        const string css = "@keyframes tint { from { fill: rgb(255,0,0) } to { fill: rgb(0,255,0) } } #r { animation: tint 2s linear forwards }";
        var (context, document) = await LoadAsync("<rect id=r width=200 height=100 fill=\"rgb(255,0,0)\" />", css);

        At(document, 0);
        Assert.True(IsRed(await PixelAsync(context, document, 10, 10)));

        var frame = At(document, 1);
        Assert.True(frame.IsRunning);
        var middle = await PixelAsync(context, document, 10, 10);
        Assert.InRange(middle.R, 90, 165);

        At(document, 3);
        Assert.True(IsGreen(await PixelAsync(context, document, 10, 10)));
        Assert.False(At(document, 4).IsRunning);
    }

    [Fact]
    public async Task AnAnimationWithAZeroDuration_IsIgnored()
    {
        var (context, document) = await LoadAsync(
            "<rect width=200 height=100 fill=\"rgb(255,0,0)\"><animate attributeName=fill to=\"rgb(0,255,0)\" dur=0s fill=freeze /></rect>");

        At(document, 1);
        Assert.True(IsRed(await PixelAsync(context, document, 10, 10)));
        Assert.False(document.HasRunningAnimations);
    }

    // A red rect that covers the svg at opacity 0.2: the green channel of a pixel reads back the opacity, 1 - G / 255.
    private static async Task<double> OpacityAsync(string animation, double seconds, string extra = "")
    {
        var (context, document) = await LoadAsync($"<rect id=r width=200 height=100 fill=\"rgb(255,0,0)\" opacity=\"0.2\">{animation}</rect>{extra}");
        At(document, 0);
        At(document, seconds);
        return 1 - ((await PixelAsync(context, document, 10, 10)).G / 255.0);
    }

    [Theory]
    [InlineData("<animate attributeName=opacity from=0 to=1 dur=2s fill=freeze />", 1, 0.5)]
    [InlineData("<animate attributeName=opacity values=\"0;1;0\" dur=2s />", 0.5, 0.5)]
    [InlineData("<animate attributeName=opacity values=\"0;1;0\" dur=2s />", 1.5, 0.5)]
    [InlineData("<animate attributeName=opacity values=\"0;0.5;1\" calcMode=discrete dur=3s />", 1.5, 0.5)]
    [InlineData("<animate attributeName=opacity values=\"0;1;1\" keyTimes=\"0;0.25;1\" dur=4s />", 0.5, 0.5)]
    [InlineData("<animate attributeName=opacity values=\"0;0.1;1\" calcMode=paced dur=10s />", 1, 0.1)]
    [InlineData("<animate attributeName=opacity from=0 to=0.5 dur=2s additive=sum fill=freeze />", 1, 0.45)]
    [InlineData("<animate attributeName=opacity by=0.5 dur=2s fill=freeze />", 1, 0.45)]
    [InlineData("<animate attributeName=opacity to=1 dur=2s fill=freeze />", 1, 0.6)]
    [InlineData("<animate attributeName=opacity from=0 to=0.3 dur=1s repeatCount=2 accumulate=sum fill=freeze />", 1.5, 0.45)]
    [InlineData("<animate attributeName=opacity from=0 to=1 dur=1s repeatDur=1.5s fill=freeze />", 3, 0.5)]
    [InlineData("<animate attributeName=opacity from=0 to=1 begin=\"0s;3s\" dur=1s restart=never />", 3.5, 0.2)]
    [InlineData("<animate attributeName=opacity from=0 to=1 dur=4s end=1s />", 2, 0.2)]
    [InlineData("<animate attributeName=opacity from=0 to=1 dur=1s min=3s fill=freeze />", 2, 1)]
    [InlineData("<animate attributeName=opacity from=0 to=1 dur=2s max=1s fill=freeze />", 3, 0.5)]
    [InlineData("<set attributeName=opacity to=0.9 begin=1s />", 2, 0.9)]
    [InlineData("<set attributeName=opacity to=0.5 begin=0s /><set attributeName=opacity to=0.9 begin=1s />", 2, 0.9)]
    [InlineData("<animate attributeName=opacity from=0 to=1 begin=\"a.end\" dur=1s id=b /><animate attributeName=opacity from=0 to=1 begin=\"b.end\" dur=1s id=a />", 1, 0.2)]
    [InlineData("<animate attributeName=opacity from=0 to=1 begin=\"-1s\" dur=2s fill=freeze />", 0.5, 0.75)]
    [InlineData("<animate attributeName=opacity from=0 to=1 begin=\"1s\" dur=2s />", 0.5, 0.2)]
    [InlineData("<animate attributeName=opacity from=0 to=1 dur=2s calcMode=spline keyTimes=\"0;1\" keySplines=\"0 0 1 1\" fill=freeze />", 1, 0.5)]
    [InlineData("<animate attributeName=opacity from=\"0\" to=\"1\" dur=\"00:00:02\" fill=freeze />", 1, 0.5)]
    public async Task AnAnimationOfOpacity_GivesTheValueItsTimingAndValuesSay(string animation, double seconds, double expected) =>
        Assert.InRange(await OpacityAsync(animation, seconds), expected - 0.03, expected + 0.03);

    [Fact]
    public async Task AnAnimationWithAnHrefAndNoParent_AnimatesTheElementItNames()
    {
        var opacity = await OpacityAsync(string.Empty, 2, "<set href=\"#r\" attributeName=opacity to=0.9 begin=1s />");
        Assert.InRange(opacity, 0.87, 0.93);
    }

    [Theory]
    [InlineData("<animateMotion path=\"M0,0 L100,0\" dur=2s fill=freeze />", 1, 50, 10)]
    [InlineData("<animateMotion values=\"0,0;100,0\" dur=2s fill=freeze />", 1, 50, 10)]
    [InlineData("<animateMotion from=\"0,0\" to=\"100,0\" dur=2s fill=freeze />", 1, 50, 10)]
    [InlineData("<animateMotion by=\"100,0\" dur=2s fill=freeze />", 1, 50, 10)]
    [InlineData("<animateMotion path=\"M0,0 L100,0\" keyPoints=\"0;1\" keyTimes=\"0;1\" calcMode=linear dur=2s fill=freeze />", 1, 50, 10)]
    [InlineData("<animateMotion path=\"M0,0 L100,0\" rotate=auto dur=2s fill=freeze />", 1, 50, 10)]
    [InlineData("<animateMotion path=\"M0,0 L100,0\" rotate=0 dur=2s fill=freeze />", 1, 50, 10)]
    public async Task AnAnimateMotion_PlacesTheElementOnItsPath(string animation, double seconds, double userX, double y)
    {
        var (context, document) = await LoadAsync($"<rect width=40 height=40 fill=\"rgb(255,0,0)\">{animation}</rect>");
        At(document, 0);
        At(document, seconds);
        Assert.True(IsWhite(await PixelAsync(context, document, 3, y)));
        Assert.True(IsRed(await PixelAsync(context, document, (userX + 20) * 0.75, 15)) || IsRed(await PixelAsync(context, document, (userX + 20) * 0.75, 25)));
    }

    [Fact]
    public async Task AnMPathAndASkewAndScaleTransform_ArePlayed()
    {
        var (context, document) = await LoadAsync(
            "<path id=track d=\"M0,0 L100,0\"/><rect width=40 height=40 fill=\"rgb(255,0,0)\"><animateMotion dur=2s fill=freeze><mpath href=\"#track\"/></animateMotion></rect>");
        At(document, 0);
        At(document, 3);
        Assert.True(IsRed(await PixelAsync(context, document, 90, 15)));

        var (scaleContext, scale) = await LoadAsync(
            "<rect width=40 height=40 fill=\"rgb(255,0,0)\"><animateTransform attributeName=transform type=scale values=\"1;2\" dur=2s fill=freeze /></rect>");
        At(scale, 0);
        At(scale, 3);
        Assert.True(IsRed(await PixelAsync(scaleContext, scale, 50, 50)));

        var (skewContext, skew) = await LoadAsync(
            "<rect width=40 height=40 fill=\"rgb(255,0,0)\"><animateTransform attributeName=transform type=skewX from=0 to=45 dur=2s fill=freeze /></rect>");
        At(skew, 0);
        At(skew, 3);
        Assert.True(IsRed(await PixelAsync(skewContext, skew, 35, 20)));
    }

    [Fact]
    public async Task AnAnimateOfTheTransformAttribute_IsMixedFunctionByFunction()
    {
        var (context, document) = await LoadAsync(
            "<rect width=40 height=40 fill=\"rgb(255,0,0)\"><animate attributeName=transform from=\"translate(0 0)\" to=\"translate(100 0)\" dur=2s fill=freeze /></rect>");
        At(document, 0);
        At(document, 3);
        Assert.True(IsWhite(await PixelAsync(context, document, 10, 10)));
        Assert.True(IsRed(await PixelAsync(context, document, 90, 10)));
    }

    [Fact]
    public async Task ASetOfTheTransformAttribute_AppliesFromItsBegin()
    {
        var (context, document) = await LoadAsync(
            "<rect width=40 height=40 fill=\"rgb(255,0,0)\"><set attributeName=transform to=\"translate(100 0)\" begin=1s /></rect>");
        At(document, 0);
        At(document, 0.5);
        Assert.True(IsRed(await PixelAsync(context, document, 10, 10)));
        At(document, 2);
        Assert.True(IsWhite(await PixelAsync(context, document, 10, 10)));
        Assert.True(IsRed(await PixelAsync(context, document, 90, 10)));
    }

    [Fact]
    public async Task APausedCssAnimationOnAnSvgElement_StaysWhereItBegan()
    {
        const string css = "@keyframes tint { from { fill: rgb(255,0,0) } to { fill: rgb(0,255,0) } } #r { animation: tint 2s linear forwards paused }";
        var (context, document) = await LoadAsync("<rect id=r width=200 height=100 fill=\"rgb(0,0,255)\" />", css);

        At(document, 0);
        At(document, 5);
        Assert.True(IsRed(await PixelAsync(context, document, 10, 10)));
        Assert.False(document.HasRunningAnimations);
    }

    // Loads a document whose images are fetched (a data: URI goes through the resource loader like any other address).
    private static async Task<(RasterRenderContext Context, HtmlDocument Document)> LoadWithImagesAsync(string body)
    {
        var context = await TestDocument.NewContextAsync();
        context.ResourceLoader = uri => HostResourceLoader.LoadAsync(uri);
        var document = await HtmlDocument.LoadAsync(TestDocument.Wrap(body, "<style>body { margin: 0 }</style>"), context, Playing());
        using var measuring = context.CreateCanvas(1, 1);
        await document.LayoutAsync(measuring);
        return (context, document);
    }

    private static string SvgImage(string body) =>
        "data:image/svg+xml;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"100\" viewBox=\"0 0 200 100\">{body}</svg>"));

    [Fact]
    public async Task AStandaloneSvgImage_IsPlayedLikeAnAnimatedImage()
    {
        var image = SvgImage("<rect width=\"200\" height=\"100\" fill=\"rgb(255,0,0)\"><animate attributeName=\"fill\" from=\"rgb(255,0,0)\" to=\"rgb(0,255,0)\" dur=\"2s\" fill=\"freeze\"/></rect>");
        var (context, document) = await LoadWithImagesAsync($"<img id=s src=\"{image}\" width=200 height=100>");

        Assert.True(document.HasRunningAnimations);
        At(document, 0);
        Assert.True(IsRed(await PixelAsync(context, document, 10, 10)));

        Assert.True(At(document, 1).IsRunning);
        var middle = await PixelAsync(context, document, 10, 10);
        Assert.InRange(middle.R, 90, 165);

        At(document, 3);
        Assert.True(IsGreen(await PixelAsync(context, document, 10, 10)));
        Assert.False(At(document, 4).IsRunning);
    }

    [Fact]
    public async Task TwoImagesOfOneStandaloneSvg_ShowTheSameMoment_AndRepaintOnlyTheirOwnRectangles()
    {
        var image = SvgImage("<rect width=\"200\" height=\"100\" fill=\"rgb(255,0,0)\"><animate attributeName=\"fill\" values=\"rgb(255,0,0);rgb(0,255,0)\" dur=\"2s\" fill=\"freeze\"/></rect>");
        var (context, document) = await LoadWithImagesAsync($"<img id=a src=\"{image}\" width=100 height=50><p style=\"height:80pt\">text</p><img id=b src=\"{image}\" width=100 height=50>");

        At(document, 0);
        var frame = At(document, 1);
        var a = document.GetElementBounds("a")!.Value;
        var b = document.GetElementBounds("b")!.Value;

        using var canvas = await TestDocument.PaintAsync(context, document);
        var first = TestDocument.Pixel(canvas, (int)a.X + 5, (int)a.Y + 5);
        var second = TestDocument.Pixel(canvas, (int)b.X + 5, (int)b.Y + 5);
        Assert.Equal(first.R, second.R);
        Assert.Equal(first.G, second.G);
        Assert.Equal(2, frame.DirtyRegions.Count);
    }

    [Fact]
    public async Task AStandaloneSvgWithNothingToPlay_IsStatic()
    {
        var image = SvgImage("<rect width=\"200\" height=\"100\" fill=\"rgb(255,0,0)\"/>");
        var (_, document) = await LoadWithImagesAsync($"<img src=\"{image}\" width=200 height=100>");

        At(document, 0);
        Assert.False(At(document, 1).IsRunning);
        Assert.False(document.HasRunningAnimations);
    }

    [Fact]
    public async Task ACssAnimationInsideAStandaloneSvg_IsPlayedWithTheImage()
    {
        var image = SvgImage("<style>@keyframes tint { from { fill: rgb(255,0,0) } to { fill: rgb(0,255,0) } } rect { animation: tint 2s linear forwards }</style><rect width=\"200\" height=\"100\" fill=\"rgb(0,0,255)\"/>");
        var (context, document) = await LoadWithImagesAsync($"<img id=s src=\"{image}\" width=200 height=100>");

        At(document, 0);
        Assert.True(IsRed(await PixelAsync(context, document, 10, 10)));

        Assert.True(At(document, 1).IsRunning);
        Assert.InRange((await PixelAsync(context, document, 10, 10)).R, 90, 165);

        At(document, 3);
        Assert.True(IsGreen(await PixelAsync(context, document, 10, 10)));
        Assert.False(At(document, 4).IsRunning);
    }

    [Fact]
    public async Task APausedCssAnimationInAStandaloneSvg_HoldsItsFirstFrame()
    {
        var image = SvgImage("<style>@keyframes tint { from { fill: rgb(255,0,0) } to { fill: rgb(0,255,0) } } rect { animation: tint 2s linear forwards paused }</style><rect width=\"200\" height=\"100\" fill=\"rgb(0,0,255)\"/>");
        var (context, document) = await LoadWithImagesAsync($"<img id=s src=\"{image}\" width=200 height=100>");

        At(document, 0);
        At(document, 5);
        Assert.True(IsRed(await PixelAsync(context, document, 10, 10)));
        Assert.False(document.HasRunningAnimations);
    }

    [Fact]
    public async Task AnAnimatedStandaloneSvg_AsAnObject_IsPlayedToo()
    {
        var image = SvgImage("<rect width=\"200\" height=\"100\" fill=\"rgb(255,0,0)\"><set attributeName=\"fill\" to=\"rgb(0,255,0)\" begin=\"1s\"/></rect>");
        var (context, document) = await LoadWithImagesAsync($"<object id=s data=\"{image}\" type=\"image/svg+xml\" width=200 height=100></object>");

        At(document, 0);
        At(document, 2);
        Assert.True(IsGreen(await PixelAsync(context, document, 10, 10)));
    }
}
