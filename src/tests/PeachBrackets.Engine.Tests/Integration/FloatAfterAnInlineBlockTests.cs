using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Rendering;
using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// A float placed after an inline-block on the same line (CSS 2.1 section 9.5: the line is shortened beside the float): the box moves beside a left float
/// if it fits there, and the float goes below the line if it does not (#2227). Expected values are what Chromium reports, in CSS pixels.
/// </summary>
public class FloatAfterAnInlineBlockTests
{
    private const double Px = 0.75;

    private static CssBox? Find(CssBox box, string id)
    {
        if (box.HtmlTag?.TryGetAttribute("id") == id) return box;

        foreach (var child in box.Boxes)
        {
            if (Find(child, id) is { } found) return found;
        }

        return null;
    }

    private static async Task<CssBox> LayOutAsync(string body)
    {
        var html = $"<!DOCTYPE html><html><head><style>body {{ margin: 0; font: 13px Arial }}</style></head><body>{body}</body></html>";
        var renderContext = new HtmlRenderContext();
        await BundledFonts.PinSansSerifAsync(renderContext);
        var container = new HtmlContainerInt(renderContext) { MediaType = MediaType.Screen };
        await container.SetHtml(html, null);

        var size = new Size(600, 842);
        container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
        container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
        using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
        await container.PerformLayout(graphics);
        return container.Root!;
    }

    private static string Page(string floatStyle) =>
        $"<div style='width:500px'><div id='ib' style='display:inline-block;width:300px;height:80px'></div><div id='f' style='{floatStyle}'></div></div>";

    [Fact]
    public async Task ALeftFloatThatFitsBesideTheBox_MovesTheBoxRight()
    {
        var root = await LayOutAsync(Page("float:left;width:100px;height:20px"));

        Assert.Equal(0, Find(root, "f")!.Location.X / Px, 0.5);
        Assert.Equal(0, Find(root, "f")!.Location.Y / Px, 0.5);
        Assert.Equal(100, Find(root, "ib")!.Location.X / Px, 0.5);
        Assert.Equal(0, Find(root, "ib")!.Location.Y / Px, 0.5);
    }

    [Fact]
    public async Task ALeftFloatThatDoesNotFitBesideTheBox_GoesBelowTheLine()
    {
        var root = await LayOutAsync(Page("float:left;width:250px;height:20px"));

        Assert.Equal(0, Find(root, "ib")!.Location.X / Px, 0.5);
        Assert.Equal(0, Find(root, "f")!.Location.X / Px, 0.5);
        Assert.Equal(83, Find(root, "f")!.Location.Y / Px, 0.5);
    }

    [Fact]
    public async Task ARightFloatBesideTheBox_LeavesItAlone()
    {
        var root = await LayOutAsync(Page("float:right;width:100px;height:20px"));

        Assert.Equal(0, Find(root, "ib")!.Location.X / Px, 0.5);
        Assert.Equal(400, Find(root, "f")!.Location.X / Px, 0.5);
        Assert.Equal(0, Find(root, "f")!.Location.Y / Px, 0.5);
    }

    // A float that does not fit beside the words goes below the line, which goes on after it: the line's final height decides where, so a taller box
    // after the float (a larger font) must not leave the float's top inside the line it was meant to be under.
    [Fact]
    public async Task ADeferredFloat_IsBelowTheWholeLine_EvenWhenALaterWordMakesItTaller()
    {
        var root = await LayOutAsync("<div style='width:300px'>aaa bbb ccc <div id='f' style='float:left;width:250px;height:20px'></div><span style='font-size:40px'>big</span></div>");

        var floated = Find(root, "f")!;
        var words = AllWords(root).Where(w => w.Text?.Trim() is "aaa" or "big").ToList();

        Assert.Equal(2, words.Count);
        Assert.All(words, w => Assert.True(w.Bottom <= floated.Location.Y + 0.5, $"{w.Text!.Trim()} ends at {w.Bottom / Px:0.#}px, the float starts at {floated.Location.Y / Px:0.#}px"));
    }

    private static IEnumerable<CssRect> AllWords(CssBox box) => box.Words.Concat(box.Boxes.SelectMany(AllWords));

    // An inline-block moved beside a float takes what is inside it along: the rectangle a styled span in it paints its background in is where its text is.
    [Fact]
    public async Task ASpanInAnInlineBlockMovedBesideAFloat_KeepsItsRectangleOnItsText()
    {
        var root = await LayOutAsync("<div id='d' style='width:300px'><span id='ib' style='display:inline-block'>aaa <b id='b' style='background:#fa0'>bbb</b></span><div style='float:left;width:60px;height:20px'></div></div>");

        var block = Find(root, "d")!;
        var span = Find(root, "b")!;
        var word = AllWords(span).First(w => w.Text?.Trim() == "bbb");
        var line = block.LineBoxes.First(l => l.Rectangles.ContainsKey(span));

        Assert.Equal(word.Left, line.Rectangles[span].X, 0.5);
        Assert.True(word.Left >= 60 * Px - 0.5, $"the text is at {word.Left / Px:0.#}px, beside the 60px float");
    }
}
