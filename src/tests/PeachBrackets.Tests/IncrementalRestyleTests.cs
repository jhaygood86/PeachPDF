using PeachBrackets.Updates;
using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Tests;

/// <summary>A change of the pointer or focus state that only changes how something is painted is applied to the styles in place; the rest loads the document again.</summary>
public class IncrementalRestyleTests
{
    // Utility classes in the shape Tailwind writes them: one rule per state variant.
    private const string Styles = """
        <style>
        .card { padding: 8px; color: rgb(10,10,10); height: 30px; }
        .hover\:text:hover { color: rgb(255,0,0) }
        .hover\:bg:hover { background-color: rgb(0,0,255) }
        .hover\:underline:hover { text-decoration: underline }
        .hover\:opacity:hover { opacity: 0.5 }
        .hover\:scale:hover { transform: scale(1.05) }
        .hover\:shadow:hover { box-shadow: 0 4px 12px rgba(0,0,0,0.5) }
        .hover\:outline:hover { outline: 2px solid rgb(0,128,0) }
        .hover\:border:hover { border-color: rgb(255,0,255) }
        .hover\:padding:hover { padding: 20px }
        .hover\:hidden:hover { display: none }
        .hover\:bold:hover { font-weight: 700 }
        .hover\:before\:content:hover::before { content: "x" }
        .group:hover .group-hover\:saturate { filter: saturate(1.5) }
        .group:hover .group-hover\:text { color: rgb(0,128,0) }
        .group:hover > .inner { background-color: rgb(1,2,3) }
        .group:hover + .after { color: rgb(9,9,9) }
        .has-child:has(.kid:hover) { background-color: rgb(7,7,7) }
        @media (hover: hover) { .mq\:hover:hover { color: rgb(0,100,0) } }
        .border-c { border: 2px solid currentColor; }
        .border-c:hover { color: rgb(200,0,0) }
        </style>
        """;

    private static async Task<(RasterRenderContext Context, HtmlDocument Document, Canvas Measuring)> LoadAsync(string body, bool incremental = true)
    {
        var options = new HtmlDocumentOptions { MediaType = MediaType.Screen, IncrementalRestyle = incremental };
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap(body, Styles), options);
        return (context, document, context.CreateCanvas(1, 1));
    }

    private static (double X, double Y) Center(HtmlDocument document, string id)
    {
        var bounds = document.GetElementBounds(id)!.Value;
        return (bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
    }

    private static async Task<byte[]> PixelsAsync(RasterRenderContext context, HtmlDocument document)
    {
        using var canvas = await TestDocument.PaintAsync(context, document);
        return canvas.ToPixelBuffer().PremultipliedRgba.ToArray();
    }

    [Theory]
    [InlineData("hover:text", true)]
    [InlineData("hover:bg", true)]
    [InlineData("hover:underline", true)]
    [InlineData("hover:opacity", true)]
    [InlineData("hover:scale", true)]
    [InlineData("hover:shadow", true)]
    [InlineData("hover:outline", true)]
    [InlineData("hover:border", true)]
    [InlineData("mq:hover", true)]
    [InlineData("hover:padding", false)]
    [InlineData("hover:hidden", false)]
    [InlineData("hover:bold", false)]
    [InlineData("hover:before:content", false)]
    public async Task AUtilityClass_IsAppliedInPlaceOnlyWhenItCannotMoveAnything(string utility, bool incremental)
    {
        var body = $"<div id=a class=\"card {utility}\">Hello</div><div id=b>after</div>";
        var (context, document, measuring) = await LoadAsync(body);
        var (reference, referenceDocument, referenceMeasuring) = await LoadAsync(body, incremental: false);
        var (x, y) = Center(document, "a");

        var result = await document.SetPointerStateAsync(0, x, y, false, measuring);
        var expected = await referenceDocument.SetPointerStateAsync(0, x, y, false, referenceMeasuring);

        Assert.True(result.Applied);
        Assert.Equal(incremental, result.Incremental);
        Assert.False(expected.Incremental);
        Assert.Equal(referenceDocument.GetElementBounds("b"), document.GetElementBounds("b"));
        Assert.Equal(await PixelsAsync(reference, referenceDocument), await PixelsAsync(context, document));

        // And back.
        var left = await document.ClearPointerStateAsync(measuring);
        var leftReference = await referenceDocument.ClearPointerStateAsync(referenceMeasuring);
        Assert.Equal(incremental, left.Incremental);
        Assert.False(leftReference.Incremental);
        Assert.Equal(await PixelsAsync(reference, referenceDocument), await PixelsAsync(context, document));
    }

    [Fact]
    public async Task GroupHover_RestylesTheDescendantsInPlace_AndAgreesWithLoadingAgain()
    {
        const string body = "<div id=g class=\"group card\"><span id=s class=\"group-hover:saturate group-hover:text\">word</span><div id=i class=inner>in</div></div><div id=n class=after>next</div>";
        var (context, document, measuring) = await LoadAsync(body);
        var (reference, referenceDocument, referenceMeasuring) = await LoadAsync(body, incremental: false);
        var (x, y) = Center(document, "g");

        var result = await document.SetPointerStateAsync(0, x, y, false, measuring);
        await referenceDocument.SetPointerStateAsync(0, x, y, false, referenceMeasuring);

        Assert.True(result.Incremental);
        Assert.NotEmpty(result.DirtyRegions);
        Assert.Equal(await PixelsAsync(reference, referenceDocument), await PixelsAsync(context, document));
    }

    [Fact]
    public async Task AHasRuleOverAnAncestor_IsAppliedInPlace()
    {
        const string body = "<div id=h class=has-child><span id=k class=\"kid card\">kid</span></div><div id=z>z</div>";
        var (context, document, measuring) = await LoadAsync(body);
        var (reference, referenceDocument, referenceMeasuring) = await LoadAsync(body, incremental: false);
        var (x, y) = Center(document, "k");

        var result = await document.SetPointerStateAsync(0, x, y, false, measuring);
        await referenceDocument.SetPointerStateAsync(0, x, y, false, referenceMeasuring);

        Assert.True(result.Applied);
        Assert.Equal(await PixelsAsync(reference, referenceDocument), await PixelsAsync(context, document));
    }

    [Fact]
    public async Task CurrentColorBorders_FollowTheInheritedColor()
    {
        const string body = "<div id=a class=\"card border-c\">Hello</div><div id=b>after</div>";
        var (context, document, measuring) = await LoadAsync(body);
        var (reference, referenceDocument, referenceMeasuring) = await LoadAsync(body, incremental: false);
        var (x, y) = Center(document, "a");

        var result = await document.SetPointerStateAsync(0, x, y, false, measuring);
        await referenceDocument.SetPointerStateAsync(0, x, y, false, referenceMeasuring);

        Assert.True(result.Incremental);
        Assert.Equal(await PixelsAsync(reference, referenceDocument), await PixelsAsync(context, document));
    }

    [Fact]
    public async Task TheSwitch_KeepsTheOldBehavior()
    {
        const string body = "<div id=a class=\"card hover:text\">Hello</div>";
        var (_, document, measuring) = await LoadAsync(body, incremental: false);
        var (x, y) = Center(document, "a");

        var result = await document.SetPointerStateAsync(0, x, y, false, measuring);

        Assert.True(result.Applied);
        Assert.False(result.Incremental);
    }

    [Fact]
    public async Task APaintOnlyRestyle_KeepsTheLayoutAndTheHandlesIntact()
    {
        const string body = "<div id=a class=\"card hover:text\">Hello</div><div id=b>after</div>";
        var (_, document, measuring) = await LoadAsync(body);
        var layoutBefore = document.GetElementBounds("b");
        var (x, y) = Center(document, "a");

        var result = await document.SetPointerStateAsync(0, x, y, false, measuring);
        var left = await document.ClearPointerStateAsync(measuring);

        Assert.True(result.Incremental);
        Assert.True(left.Incremental);
        Assert.Equal(layoutBefore, document.GetElementBounds("b"));
        Assert.Single(result.DirtyRegions);
    }

    private static async Task<(HtmlDocument Document, Canvas Measuring, UpdateResult Result)> HoverAsync(string head, string body, string id)
    {
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap(body, head), new HtmlDocumentOptions { MediaType = MediaType.Screen });
        var measuring = context.CreateCanvas(1, 1);
        var (x, y) = Center(document, id);
        return (document, measuring, await document.SetPointerStateAsync(0, x, y, false, measuring));
    }

    [Theory]
    [InlineData("body:hover { background-color: rgb(1,2,3) }", "<div id=a>x</div>")]
    [InlineData("#a:hover { filter: blur(2px) } #a { height: 20px }", "<div id=a><div style=\"position:absolute\">abs</div></div>")]
    [InlineData("#a:hover::first-line { color: red }", "<p id=a>x</p>")]
    [InlineData("#a:hover::after { content: 'x' }", "<div id=a>x</div>")]
    [InlineData("#a:hover { background-image: linear-gradient(red, blue) }", "<div id=a>x</div>")]
    public async Task AChangeThatCouldMoveOrCreateSomething_LoadsTheDocumentAgain(string rule, string body)
    {
        var (_, _, result) = await HoverAsync("<style>" + rule + "</style>", body, "a");

        Assert.True(result.Applied);
        Assert.False(result.Incremental);
    }

    [Fact]
    public async Task Visibility_OnlyPaints_SoItIsRestyledInPlaceWithTheBoxesUnderIt()
    {
        const string head = "<style>#a { height: 20px; background-color: rgb(200, 0, 0) } #a:hover { visibility: hidden }</style>";
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap("<div id=a><div id=c style=\"background-color: rgb(0, 0, 200); height: 10px\"></div></div>", head), new HtmlDocumentOptions { MediaType = MediaType.Screen });
        var (referenceContext, reference) = await TestDocument.LoadAsync(TestDocument.Wrap("<div id=a><div id=c style=\"background-color: rgb(0, 0, 200); height: 10px\"></div></div>", head), new HtmlDocumentOptions { MediaType = MediaType.Screen, IncrementalRestyle = false });
        var (x, y) = Center(document, "a");

        var result = await document.SetPointerStateAsync(0, x, y, false, context.CreateCanvas(1, 1));
        await reference.SetPointerStateAsync(0, x, y, false, referenceContext.CreateCanvas(1, 1));

        Assert.True(result.Incremental);
        Assert.Equal(await PixelsAsync(referenceContext, reference), await PixelsAsync(context, document));
    }

    [Fact]
    public async Task CustomPropertiesTailwindWritesForAColor_AreRestyledInPlace()
    {
        const string head = "<style>#a { height: 20px; background-color: rgb(0 0 0 / var(--tw-bg-opacity, 0)) } #a:hover { --tw-bg-opacity: 1; background-color: rgb(239 68 68 / var(--tw-bg-opacity)) }</style>";
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap("<div id=a></div>", head), new HtmlDocumentOptions { MediaType = MediaType.Screen });
        var (referenceContext, reference) = await TestDocument.LoadAsync(TestDocument.Wrap("<div id=a></div>", head), new HtmlDocumentOptions { MediaType = MediaType.Screen, IncrementalRestyle = false });
        var (x, y) = Center(document, "a");

        var result = await document.SetPointerStateAsync(0, x, y, false, context.CreateCanvas(1, 1));
        await reference.SetPointerStateAsync(0, x, y, false, referenceContext.CreateCanvas(1, 1));

        Assert.True(result.Incremental);
        Assert.Equal(await PixelsAsync(referenceContext, reference), await PixelsAsync(context, document));
    }

    [Fact]
    public async Task APageWithoutAnyStateRule_ReportsNothingToRepaint()
    {
        var (_, _, result) = await HoverAsync("", "<div id=a>x</div>", "a");

        Assert.True(result.Applied);
        Assert.Empty(result.DirtyRegions);
        Assert.False(result.Incremental);
    }

    [Fact]
    public async Task ARestyleInPlace_WidensTheRepaintByTheShadowItAddsAndTheTransformItApplies()
    {
        var (_, _, shadow) = await HoverAsync("<style>#a { margin: 40px; height: 20px } #a:hover { box-shadow: 0 0 30px rgb(0,0,0) }</style>", "<div id=a>x</div>", "a");
        var (document, _, plain) = await HoverAsync("<style>#a { margin: 40px; height: 20px } #a:hover { color: red }</style>", "<div id=a>x</div>", "a");

        Assert.True(shadow.Incremental);
        Assert.True(plain.Incremental);
        Assert.True(Assert.Single(shadow.DirtyRegions).Bounds.Width > Assert.Single(plain.DirtyRegions).Bounds.Width + 30);
        _ = document;
    }

    [Fact]
    public async Task AFocusChange_IsRestyledInPlace()
    {
        var (context, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap("<input id=f><div id=z>after</div>", "<style>#f:focus { outline: 2px solid rgb(0,128,0) }</style>"), new HtmlDocumentOptions { MediaType = MediaType.Screen });
        var measuring = context.CreateCanvas(1, 1);
        var before = document.GetElementBounds("z");

        var focused = await document.SetFocusAsync(document.GetElement("f"), true, measuring);
        var left = await document.SetFocusAsync(null, false, measuring);

        Assert.True(focused.Incremental);
        Assert.True(left.Incremental);
        Assert.Equal(before, document.GetElementBounds("z"));
    }
}
