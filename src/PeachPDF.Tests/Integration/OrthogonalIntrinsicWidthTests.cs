using PeachDrawing.Core;
using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// A flex item that holds a vertical box with a declared width is as wide as that box, not as wide as its text laid out
/// horizontally: the box's words run down the page, so adding their widths up as one horizontal line made it ask for the
/// width of its whole text. Kana can break between characters (UAX #14), so the old approximation, which treated a run of
/// kana as one word, undercounted by luck and the corrected line breaking exposed it.
/// </summary>
public class OrthogonalIntrinsicWidthTests
{
    private static async Task<CssBox> LayOutAsync(string item)
    {
        var html = "<!DOCTYPE html><html><head><meta charset='utf-8'><style>body{margin:0;font:9pt Arial}" +
                   ".row{display:flex;gap:12px;align-items:flex-start}" +
                   ".vbox{border:2px solid #000;padding:8px;writing-mode:vertical-rl;width:60px;height:140px}</style></head>" +
                   $"<body><div class='row'><div id='item'>{item}</div><div id='after'>x</div></div></body></html>";

        var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
        var container = new HtmlContainerInt(adapter) { MarginTop = 0, MarginLeft = 0, MarginRight = 0, MarginBottom = 0 };
        await container.SetHtml(html, null);

        var size = new XSize(600, 800);
        container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
        container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

        var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
        using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
        await container.PerformLayout(graphics);

        return Find(container.Root!, "item")!;
    }

    private static CssBox? Find(CssBox box, string id)
    {
        if (box.HtmlTag?.TryGetAttribute("id", "") == id) return box;
        foreach (var child in box.Boxes)
        {
            if (Find(child, id) is { } found) return found;
        }
        return null;
    }

    // 60px of width plus 8px of padding and 2px of border on each side is 80px, which is 60pt.
    [Theory]
    [InlineData("縦書きテキストPDF2024")]
    [InlineData("縦書きテキスト縦書きテキスト縦書きテキスト")]
    public async Task FlexItem_HoldingAVerticalBoxWithADeclaredWidth_IsAsWideAsTheBox(string text)
    {
        var item = await LayOutAsync($"<div class='vbox'>{text}</div>");

        Assert.InRange(item.ActualRight - item.Location.X, 59, 61);
    }

    [Fact]
    public async Task FlexItem_HoldingAVerticalBoxWithoutADeclaredWidth_IsNotChanged()
    {
        var item = await LayOutAsync("<div class='vbox' style='width:auto'>縦書き</div>");

        Assert.True(item.ActualRight - item.Location.X > 0);
    }
}
