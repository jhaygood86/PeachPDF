using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Rendering;

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
}
