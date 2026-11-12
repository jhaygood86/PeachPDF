using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Tests;

public class FormControlPaintTests
{
    private const string Styles = "<style>@page { margin: 0 } input, select { position: absolute; margin: 0 } </style>";

    private static async Task<RasterCanvas> PaintAsync(string body, bool paintControls)
    {
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap(body, Styles),
            new HtmlDocumentOptions { PaintFormControls = paintControls, MediaType = MediaType.Screen });
        return await TestDocument.PaintAsync(context, document);
    }

    private static int InkCount(RasterCanvas canvas, int x, int y, int width, int height, Func<(byte R, byte G, byte B, byte A), bool> match)
    {
        var count = 0;
        for (var j = y; j < y + height; j++)
        {
            for (var i = x; i < x + width; i++)
            {
                if (match(TestDocument.Pixel(canvas, i, j)))
                    count++;
            }
        }

        return count;
    }

    private static bool IsRed((byte R, byte G, byte B, byte A) p) => p.R > 200 && p.G < 60 && p.B < 60;

    private static bool IsDark((byte R, byte G, byte B, byte A) p) => p.R < 100 && p.G < 100 && p.B < 100;

    [Fact]
    public async Task ACheckedCheckBoxShowsItsCheckMarkInTheAccentColour_AnUncheckedOneDoesNot()
    {
        const string body = "<input type=checkbox checked style='left:10pt;top:10pt;width:40pt;height:40pt;accent-color:red'>"
            + "<input type=checkbox style='left:100pt;top:10pt;width:40pt;height:40pt;accent-color:red'>";
        var canvas = await PaintAsync(body, paintControls: true);

        Assert.True(InkCount(canvas, 10, 10, 40, 40, IsRed) > 20);
        Assert.Equal(0, InkCount(canvas, 100, 10, 40, 40, IsRed));
    }

    [Fact]
    public async Task AStaticPageShowsValuesCheckedStateAndSelectedOptionToo()
    {
        const string body = "<input value='HHHH' style='left:10pt;top:10pt;width:150pt;height:30pt;font-size:20pt;color:rgb(0,0,0)'>"
            + "<input type=checkbox checked style='left:10pt;top:60pt;width:40pt;height:40pt;accent-color:red'>"
            + "<input type=checkbox style='left:100pt;top:60pt;width:40pt;height:40pt;accent-color:red'>"
            + "<select style='left:10pt;top:110pt;width:150pt;height:30pt;font-size:20pt'><option>HHHH<option selected>IIII</select>";
        var canvas = await PaintAsync(body, paintControls: false);

        Assert.True(InkCount(canvas, 14, 14, 140, 22, IsDark) > 40, "the value is drawn");
        Assert.True(InkCount(canvas, 10, 60, 40, 40, IsRed) > 20, "the check mark is drawn");
        Assert.Equal(0, InkCount(canvas, 100, 60, 40, 40, IsRed));
        Assert.True(InkCount(canvas, 14, 114, 100, 22, IsDark) > 10, "the chosen option is drawn");
    }

    [Fact]
    public async Task ACheckedRadioShowsItsDot()
    {
        const string body = "<input type=radio checked style='left:10pt;top:10pt;width:40pt;height:40pt;accent-color:red'>";
        var canvas = await PaintAsync(body, paintControls: true);

        Assert.True(InkCount(canvas, 10, 10, 40, 40, IsRed) > 20);
    }

    [Fact]
    public async Task ATextFieldShowsItsValue_AndAnEmptyOneItsPlaceholder()
    {
        const string body = "<input value='HHHH' style='left:10pt;top:10pt;width:150pt;height:30pt;font-size:20pt;color:rgb(0,0,0)'>"
            + "<input placeholder='HHHH' style='left:10pt;top:60pt;width:150pt;height:30pt;font-size:20pt'>"
            + "<input style='left:10pt;top:110pt;width:150pt;height:30pt;font-size:20pt'>";
        var canvas = await PaintAsync(body, paintControls: true);

        // The border is dark too, so look inside it.
        Assert.True(InkCount(canvas, 14, 14, 140, 22, IsDark) > 40, "the value is drawn");
        Assert.True(InkCount(canvas, 14, 64, 140, 22, p => p.R < 200 && p.A > 0) > 40, "the placeholder is drawn");
        Assert.Equal(0, InkCount(canvas, 14, 114, 140, 22, p => p.R < 200));
    }

    [Fact]
    public async Task AButtonShowsItsLabelAndASubmitButtonItsDefault()
    {
        const string body = "<input type=button value='HHHH' style='left:10pt;top:10pt;width:150pt;height:30pt;font-size:20pt'>"
            + "<input type=submit style='left:10pt;top:60pt;width:150pt;height:30pt;font-size:20pt'>";
        var canvas = await PaintAsync(body, paintControls: true);

        Assert.True(InkCount(canvas, 14, 14, 140, 22, IsDark) > 40);
        Assert.True(InkCount(canvas, 14, 64, 140, 22, IsDark) > 40);
    }

    [Fact]
    public async Task ASelectShowsTheChosenOptionAndAnArrow()
    {
        const string body = "<select style='left:10pt;top:10pt;width:150pt;height:30pt;font-size:20pt'><option>HHHH<option selected>IIII</select>";
        var canvas = await PaintAsync(body, paintControls: true);

        Assert.True(InkCount(canvas, 14, 14, 100, 22, IsDark) > 10, "the label is drawn");
        Assert.True(InkCount(canvas, 125, 14, 30, 22, IsDark) > 5, "the arrow is drawn");
    }

    [Fact]
    public async Task ARangeShowsATrackAndAThumbWhereItsValueIs()
    {
        const string body = "<input type=range min=0 max=100 value=100 style='left:10pt;top:10pt;width:150pt;height:30pt;accent-color:red'>";
        var canvas = await PaintAsync(body, paintControls: true);

        // At 100 the thumb is at the right end and the track is filled to it.
        Assert.True(InkCount(canvas, 100, 10, 60, 30, IsRed) > 30);
        Assert.True(InkCount(canvas, 10, 10, 30, 30, IsRed) > 5);
    }

    [Fact]
    public async Task TheScreenLookWinsWhenFieldsAreAlsoCollected()
    {
        const string body = "<input value='HHHH' style='left:10pt;top:10pt;width:150pt;height:30pt;font-size:20pt;color:rgb(0,0,0)'>";
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap(body, Styles),
            new HtmlDocumentOptions { PaintFormControls = true, CollectFormFields = true, MediaType = MediaType.Screen });
        var canvas = await TestDocument.PaintAsync(context, document);

        Assert.True(InkCount(canvas, 14, 14, 140, 22, IsDark) > 40, "the value is drawn although the field is also collected");
    }

    [Fact]
    public async Task AColorInputShowsItsColorAsABlock()
    {
        const string body = "<input type=color value='#ff0000' style='left:10pt;top:10pt;width:60pt;height:30pt;padding:0'>"
            + "<input type=color value='#0f0' style='left:100pt;top:10pt;width:60pt;height:30pt;padding:0'>"
            + "<input type=color style='left:200pt;top:10pt;width:60pt;height:30pt;padding:0'>";
        var canvas = await PaintAsync(body, paintControls: true);

        Assert.True(InkCount(canvas, 12, 12, 56, 26, IsRed) > 1000);
        Assert.True(InkCount(canvas, 102, 12, 56, 26, p => p.G > 200 && p.R < 60 && p.B < 60) > 1000);
        Assert.True(InkCount(canvas, 202, 12, 56, 26, IsDark) > 1000, "no value is black");
    }

    [Fact]
    public async Task AFileInputShowsAChooseButtonAndWhatIsChosen()
    {
        const string body = "<input type=file style='left:10pt;top:10pt;width:250pt;height:30pt;font-size:14pt'>";
        var canvas = await PaintAsync(body, paintControls: true);

        Assert.True(InkCount(canvas, 12, 12, 100, 26, p => p is { R: > 220 and < 250, G: > 220 and < 250 }) > 500, "the button");
        Assert.True(InkCount(canvas, 120, 12, 130, 26, p => p.R < 200 && p.A > 0) > 40, "the words beside it");
    }

    [Fact]
    public async Task ADateFieldWithNoValueShowsItsShapeAndOneWithAValueShowsTheValue()
    {
        const string body = "<input type=date style='left:10pt;top:10pt;width:150pt;height:30pt;font-size:16pt'>"
            + "<input type=date value='2024-05-01' style='left:10pt;top:60pt;width:150pt;height:30pt;font-size:16pt;color:rgb(0,0,0)'>"
            + "<input type=time style='left:10pt;top:110pt;width:150pt;height:30pt;font-size:16pt'>";
        var canvas = await PaintAsync(body, paintControls: true);

        Assert.True(InkCount(canvas, 14, 14, 140, 22, p => p.R < 200 && p.A > 0) > 40, "the date pattern");
        Assert.True(InkCount(canvas, 14, 64, 140, 22, IsDark) > 40, "the value");
        Assert.True(InkCount(canvas, 14, 114, 140, 22, p => p.R < 200 && p.A > 0) > 20, "the time pattern");
    }
}
