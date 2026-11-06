using AppKit;
using CoreGraphics;
using Foundation;
using PeachBrackets.Updates;

// Parallel tests would fight over one NSApplication and one run loop.
[assembly: Xunit.CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]

namespace PeachBrackets.Apple.AppTests;

public class HtmlViewTests
{
    private const string Page = """
        <style>
        body { margin: 0; font: 16px sans-serif }
        #red { width: 100px; height: 100px; background: #ff0000 }
        .card { width: 200px; height: 60px; background: #dfeaf7 }
        .card:hover { background: #f7e3c4 }
        a#link { display: block; padding: 8px }
        </style>
        <h1 id="title">Title</h1>
        <div id="red"></div>
        <div class="card" id="card"></div>
        <a id="link" href="https://example.com/">go</a>
        <form>
          <input type="checkbox" id="agree">
          <input type="text" id="name" style="width: 150px">
          <input type="range" id="level" min="0" max="10" value="2" style="width: 200px">
          <button type="submit" id="send">Send</button>
        </form>
        <div style="height: 2000px"></div>
        """;

    private static async Task<(byte R, byte G, byte B)> ColorAtAsync(ViewHarness harness, double x, double y) =>
        await harness.PixelAtAsync(x, y) ?? throw new InvalidOperationException($"No tile painted at {x},{y}");

    private static void AssertNear((byte R, byte G, byte B) actual, (int R, int G, int B) expected, int tolerance = 3)
    {
        Assert.InRange(actual.R, expected.R - tolerance, expected.R + tolerance);
        Assert.InRange(actual.G, expected.G - tolerance, expected.G + tolerance);
        Assert.InRange(actual.B, expected.B - tolerance, expected.B + tolerance);
    }

    [Fact]
    public async Task ItLoadsAndPaintsTheDocument()
    {
        var harness = await ViewHarness.LoadAsync(Page);
        Assert.Single(harness.View.Document!.Pages);

        // The middle of the red box, in CSS pixels from the top-left of the content.
        var bounds = harness.View.Document!.GetElementBounds("red")!.Value;
        AssertNear(await ColorAtAsync(harness, (bounds.X + (bounds.Width / 2)) * 96 / 72, (bounds.Y + (bounds.Height / 2)) * 96 / 72), (255, 0, 0));
    }

    [Fact]
    public async Task HoverRestylesAndRepaintsOnlyWhatChanged()
    {
        var harness = await ViewHarness.LoadAsync(Page);
        var card = harness.View.Document!.GetElementBounds("card")!.Value;
        var x = (card.X + 20) * 96 / 72;
        var y = (card.Y + (card.Height / 2)) * 96 / 72;
        AssertNear(await ColorAtAsync(harness, x, y), (0xdf, 0xea, 0xf7));

        await harness.MoveMouseToAsync("card");
        await ViewHarness.Eventually(() => harness.PixelAt(x, y) is { } c && Math.Abs(c.R - 0xf7) < 4, 5);
        AssertNear(await ColorAtAsync(harness, x, y), (0xf7, 0xe3, 0xc4));

        // The red box was not touched, and is still painted.
        var red = harness.View.Document!.GetElementBounds("red")!.Value;
        AssertNear(await ColorAtAsync(harness, (red.X + 40) * 96 / 72, (red.Y + 40) * 96 / 72), (255, 0, 0));
    }

    [Fact]
    public async Task ClickingALinkRaisesLinkActivatedAndCanBeCancelled()
    {
        var harness = await ViewHarness.LoadAsync(Page);
        string? uri = null;
        await MainThread.Run(() => harness.View.LinkActivated += (_, e) =>
        {
            uri = e.Uri;
            e.Cancel = true;
        });

        await harness.ClickAsync("link");
        await ViewHarness.Eventually(() => uri is not null);
        Assert.Equal("https://example.com/", uri);
    }

    [Fact]
    public async Task ACheckBoxToggles()
    {
        var harness = await ViewHarness.LoadAsync(Page);
        await harness.ClickAsync("agree");
        await ViewHarness.Eventually(() => harness.View.Document!.GetFormState(harness.View.Document.GetElement("agree")!) is { Checked: true });
    }

    [Fact]
    public async Task TypingFillsATextFieldAndSubmitRaisesFormSubmitting()
    {
        var harness = await ViewHarness.LoadAsync(Page);
        var submitted = false;
        await MainThread.Run(() => harness.View.FormSubmitting += (_, _) => submitted = true);

        await harness.ClickAsync("name");
        await harness.TypeAsync("a b");
        await ViewHarness.Eventually(() => harness.View.Document!.GetFormState(harness.View.Document.GetElement("name")!)?.Value == "a b");

        await harness.ClickAsync("send");
        await ViewHarness.Eventually(() => submitted);
    }

    [Fact]
    public async Task DraggingARangeSetsItsValue()
    {
        var harness = await ViewHarness.LoadAsync(Page);
        await harness.DragAsync("level", 0.05, 0.97);
        await ViewHarness.Eventually(() => harness.View.Document!.GetFormState(harness.View.Document.GetElement("level")!)?.Value == "10");
    }

    [Fact]
    public async Task ItExposesTheHeadingAndTheLinkToVoiceOver()
    {
        var harness = await ViewHarness.LoadAsync(Page);
        var children = await MainThread.Run(() => harness.View.DocumentView!.AccessibilityChildren ?? []);
        var parts = children.OfType<NSAccessibilityElement>().Select(p => (p.AccessibilityRole, p.AccessibilityLabel)).ToList();
        Assert.Contains(("AXHeading", "Title"), parts);
        Assert.Contains(("AXLink", "go"), parts);
    }

    [Fact]
    public async Task AStickyHeaderStaysAtTheTopOfTheViewWhileTheContentScrolls()
    {
        // The regression: the layer of what the viewport holds was painted by a region around what is in view, which culled a stuck box (its layout position is still
        // where it would be if it scrolled), so a sticky header disappeared as soon as it stuck.
        const string sticky = """
            <style>body { margin: 0 } .top { position: sticky; top: 0; height: 40px; background: #243b6b }
            .row { height: 40px; background: #ffffff; border-bottom: 1px solid #ccc }</style>
            <div class="top"></div>
            <div style="height: 3000px; background: linear-gradient(#ffffff, #ffffee)"></div>
            """;
        var harness = await ViewHarness.LoadAsync(sticky);
        await MainThread.Run(() =>
        {
            var clip = harness.View.ContentView;
            clip.ScrollToPoint(new CGPoint(0, 1500));
            harness.View.ReflectScrolledClipView(clip);
        });

        // The view's own layer holds the overlay: its pixel at the top of what is in view is the header's blue.
        await ViewHarness.Eventually(() => harness.OverlayPixel(10, 10) is { } c && Math.Abs(c.B - 0x6b) < 6 && Math.Abs(c.R - 0x24) < 6, 10);
    }

    [Fact]
    public async Task ItLaysTheDocumentOutAgainWhenTheWindowIsResized()
    {
        const string wide = "<style>body { margin: 0 }</style><div id=\"wide\" style=\"height: 10px; background: #000\"></div>";
        var harness = await ViewHarness.LoadAsync(wide, width: 800, height: 400);
        var before = harness.View.Document!.GetElementBounds("wide")!.Value.Width;

        await MainThread.Run(() => harness.Window.SetContentSize(new CGSize(400, 400)));
        await harness.WaitForLoadsAsync(2);
        var after = harness.View.Document!.GetElementBounds("wide")!.Value.Width;
        Assert.True(after < before * 0.7, $"the document was {before} pt wide and is {after} pt wide after the window got narrower");
    }

    [Fact]
    public async Task TakingTheViewOutOfItsWindowDropsTheDocument()
    {
        var harness = await ViewHarness.LoadAsync(Page);
        await MainThread.Run(() => harness.Window.ContentView = new NSView());
        await ViewHarness.Eventually(() => harness.View.Document is null);
    }
}
