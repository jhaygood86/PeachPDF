using PeachDrawing;
using PeachBrackets.Text;
using PeachDrawing.Core;

namespace PeachBrackets.Tests;

/// <summary>
/// <c>content-visibility: auto</c> on a screen (css-contain-2 §4): the contents of an element that is not relevant to the user - its box lies away from the
/// viewport and it holds no focus - are skipped, and it is as large as an empty box, or its <c>contain-intrinsic-size</c>, while they are.
/// </summary>
public class ContentVisibilityAutoTests
{
    private static HtmlDocumentOptions Screen(bool skip = true)
    {
        var options = new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = 400, ViewportHeightPoints = 300, SkipOffscreenContent = skip };
        options.SetMargins(0);
        return options;
    }

    // Five sections of 400pt of red under `content-visibility: auto`, each standing for 100pt (or what it last measured) while its contents are skipped.
    private static string Sections(string style = "contain-intrinsic-size: auto 100pt") =>
        TestDocument.Wrap(
            string.Concat(Enumerable.Range(0, 5).Select(i => $"<div id=s{i} style='content-visibility: auto; {style}'><div style='height:400pt;background:red'></div></div>")));

    private static async Task<bool> IsRedAsync(RasterRenderContext context, HtmlDocument document, double y)
    {
        // Below the end of the surface there is nothing, red or otherwise.
        if (y >= document.Pages[0].HeightPoints) return false;

        var canvas = await TestDocument.PaintAsync(context, document);
        var p = TestDocument.Pixel(canvas, 200, (int)y);

        return p.R > 200 && p.G < 60 && p.B < 60;
    }

    private static double Top(HtmlDocument document, string id) => document.GetElementBounds(id)!.Value.Y;

    private static double Height(HtmlDocument document, string id) => document.GetElementBounds(id)!.Value.Height;

    [Fact]
    public async Task OnlyTheElementsNearTheViewportKeepTheirContentsAndTheOthersStandInForThem()
    {
        var (context, document) = await TestDocument.LoadAsync(Sections(), Screen());

        // Shown, the first two sections are 400pt each. The viewport is 300pt tall and half of it again counts as near, so the third (at 800pt) is not.
        Assert.True(await IsRedAsync(context, document, 100));
        Assert.True(await IsRedAsync(context, document, 500));
        Assert.False(await IsRedAsync(context, document, 900));
        Assert.False(await IsRedAsync(context, document, 1500));

        // Skipped, a section is as large as the size it last had when it was shown (auto), so the document is as tall as if all were.
        Assert.Equal(400, Height(document, "s2"), 3);
        Assert.Equal(800, Top(document, "s2"), 3);
        Assert.Equal(2000, document.Pages[0].HeightPoints, 3);
        Assert.False(document.ContentVisibilityNeedsUpdate);
    }

    [Fact]
    public async Task AnElementIsAsLargeAsItsContainIntrinsicSizeWhileItsContentsAreSkippedWhenItNeverWasShown()
    {
        var (context, document) = await TestDocument.LoadAsync(Sections("contain-intrinsic-size: 90pt"), Screen());

        // Without auto nothing is remembered: a skipped section is its 90pt, wherever it was before.
        Assert.Equal(400, Height(document, "s0"), 3);
        Assert.Equal(400, Height(document, "s1"), 3);
        Assert.Equal(90, Height(document, "s2"), 3);
        Assert.Equal(90, Height(document, "s4"), 3);
        Assert.False(await IsRedAsync(context, document, 900));
    }

    [Fact]
    public async Task MovingTheViewportShowsWhatCameNearAndSkipsWhatWentAway()
    {
        var (context, document) = await TestDocument.LoadAsync(Sections(), Screen());
        using var measuring = context.CreateCanvas(1, 1);

        document.SetViewport(new Rect(0, 1200, 400, 300));
        Assert.True(document.ContentVisibilityNeedsUpdate);

        var result = await document.UpdateContentVisibilityAsync(measuring);

        Assert.True(result.Applied);
        Assert.NotEmpty(result.DirtyRegions);
        Assert.False(document.ContentVisibilityNeedsUpdate);
        Assert.True(await IsRedAsync(context, document, 1300));
        Assert.False(await IsRedAsync(context, document, 100));
        Assert.False(await IsRedAsync(context, document, 500));

        // What was shown keeps its size when it is skipped, so nothing moved under the host's scroll position.
        Assert.Equal(400, Height(document, "s0"), 3);
        Assert.Equal(2000, document.Pages[0].HeightPoints, 3);

        // And back.
        document.SetViewport(new Rect(0, 0, 400, 300));
        Assert.True((await document.UpdateContentVisibilityAsync(measuring)).Applied);
        Assert.True(await IsRedAsync(context, document, 100));
        Assert.False(await IsRedAsync(context, document, 1300));
    }

    [Fact]
    public async Task AViewportThatDidNotMoveFarChangesNothing()
    {
        var (context, document) = await TestDocument.LoadAsync(Sections(), Screen());
        using var measuring = context.CreateCanvas(1, 1);

        document.SetViewport(new Rect(0, 40, 400, 300));
        Assert.False(document.ContentVisibilityNeedsUpdate);

        var result = await document.UpdateContentVisibilityAsync(measuring);

        Assert.False(result.Applied);
        Assert.Empty(result.DirtyRegions);
    }

    [Fact]
    public async Task ADocumentThatDidNotAskToSkipShowsEverything()
    {
        var (context, document) = await TestDocument.LoadAsync(Sections(), Screen(skip: false));
        using var measuring = context.CreateCanvas(1, 1);

        Assert.True(await IsRedAsync(context, document, 1900));
        Assert.False(document.ContentVisibilityNeedsUpdate);

        var result = await document.UpdateContentVisibilityAsync(measuring);
        Assert.False(result.Applied);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public async Task APagedDocumentNeverSkips()
    {
        var options = new HtmlDocumentOptions { MediaType = MediaType.Print, SkipOffscreenContent = true };
        var (context, document) = await TestDocument.LoadAsync(Sections(), options);

        Assert.False(document.ContentVisibilityNeedsUpdate);
        Assert.True(document.Pages.Count >= 2);
        Assert.NotNull(context);
    }

    [Fact]
    public async Task AnElementThatHoldsTheFocusIsRelevantWhereverItIs()
    {
        var html = TestDocument.Wrap(
            "<div style='height:2000pt'></div><div id=far style='content-visibility: auto; contain-intrinsic-size: 50pt'><div style='height:300pt;background:red'><input id=field></div></div>");
        var (context, document) = await TestDocument.LoadAsync(html, Screen());
        using var measuring = context.CreateCanvas(1, 1);

        Assert.Equal(50, Height(document, "far"), 3);

        // Focus is on something inside it that is not there to be focused, so it stays skipped; a host that has focus inside it asks for it by showing it.
        Assert.False(document.ContentVisibilityNeedsUpdate);
    }

    private static HtmlDocumentOptions ScreenWithRuns()
    {
        var options = Screen();
        options.RecordTextRuns = true;
        return options;
    }

    private static string TextSections() =>
        TestDocument.Wrap(string.Concat(Enumerable.Range(0, 6).Select(i =>
            $"<div id=s{i} style='content-visibility: auto; contain-intrinsic-size: 100pt'><p style='margin:0'>section {i} text</p><div style='height:390pt'></div></div>")));

    [Fact]
    public async Task TheViewportKeepsLookingAtTheSameThingWhenContentAboveItChangesSize()
    {
        var (context, document) = await TestDocument.LoadAsync(TextSections(), Screen());
        using var measuring = context.CreateCanvas(1, 1);

        // s0 and s1 are shown and the rest stand in at 100pt: the viewport goes to the top of s5. s0 and s1 are far away now and give their 405pt up, s4
        // comes near and takes 305pt more: the document above s5 is shorter, and the viewport went with it rather than being left over something else.
        var before = Top(document, "s5");
        document.SetViewport(new Rect(0, before, 400, 300));
        var result = await document.UpdateContentVisibilityAsync(measuring);

        Assert.True(result.Applied);
        Assert.True(Top(document, "s5") < before - 1);
        Assert.Equal(Top(document, "s5"), document.Viewport.Y, 3);
    }

    [Fact]
    public async Task AnElementWithSelectedTextStaysShownAndTheSelectionSurvivesTheLayoutsAround()
    {
        var (context, document) = await TestDocument.LoadAsync(TextSections(), ScreenWithRuns());
        using var measuring = context.CreateCanvas(1, 1);

        document.SetViewport(new Rect(0, Top(document, "s4"), 400, 300));
        await document.UpdateContentVisibilityAsync(measuring);

        var runs = document.TextRuns;
        Assert.True(runs.Any(r => r.Text.Contains("section 4", StringComparison.Ordinal)), string.Join(" | ", runs.Select(r => r.Text)) + $" ... s4 at {Top(document, "s4")}, viewport {document.Viewport}");
        var run = runs.Select((r, i) => (r, i)).First(x => x.r.Text.Contains("section 4", StringComparison.Ordinal));
        document.Selection.SetBaseAndExtent(new TextPosition(run.i, 0), new TextPosition(run.i, run.r.Text.Length));
        var selected = document.Selection.GetText();
        Assert.Contains("section 4", selected, StringComparison.Ordinal);

        // Back to the top: s4 is far away, but it holds what is selected, so its contents stay, and what is selected is still that text.
        document.SetViewport(new Rect(0, 0, 400, 300));
        var result = await document.UpdateContentVisibilityAsync(measuring);

        Assert.True(result.Applied);
        Assert.Equal(selected, document.Selection.GetText());
        Assert.Contains(document.TextRuns, r => r.Text.Contains("section 4", StringComparison.Ordinal));
        Assert.DoesNotContain(document.TextRuns, r => r.Text.Contains("section 3", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ASelectionIsCarriedOverWhenTheRunsBeforeItChange()
    {
        var (context, document) = await TestDocument.LoadAsync(TextSections(), ScreenWithRuns());
        using var measuring = context.CreateCanvas(1, 1);

        var run = document.TextRuns.Select((r, i) => (r, i)).First(x => x.r.Text.Contains("section 1", StringComparison.Ordinal));
        document.Selection.SetBaseAndExtent(new TextPosition(run.i, 0), new TextPosition(run.i, run.r.Text.Length));
        var selected = document.Selection.GetText();

        // s0 loses its contents, so the index of the run that follows moves; the selection is still the same text.
        document.SetViewport(new Rect(0, 600, 400, 300));
        await document.UpdateContentVisibilityAsync(measuring);

        Assert.Equal(selected, document.Selection.GetText());
    }
}
