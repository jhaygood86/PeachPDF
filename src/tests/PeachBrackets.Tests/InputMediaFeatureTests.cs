using PeachDrawing.Core;

namespace PeachBrackets.Tests;

public class InputMediaFeatureTests
{
    // Each query turns the box's cursor to a distinct value when it matches, so the cursor read back says which queries matched.
    private const string Styles = """
        <style>
        #a { height: 20px }
        @media (hover: hover) { #a { cursor: help } }
        @media (hover: none) { #a { cursor: wait } }
        @media (pointer: fine) { #b { cursor: crosshair } }
        @media (pointer: coarse) { #b { cursor: move } }
        @media (pointer: none) { #b { cursor: not-allowed } }
        @media (any-pointer: coarse) { #c { cursor: grab } }
        @media (any-pointer: fine) { #c { cursor: grabbing } }
        @media (any-hover) { #d { cursor: zoom-in } }
        @media (hover: hover) and (pointer: fine) { #e { cursor: zoom-out } }
        </style>
        """;

    private const string Body = "<div id=a></div><div id=b style='height:20px'></div><div id=c style='height:20px'></div><div id=d style='height:20px'></div><div id=e style='height:20px'></div>";

    private static async Task<IReadOnlyDictionary<string, CursorShape>> CursorsAsync(InputCapabilities? input)
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap(Body, Styles), new HtmlDocumentOptions { Input = input, Media = "screen" });
        var result = new Dictionary<string, CursorShape>();
        foreach (var id in new[] { "a", "b", "c", "d", "e" })
        {
            var bounds = document.GetElementBounds(id)!.Value;
            result[id] = document.GetCursorAt(0, bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2)).Shape;
        }

        return result;
    }

    [Fact]
    public async Task ByDefaultThereIsNothingToPointWith()
    {
        var cursors = await CursorsAsync(null);

        Assert.Equal(CursorShape.Wait, cursors["a"]);
        Assert.Equal(CursorShape.NotAllowed, cursors["b"]);
        Assert.Equal(CursorShape.Default, cursors["c"]);
        Assert.Equal(CursorShape.Default, cursors["d"]);
        Assert.Equal(CursorShape.Default, cursors["e"]);
    }

    [Fact]
    public async Task AMouseHoversAndIsFine()
    {
        var cursors = await CursorsAsync(InputCapabilities.Mouse);

        Assert.Equal(CursorShape.Help, cursors["a"]);
        Assert.Equal(CursorShape.Crosshair, cursors["b"]);
        Assert.Equal(CursorShape.Grabbing, cursors["c"]);
        Assert.Equal(CursorShape.ZoomIn, cursors["d"]);
        Assert.Equal(CursorShape.ZoomOut, cursors["e"]);
    }

    [Fact]
    public async Task ATouchScreenCannotHoverAndIsCoarse()
    {
        var cursors = await CursorsAsync(InputCapabilities.Touch);

        Assert.Equal(CursorShape.Wait, cursors["a"]);
        Assert.Equal(CursorShape.Move, cursors["b"]);
        Assert.Equal(CursorShape.Grab, cursors["c"]);
        Assert.Equal(CursorShape.Default, cursors["d"]);
        Assert.Equal(CursorShape.Default, cursors["e"]);
    }

    [Fact]
    public async Task TheAnyFormsSeeEveryDevice_WhileThePrimaryOnesSeeOne()
    {
        // A touch screen first, with a mouse also attached.
        var both = new InputCapabilities(PointerTypes.Coarse, false, PointerTypes.Coarse | PointerTypes.Fine, true);
        var cursors = await CursorsAsync(both);

        Assert.Equal(CursorShape.Wait, cursors["a"]);
        Assert.Equal(CursorShape.Move, cursors["b"]);
        Assert.Equal(CursorShape.Grabbing, cursors["c"]); // both any-pointer rules match; the later one wins
        Assert.Equal(CursorShape.ZoomIn, cursors["d"]);
        Assert.Equal(CursorShape.Default, cursors["e"]); // the primary device cannot hover
    }
}
