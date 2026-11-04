using PeachBrackets.Accessibility;

namespace PeachBrackets.Tests;

public class TextAreaControlTests
{
    private static async Task<HtmlDocument> LoadAsync(string body)
    {
        var (_, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap(body, "<style>@page { margin: 0 } textarea { display: block; margin: 0 }</style>"), new HtmlDocumentOptions { MediaType = MediaType.Screen });
        return document;
    }

    [Fact]
    public async Task ATextAreaHoldsItsTextAsAValueAndDropsTheFirstLineBreak()
    {
        var document = await LoadAsync("<textarea id=t>\nFirst line\nSecond line</textarea>");

        var state = document.GetFormState(document.GetElement("t")!);

        Assert.Equal("textarea", state!.Value.Type);
        Assert.Equal("First line\nSecond line", state.Value.Value);
    }

    [Fact]
    public async Task ATextAreaIsATextBoxWhoseSizeComesFromColsAndRows()
    {
        var document = await LoadAsync("<textarea id=small cols=10 rows=2></textarea><textarea id=big cols=40 rows=6></textarea>");
        var tree = document.GetAccessibilityTree();

        var small = tree.Nodes.First(n => n.ElementId == "small");
        var big = tree.Nodes.First(n => n.ElementId == "big");

        Assert.Equal(AccessibilityRole.TextBox, small.Role);
        Assert.True(big.Bounds[0].Bounds.Width > small.Bounds[0].Bounds.Width * 3);
        Assert.True(big.Bounds[0].Bounds.Height > small.Bounds[0].Bounds.Height * 2);
    }

    [Fact]
    public async Task ATextAreaIsAMultilineTextFieldForTheAnnotations()
    {
        var (_, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap("<textarea id=t name=notes>one\ntwo</textarea>", "<style>@page { margin: 0 }</style>"), new HtmlDocumentOptions { CollectFormFields = true });

        var field = document.Annotations.FormFields.Single();

        Assert.True(field.Classification.Multiline);
        Assert.Equal("notes", field.Classification.Name);
        Assert.Equal("one\ntwo", field.Classification.Value);
    }

    [Fact]
    public async Task ATextAreaIsNotFlowedAsContent()
    {
        var document = await LoadAsync("<textarea id=t cols=10 rows=2>this is a long text that would otherwise flow and make the box taller than two lines</textarea>");

        var node = document.GetAccessibilityTree().Nodes.First(n => n.ElementId == "t");

        Assert.True(node.Bounds[0].Bounds.Height < 100);
    }

    private static async Task<PeachDrawing.RasterCanvas> PaintAsync(string body)
    {
        var (context, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap(body, "<style>@page { margin: 0 } textarea { position: absolute; margin: 0; left: 10pt; top: 10pt; width: 200pt; height: 80pt; font-size: 20pt; color: rgb(0,0,0) }</style>"),
            new HtmlDocumentOptions { PaintFormControls = true, MediaType = MediaType.Screen });
        return await TestDocument.PaintAsync(context, document);
    }

    private static int Ink(PeachDrawing.RasterCanvas canvas, int x, int y, int width, int height)
    {
        var count = 0;
        for (var j = y; j < y + height; j++)
        {
            for (var i = x; i < x + width; i++)
            {
                var p = TestDocument.Pixel(canvas, i, j);
                if (p.R < 100 && p.G < 100 && p.B < 100)
                    count++;
            }
        }

        return count;
    }

    [Fact]
    public async Task ATextAreaDrawsEachLineOfItsValueBelowTheLast()
    {
        var canvas = await PaintAsync("<textarea>HHHH\nHHHH\nHHHH</textarea>");

        // 20pt text: three lines of about 24pt each inside the 80pt box.
        Assert.True(Ink(canvas, 14, 14, 150, 24) > 50);
        Assert.True(Ink(canvas, 14, 40, 150, 24) > 50);
        Assert.True(Ink(canvas, 14, 64, 150, 24) > 20);
    }

    [Fact]
    public async Task ALongLineWrapsInsideTheTextArea()
    {
        var canvas = await PaintAsync("<textarea>HHHH HHHH HHHH HHHH HHHH HHHH</textarea>");

        Assert.True(Ink(canvas, 14, 14, 190, 24) > 50);
        Assert.True(Ink(canvas, 14, 40, 190, 24) > 50);
    }

    [Fact]
    public async Task AnEmptyTextAreaShowsItsPlaceholder()
    {
        var shown = await PaintAsync("<style>textarea::placeholder { color: rgb(0,0,0) }</style><textarea placeholder=HHHH></textarea>");
        var none = await PaintAsync("<textarea></textarea>");

        Assert.True(Ink(shown, 14, 14, 190, 70) > 50);
        Assert.Equal(0, Ink(none, 14, 14, 190, 70));
    }
}
