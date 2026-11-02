using PeachDrawing.Core;

namespace PeachBrackets.Tests;

public class TouchAndInertTests
{
    private static (double X, double Y) Center(HtmlDocument document, string id)
    {
        var bounds = document.GetElementBounds(id)!.Value;
        return (bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
    }

    private static async Task<TouchActions> TouchAsync(string style, string wrapperStyle = "")
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap($"<div style='{wrapperStyle}'><div id=a style='height:40px;{style}'></div></div>"));
        var (x, y) = Center(document, "a");
        return document.GetInteractionAt(0, x, y).TouchActions;
    }

    [Theory]
    [InlineData("", TouchActions.All)]
    [InlineData("touch-action: auto", TouchActions.All)]
    [InlineData("touch-action: none", TouchActions.None)]
    [InlineData("touch-action: manipulation", TouchActions.Pan | TouchActions.PinchZoom)]
    [InlineData("touch-action: pan-x", TouchActions.PanLeft | TouchActions.PanRight)]
    [InlineData("touch-action: pan-y pinch-zoom", TouchActions.PanUp | TouchActions.PanDown | TouchActions.PinchZoom)]
    [InlineData("touch-action: pan-left pan-down", TouchActions.PanLeft | TouchActions.PanDown)]
    [InlineData("touch-action: banana", TouchActions.All)]
    public async Task TouchActionNamesTheGesturesAllowed(string style, TouchActions expected) =>
        Assert.Equal(expected, await TouchAsync(style));

    [Fact]
    public async Task TheGesturesAreTheIntersectionWithTheAncestors()
    {
        Assert.Equal(TouchActions.PanLeft | TouchActions.PanRight, await TouchAsync("touch-action: manipulation", "touch-action: pan-x"));
        Assert.Equal(TouchActions.None, await TouchAsync("touch-action: pan-y", "touch-action: pan-x"));
        Assert.Equal(TouchActions.None, await TouchAsync("", "touch-action: none"));
    }

    [Theory]
    [InlineData("inert")]
    [InlineData("style='interactivity: inert'")]
    public async Task AnInertSubtreeTakesNoPartInInteraction(string attribute)
    {
        var (_, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap($"<div id=under style='height:40px;cursor:help'></div><div id=wall {attribute}><a id=l href='https://example.com/' style='display:block;height:40px;cursor:wait'>link</a></div>"),
            new HtmlDocumentOptions { RecordTextRuns = true });

        // The inert link lets the point through (to the body beneath) and is not a link, a text position or a pointer area.
        var (x, y) = Center(document, "l");
        Assert.Empty(document.Annotations.Links);
        Assert.Equal("body", document.GetElementAt(0, x, y)?.TagName); // falls through to what is beneath
        Assert.Equal(CursorShape.Default, document.GetCursorAt(0, x, y).Shape);
        Assert.Null(document.HitTest(0, x, y));
        var interaction = document.GetInteractionAt(0, x, y);
        Assert.True(interaction.Inert);
        Assert.False(interaction.CanSelectText);

        // What is not inert still works.
        (x, y) = Center(document, "under");
        Assert.False(document.GetInteractionAt(0, x, y).Inert);
        Assert.Equal(CursorShape.Help, document.GetCursorAt(0, x, y).Shape);
    }

    [Fact]
    public async Task AnInertElementCannotTakeFocus()
    {
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap("<input id=i inert>"));
        var measuring = context.CreateCanvas(1, 1);

        var result = await document.SetFocusAsync(document.GetElement("i"), focusVisible: true, measuring);

        Assert.False(result.Applied);
    }

    [Fact]
    public async Task OutsideAnyElementEverythingIsAllowed()
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap(""));

        var interaction = document.GetInteractionAt(0, 2, 2);

        Assert.Equal(new DocumentInteraction(false, TouchActions.All, true), interaction);
    }
}
