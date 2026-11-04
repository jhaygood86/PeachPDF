using PeachBrackets.Accessibility;
using PeachDrawing;

namespace PeachBrackets.Tests;

public class AccessibilityFocusTests
{
    private static async Task<(RasterRenderContext Context, HtmlDocument Document, PeachDrawing.Core.Canvas Measuring)> LoadAsync(string body, string head = "")
    {
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap(body, head), new HtmlDocumentOptions { MediaType = MediaType.Screen });
        return (context, document, context.CreateCanvas(1, 1));
    }

    private static string[] Names(IEnumerable<AccessibilityNode> nodes) => nodes.Select(n => n.Name).ToArray();

    [Fact]
    public async Task TabVisitsWhatCanBeFocusedInDocumentOrder()
    {
        var (_, document, _) = await LoadAsync("<a href='#'>link</a><button>button</button><input type=text aria-label='input'><p>text</p><select aria-label='select'></select>");

        Assert.Equal(["link", "button", "input", "select"], Names(document.GetAccessibilityTree().FocusOrder));
    }

    [Fact]
    public async Task APositiveTabindexGoesFirstLowestFirst_AndANegativeOneIsNotAStop()
    {
        var (_, document, _) = await LoadAsync(
            "<a href='#'>first in source</a><div role=button tabindex=2>two</div><div role=button tabindex=0>zero</div><div role=button tabindex=1>one</div>"
            + "<div role=button tabindex=-1>skipped</div><div role=button tabindex=1>another one</div>");

        var tree = document.GetAccessibilityTree();
        Assert.Equal(["one", "another one", "two", "first in source", "zero"], Names(tree.FocusOrder));
        // It can still be focused: it is only not a stop.
        Assert.True(tree.Nodes.Single(n => n.Name == "skipped").IsFocusable);
    }

    [Fact]
    public async Task WhatIsDisabledHiddenOrInertIsNotAStop()
    {
        var (_, document, _) = await LoadAsync(
            "<button>ok</button><button disabled>disabled</button><fieldset disabled><button>in fieldset</button></fieldset>"
            + "<button hidden>hidden</button><button style='display:none'>none</button><div inert><button>inert</button></div><button aria-hidden='true'>aria hidden</button>");

        Assert.Equal(["ok"], Names(document.GetAccessibilityTree().FocusOrder));
    }

    [Fact]
    public async Task NextFocusWalksBothWaysAndFallsOffTheEnds()
    {
        var (_, document, _) = await LoadAsync("<button>a</button><button>b</button><button>c</button>");
        var tree = document.GetAccessibilityTree();
        var (a, b, c) = (tree.FocusOrder[0], tree.FocusOrder[1], tree.FocusOrder[2]);

        Assert.Same(a, tree.NextFocus(null, forward: true));
        Assert.Same(c, tree.NextFocus(null, forward: false));
        Assert.Same(b, tree.NextFocus(a, forward: true));
        Assert.Same(a, tree.NextFocus(b, forward: false));
        Assert.Null(tree.NextFocus(c, forward: true));
        Assert.Null(tree.NextFocus(a, forward: false));
    }

    [Fact]
    public async Task FromSomethingThatIsNotAStopTheNextOneInDocumentOrderIsUsed()
    {
        var (_, document, _) = await LoadAsync("<button>a</button><div role=button tabindex=-1>mid</div><button>b</button><p>after</p>");
        var tree = document.GetAccessibilityTree();
        var mid = tree.Nodes.Single(n => n.Name == "mid");

        Assert.Equal("b", tree.NextFocus(mid, forward: true)!.Name);
        Assert.Equal("a", tree.NextFocus(mid, forward: false)!.Name);
        Assert.Equal("b", tree.NextFocus(tree.Nodes.Single(n => n.Role == AccessibilityRole.Paragraph), forward: false)!.Name);
        Assert.Null(tree.NextFocus(tree.Root, forward: true) is { } root && root.Name == "never" ? root : null);
    }

    [Fact]
    public async Task ADocumentWithNothingToFocusHasNoStops()
    {
        var (_, document, measuring) = await LoadAsync("<p>just text</p>");

        Assert.Empty(document.GetAccessibilityTree().FocusOrder);
        Assert.Null(document.GetAccessibilityTree().NextFocus(null, forward: true));
        var moved = await document.MoveFocusAsync(forward: true, measuring);
        Assert.Null(moved.Node);
        Assert.True(moved.LeftDocument);
    }

    [Fact]
    public async Task MoveFocusAsyncStepsThroughTheStopsAndThenLeavesTheDocument()
    {
        var (_, document, measuring) = await LoadAsync("<button>a</button><button>b</button>");

        Assert.Null(document.FocusedNode);
        var first = await document.MoveFocusAsync(forward: true, measuring);
        Assert.Equal("a", first.Node!.Name);
        Assert.False(first.LeftDocument);
        Assert.Equal("a", document.FocusedNode!.Name);

        var second = await document.MoveFocusAsync(forward: true, measuring);
        Assert.Equal("b", second.Node!.Name);

        var back = await document.MoveFocusAsync(forward: false, measuring);
        Assert.Equal("a", back.Node!.Name);

        await document.MoveFocusAsync(forward: true, measuring);
        var past = await document.MoveFocusAsync(forward: true, measuring);
        Assert.Null(past.Node);
        Assert.True(past.LeftDocument);
        Assert.Null(document.FocusedNode);

        // From outside, Shift+Tab goes to the last stop.
        var last = await document.MoveFocusAsync(forward: false, measuring);
        Assert.Equal("b", last.Node!.Name);
    }

    [Fact]
    public async Task TheFocusStyleFollowsTheFocus_AndFocusVisibleMatchesForTheKeyboard()
    {
        var (context, document, measuring) = await LoadAsync(
            "<button id=a style='display:block;width:60px;height:20px;border:0;background:rgb(0,0,255)'>a</button>"
            + "<button id=b style='display:block;width:60px;height:20px;border:0;background:rgb(0,0,255)'>b</button>",
            "<style>button:focus-visible { background: rgb(255,0,0) !important }</style>");

        async Task<byte> RedAsync(string id)
        {
            using var canvas = await TestDocument.PaintAsync(context, document);
            var bounds = document.GetElementBounds(id)!.Value;
            return TestDocument.Pixel(canvas, (int)(bounds.X + 10), (int)(bounds.Y + 5)).R;
        }

        Assert.Equal(0, await RedAsync("a"));
        await document.MoveFocusAsync(forward: true, measuring);
        Assert.Equal(255, await RedAsync("a"));
        Assert.Equal(0, await RedAsync("b"));

        await document.MoveFocusAsync(forward: true, measuring);
        Assert.Equal(0, await RedAsync("a"));
        Assert.Equal(255, await RedAsync("b"));
    }

    [Fact]
    public async Task OnlyAnElementThatCanTakeTheFocusIsFocused()
    {
        var (_, document, measuring) = await LoadAsync("<button>b</button><p>text</p>");
        var tree = document.GetAccessibilityTree();

        var onText = await document.FocusAsync(tree.Nodes.Single(n => n.Role == AccessibilityRole.Paragraph), focusVisible: true, measuring);
        Assert.False(onText.Applied);
        Assert.Null(document.FocusedNode);

        var onRoot = await document.FocusAsync(tree.Root, focusVisible: true, measuring);
        Assert.False(onRoot.Applied);

        var onButton = await document.FocusAsync(tree.FocusOrder[0], focusVisible: false, measuring);
        Assert.True(onButton.Applied || document.FocusedNode is not null);
        Assert.Equal("b", document.FocusedNode!.Name);
        Assert.Throws<ArgumentNullException>(() => { _ = document.FocusAsync(null!, true, measuring); });
    }
}
