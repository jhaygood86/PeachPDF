using PeachBrackets;
using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Tests;

/// <summary>
/// What quirks mode changes, through the public API only: each case is the same markup with and without a DOCTYPE, laid out by
/// <see cref="HtmlDocument"/> and read back through <see cref="HtmlDocument.GetElementBounds"/> or a painted pixel. (The engine's own tests assert on the
/// box tree; these are the ones that go through what a host calls.)
/// </summary>
public class QuirksModeBehaviorTests
{
    private const string Doctype = "<!DOCTYPE html>";

    private static HtmlDocumentOptions Options() => new()
    {
        MediaType = MediaType.Screen,
        PageWidthPoints = 300,
        ViewportHeightPoints = 200,
    };

    private static async Task<HtmlDocument> LoadWithStyleAsync(string css, string html, bool doctype)
    {
        var context = await TestDocument.NewContextAsync();
        var options = Options();
        options.SetMargins(0);

        var document = await HtmlDocument.LoadAsync(
            (doctype ? Doctype : "") + $"<html><head><style>body{{margin:0;font-size:12pt}} {css}</style></head><body>{html}</body></html>", context, options);
        using var measuring = context.CreateCanvas(1, 1);
        await document.LayoutAsync(measuring);
        return document;
    }

    private static Rect Bounds(HtmlDocument document, string id) => document.GetElementBounds(id)!.Value;

    [Fact]
    public async Task AHexColorMayLackItsHashInQuirksMode()
    {
        var context = await TestDocument.NewContextAsync();
        context.PixelsPerPoint = 96.0 / 72.0;

        async Task<(byte R, byte G, byte B)> PaintedAsync(bool doctype)
        {
            var options = Options();
            options.PageWidthPoints = 60 * 0.75;
            options.ViewportHeightPoints = 40 * 0.75;
            options.SetMargins(0);
            using var document = await HtmlDocument.LoadAsync(
                (doctype ? Doctype : "") + "<html><body style='margin:0'><div style='width:40px;height:30px;background-color:ff0000'></div></body></html>", context, options);
            using (var measuring = context.CreateCanvas(1, 1)) await document.LayoutAsync(measuring);

            var canvas = context.CreateCanvas(60, 40);
            canvas.DrawRectangle(canvas.GetSolidBrush(PaintColor.FromArgb(255, 255, 255, 255)), 0, 0, 60, 40);
            await document.PaintPageAsync(0, canvas);
            var (r, g, b, _) = TestDocument.Pixel(canvas, 10, 10);
            return (r, g, b);
        }

        Assert.Equal(((byte)255, (byte)0, (byte)0), await PaintedAsync(false));
        Assert.Equal(((byte)255, (byte)255, (byte)255), await PaintedAsync(true));
    }

    [Fact]
    public async Task ALengthMayLackItsUnitInQuirksMode()
    {
        using var quirks = await LoadWithStyleAsync("#d{width:100;height:10}", "<div id='d'>x</div>", doctype: false);
        using var standards = await LoadWithStyleAsync("#d{width:100;height:10}", "<div id='d'>x</div>", doctype: true);

        Assert.Equal(75.0, Bounds(quirks, "d").Width, 0.01);
        Assert.True(Bounds(standards, "d").Width > 200);
    }

    [Fact]
    public async Task ABlockInTheBodyHasItsPercentageHeightAgainstThePageInQuirksMode()
    {
        using var quirks = await LoadWithStyleAsync("", "<div id='d' style='height:50%'>x</div>", doctype: false);
        using var standards = await LoadWithStyleAsync("", "<div id='d' style='height:50%'>x</div>", doctype: true);

        Assert.Equal(100.0, Bounds(quirks, "d").Height, 1.0);
        Assert.True(Bounds(standards, "d").Height < 40);
    }

    [Fact]
    public async Task APercentageHeightInsideATableCellIsNotGivenTheQuirk()
    {
        using var document = await LoadWithStyleAsync("",
            "<div style='display:table-cell;height:100px;background:green'><div style='width:100px'><div id='d' style='height:100%;background:red'></div></div></div>", doctype: false);

        Assert.Equal(0.0, Bounds(document, "d").Height, 0.01);
    }

    [Fact]
    public async Task ImagesInAnAutoWidthCellDoNotWrapApartInQuirksMode()
    {
        const string table = "<table style='width:1px'><tr><td id='c'><img width='30' height='5'> <img width='30' height='5'> <img width='30' height='5'></td></tr></table>";
        using var quirks = await LoadWithStyleAsync("", table, doctype: false);
        using var standards = await LoadWithStyleAsync("", table, doctype: true);

        Assert.True(Bounds(quirks, "c").Width >= 67.5);
        Assert.True(Bounds(standards, "c").Width < 45);
    }

    [Fact]
    public async Task ANowrapCellIsAsWideAsItsWidthInQuirksMode()
    {
        using var quirks = await LoadWithStyleAsync("", "<table><tr><td id='c' nowrap style='width:150px'>a</td><td>b</td></tr></table>", doctype: false);

        Assert.True(Bounds(quirks, "c").Width >= 112.5);
    }

    [Fact]
    public async Task ANowrapCellWithAWidthAttributeWrapsInQuirksModeOnly()
    {
        const string table = "<table><tr><td id='c' nowrap width='40'>aaa bbb ccc ddd eee fff</td></tr></table>";
        using var quirks = await LoadWithStyleAsync("", table, doctype: false);
        using var standards = await LoadWithStyleAsync("", table, doctype: true);

        Assert.True(Bounds(quirks, "c").Height > Bounds(standards, "c").Height * 1.5);
    }

    [Fact]
    public async Task AFirstLineRuleReadsAnInlineStyleWithTheQuirksOfTheDocument()
    {
        const string css = "#d::first-line{color:red}";
        const string div = "<div id='d' style='font-size:40;width:200'>first line of text</div>";
        using var quirks = await LoadWithStyleAsync(css, div, doctype: false);
        using var standards = await LoadWithStyleAsync(css, div, doctype: true);

        Assert.True(Bounds(quirks, "d").Height > Bounds(standards, "d").Height);
    }

    [Fact]
    public async Task ABlockquoteWithTextAndABlockInsideItIsNotBlank()
    {
        // The anonymous box that wraps the text beside the block is looked through when the standard's "blank" is asked.
        using var document = await LoadWithStyleAsync("", "<blockquote id='q'>text<div>d</div></blockquote><p id='p'>after</p>", doctype: false);

        Assert.Equal(0.0, Bounds(document, "q").Y, 0.01);
        Assert.True(Bounds(document, "p").Y > Bounds(document, "q").Y);
    }

    [Theory]
    [InlineData("\n \t<!DOCTYPE html>", DocumentMode.NoQuirks)]
    [InlineData("﻿<!DOCTYPE html>", DocumentMode.NoQuirks)]
    [InlineData(" <!DOCTYPE html>", DocumentMode.Quirks)]
    [InlineData("x<!DOCTYPE html>", DocumentMode.Quirks)]
    [InlineData("<!-- c -->\r\n<!DOCTYPE html>", DocumentMode.NoQuirks)]
    public async Task OnlyHtmlWhiteSpaceMayComeBeforeTheDoctype(string lead, DocumentMode expected)
    {
        var context = await TestDocument.NewContextAsync();
        using var document = await HtmlDocument.LoadAsync(lead + "<html><body>x</body></html>", context, Options());

        Assert.Equal(expected, document.DocumentMode);
    }

    private static async Task<double> HoveredLeftAsync(string selector, bool doctype)
    {
        using var document = await LoadWithStyleAsync($"{selector}{{margin-left:30px}}", "<div id='d' class='x' style='width:100px;height:40px'>x</div><a id='a' href='#'>link</a>", doctype);
        using var canvas = (await TestDocument.NewContextAsync()).CreateCanvas(1, 1);
        var bounds = Bounds(document, "d");

        await document.SetPointerStateAsync(0, bounds.X + 5, bounds.Y + 5, pressed: selector.Contains("active"), canvas);
        return Bounds(document, "d").X;
    }

    [Theory]
    [InlineData(":hover", false)]
    [InlineData(":hover:active", false)]
    [InlineData("div:hover", true)]
    [InlineData(".x:hover", true)]
    public async Task ABareHoverMatchesOnlyLinksInQuirksMode(string selector, bool movesDiv)
    {
        Assert.Equal(movesDiv ? 22.5 : 0.0, await HoveredLeftAsync(selector, doctype: false), 0.5);
        Assert.True(await HoveredLeftAsync(selector, doctype: true) > 20, "in standards mode every one of them matches");
    }

    [Fact]
    public async Task ABareHoverStillMatchesALinkInQuirksMode()
    {
        using var document = await LoadWithStyleAsync(":hover{margin-left:30px}", "<a id='a' href='#'>link</a>", doctype: false);
        using var canvas = (await TestDocument.NewContextAsync()).CreateCanvas(1, 1);
        var bounds = Bounds(document, "a");

        await document.SetPointerStateAsync(0, bounds.X + 2, bounds.Y + 2, false, canvas);

        Assert.True(Bounds(document, "a").X > 20);
    }
}
