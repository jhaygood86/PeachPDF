using PeachDrawing;

namespace PeachBrackets.Tests;

public class FormStateSelectorTests
{
    private const string Styles = """
        <style>
        @page { margin: 0 }
        input, select, textarea, p, fieldset, legend { position: absolute; margin: 0; padding: 0; border: 0; width: 20pt; height: 20pt; background: rgb(255,255,255) }
        :enabled { background: rgb(0,255,0) !important }
        :checked { background: rgb(255,0,0) !important }
        :disabled { background: rgb(0,0,255) !important }
        :required { border-left: 5pt solid rgb(255,0,255) }
        :optional { border-top: 5pt solid rgb(0,255,255) }
        :read-write { outline: 0; box-shadow: inset 0 -6pt 0 rgb(255,255,0) }
        </style>
        """;

    private static async Task<(RasterCanvas Canvas, HtmlDocument Document)> PaintAsync(string body)
    {
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap(body, Styles), new HtmlDocumentOptions { MediaType = MediaType.Screen });
        return (await TestDocument.PaintAsync(context, document), document);
    }

    // The color in the middle of an element.
    private static (byte R, byte G, byte B, byte A) Middle((RasterCanvas Canvas, HtmlDocument Document) page, string id)
    {
        var bounds = page.Document.GetElementBounds(id)!.Value;
        return TestDocument.Pixel(page.Canvas, (int)(bounds.X + (bounds.Width / 2)), (int)(bounds.Y + (bounds.Height / 2)));
    }

    private static bool IsRed((byte R, byte G, byte B, byte A) p) => p is { R: > 200, G: < 60, B: < 60 };

    private static bool IsBlue((byte R, byte G, byte B, byte A) p) => p is { R: < 60, G: < 60, B: > 200 };

    private static bool IsGreen((byte R, byte G, byte B, byte A) p) => p is { R: < 60, G: > 200, B: < 60 };

    [Fact]
    public async Task CheckedMatchesACheckedBoxOrRadioAndASelectedOptionButNothingElse()
    {
        var page = await PaintAsync(
            "<input id=a type=checkbox checked style='left:0;top:0'><input id=b type=checkbox style='left:30pt;top:0'><input id=c type=radio checked style='left:60pt;top:0'>"
            + "<input id=d type=text checked style='left:90pt;top:0'><p id=e checked style='left:120pt;top:0'>x</p>");

        Assert.True(IsRed(Middle(page, "a")));
        Assert.False(IsRed(Middle(page, "b")));
        Assert.True(IsRed(Middle(page, "c")));
        Assert.False(IsRed(Middle(page, "d")));
        Assert.False(IsRed(Middle(page, "e")));
    }

    [Fact]
    public async Task ADisabledControlMatchesDisabledAndAnEnabledOneEnabled()
    {
        var page = await PaintAsync(
            "<input id=a disabled style='left:0;top:0'><input id=b style='left:30pt;top:0'><p id=c disabled style='left:60pt;top:0'>x</p>"
            + "<select id=d disabled style='left:90pt;top:0'></select><select id=e style='left:120pt;top:0'></select>");

        Assert.True(IsBlue(Middle(page, "a")));
        Assert.True(IsGreen(Middle(page, "b")));
        // A paragraph is neither: it is not a form control.
        Assert.False(IsBlue(Middle(page, "c")) || IsGreen(Middle(page, "c")));
        Assert.True(IsBlue(Middle(page, "d")));
        Assert.True(IsGreen(Middle(page, "e")));
    }

    [Fact]
    public async Task AControlInADisabledFieldsetIsDisabledExceptInItsFirstLegend()
    {
        var page = await PaintAsync(
            "<fieldset disabled style='left:0;top:0;width:200pt;height:60pt'>"
            + "<legend style='position:static;display:block'><input id=in-legend style='position:static'></legend><input id=inside style='position:static'></fieldset>"
            + "<fieldset style='left:0;top:80pt;width:100pt'><input id=outside style='position:static'></fieldset>");

        Assert.False(IsBlue(Middle(page, "in-legend")));
        Assert.True(IsBlue(Middle(page, "inside")));
        Assert.True(IsGreen(Middle(page, "outside")));
    }

    [Fact]
    public async Task RequiredAndOptionalApplyToTheControlsThatCanBeRequired()
    {
        var page = await PaintAsync(
            "<input id=a required style='left:0;top:0'><input id=b style='left:30pt;top:0'><input id=c type=hidden required><input id=d type=submit style='left:60pt;top:0'>");

        // The required one has a magenta left border and no cyan top border; the optional one the reverse.
        var a = page.Document.GetElementBounds("a")!.Value;
        var b = page.Document.GetElementBounds("b")!.Value;
        Assert.True(TestDocument.Pixel(page.Canvas, (int)a.X + 2, (int)a.Y + 10) is { R: > 200, G: < 60, B: > 200 });
        Assert.False(TestDocument.Pixel(page.Canvas, (int)a.X + 10, (int)a.Y + 2) is { R: < 60, G: > 200, B: > 200 });
        Assert.True(TestDocument.Pixel(page.Canvas, (int)b.X + 10, (int)b.Y + 2) is { R: < 60, G: > 200, B: > 200 });
        var submit = page.Document.GetElementBounds("d")!.Value;
        Assert.False(TestDocument.Pixel(page.Canvas, (int)submit.X + 2, (int)submit.Y + 10) is { R: > 200, G: < 60, B: > 200 });
        Assert.False(TestDocument.Pixel(page.Canvas, (int)submit.X + 10, (int)submit.Y + 2) is { R: < 60, G: > 200, B: > 200 });
    }

    [Fact]
    public async Task ATextFieldIsReadWriteUnlessReadOnlyOrDisabledAndEverythingElseIsReadOnly()
    {
        var page = await PaintAsync(
            "<input id=a style='left:0;top:0'><input id=b readonly style='left:30pt;top:0'><input id=c disabled style='left:60pt;top:0'>"
            + "<input id=d type=checkbox style='left:90pt;top:0'><textarea id=e style='left:120pt;top:0'></textarea><p id=f contenteditable style='left:150pt;top:0'>x</p><p id=g style='left:180pt;top:0'>x</p>");

        bool Yellow(string id)
        {
            var bounds = page.Document.GetElementBounds(id)!.Value;
            return TestDocument.Pixel(page.Canvas, (int)bounds.X + 10, (int)(bounds.Y + bounds.Height) - 2) is { R: > 200, G: > 200, B: < 80 };
        }

        Assert.True(Yellow("a"));
        Assert.False(Yellow("b"));
        Assert.False(Yellow("c"));
        Assert.False(Yellow("d"));
        Assert.True(Yellow("e"));
        Assert.True(Yellow("f"));
        Assert.False(Yellow("g"));
    }

    [Fact]
    public async Task TheListsStillParseWhenOneNameIsNotAnswered()
    {
        // :valid has no answer yet and selects nothing, but the rest of the list must still apply.
        var page = await PaintAsync("<input id=a type=checkbox checked style='left:0;top:0'><style>input:valid, input:checked { outline: 0 }</style>");

        Assert.True(IsRed(Middle(page, "a")));
    }
}
