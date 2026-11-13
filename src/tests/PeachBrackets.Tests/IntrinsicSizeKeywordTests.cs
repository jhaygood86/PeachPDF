using PeachBrackets;
using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Tests;

/// <summary>
/// <c>min-content</c>, <c>max-content</c> and <c>fit-content</c> as a <c>width</c> (css-sizing-3 §3): a block sized to its content instead of
/// filling its container. They are the same sizes as <c>calc-size(&lt;keyword&gt;, size)</c>.
/// </summary>
public class IntrinsicSizeKeywordTests
{
    // The right edge of a black bar whose text wraps at the width given: the bar is as wide as the box.
    private static async Task<int> RightEdgeAsync(string width, string extraStyle = "")
    {
        var context = await TestDocument.NewContextAsync();
        var options = new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = 300, ViewportHeightPoints = 60 };
        options.SetMargins(0);

        var body = "<div style='width:" + width + ";background:rgb(0,0,0);height:10px;" + extraStyle + "'>" +
                   "<span style='font-size:20px;color:white'>aaa bbb</span></div>";
        using var document = await HtmlDocument.LoadAsync(TestDocument.Wrap(body), context, options);
        using (var measuring = context.CreateCanvas(1, 1))
        {
            await document.LayoutAsync(measuring);
        }

        var canvas = context.CreateCanvas(400, 60);
        canvas.DrawRectangle(canvas.GetSolidBrush(PaintColor.FromArgb(255, 255, 255, 255)), 0, 0, 400, 60);
        await document.PaintPageAsync(0, canvas);

        var right = 0;
        for (var x = 0; x < 400; x++)
        {
            var (r, _, _, _) = TestDocument.Pixel(canvas, x, 2);
            if (r < 128) right = x;
        }

        return right;
    }

    [Fact]
    public async Task MaxContent_IsAsWideAsTheUnwrappedText_NotTheContainer()
    {
        var auto = await RightEdgeAsync("auto");
        var maxContent = await RightEdgeAsync("max-content");

        Assert.True(auto > 250, "auto fills the 300px page");
        Assert.InRange(maxContent, 20, 150);
    }

    [Fact]
    public async Task MinContent_IsTheWidestWord_NarrowerThanMaxContent()
    {
        Assert.True(await RightEdgeAsync("min-content") < await RightEdgeAsync("max-content"));
    }

    [Fact]
    public async Task FitContent_IsMaxContent_WhenThereIsRoom()
    {
        Assert.Equal(await RightEdgeAsync("max-content"), await RightEdgeAsync("fit-content"));
    }

    [Fact]
    public async Task TheKeyword_IsTheSameSizeAsItsCalcSize()
    {
        Assert.Equal(await RightEdgeAsync("calc-size(max-content, size)"), await RightEdgeAsync("max-content"));
        Assert.Equal(await RightEdgeAsync("calc-size(min-content, size)"), await RightEdgeAsync("min-content"));
    }

    [Fact]
    public async Task AMaxWidthKeyword_CapsAnAutoWidth()
    {
        Assert.Equal(await RightEdgeAsync("max-content"), await RightEdgeAsync("auto", "max-width:max-content"));
    }
}
