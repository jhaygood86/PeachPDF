using PeachDrawing;

namespace PeachBrackets.Tests;

public class FormRestyleTests
{
    private const string Rule = "input:checked + label { background: rgb(255,0,0) }";
    private const string Page = "<input id=a type=checkbox><label id=l for=a>on</label><input id=r1 type=radio name=p checked><label id=l1>one</label><input id=r2 type=radio name=p><label id=l2>two</label><input id=t><p>x</p>";

    private static async Task<(RasterRenderContext Context, HtmlDocument Document, RasterCanvas Measuring)> LoadAsync(string rule = Rule)
    {
        var (context, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap(Page, "<style>@page { margin: 0 } label { display: block; width: 30pt; height: 10pt } " + rule + "</style>"), new HtmlDocumentOptions { Media = "screen" });
        return (context, document, context.CreateCanvas(1, 1));
    }

    private static bool IsRed((byte R, byte G, byte B, byte A) p) => p is { R: > 200, G: < 60, B: < 60 };

    private static bool LabelIsRed(RasterCanvas canvas, HtmlDocument document, string id)
    {
        var b = document.GetElementBounds(id)!.Value;
        return IsRed(TestDocument.Pixel(canvas, (int)(b.X + (b.Width / 2)), (int)(b.Y + (b.Height / 2))));
    }

    [Fact]
    public async Task ACheckedBoxRestylesTheLabelThatFollowsItOnceTheHostAsks()
    {
        var (context, document, measuring) = await LoadAsync();
        Assert.False(document.NeedsRestyle);
        Assert.False(LabelIsRed(await TestDocument.PaintAsync(context, document), document, "l"));

        document.SetChecked(document.GetElement("a")!, true);

        Assert.True(document.NeedsRestyle);
        Assert.False(LabelIsRed(await TestDocument.PaintAsync(context, document), document, "l"), "nothing is restyled until it is asked for");

        var result = await document.RestyleFormAsync(measuring);

        Assert.True(result.Applied);
        Assert.NotEmpty(result.DirtyRegions);
        Assert.False(document.NeedsRestyle);
        Assert.True(LabelIsRed(await TestDocument.PaintAsync(context, document), document, "l"));
        Assert.True(document.GetFormState(document.GetElement("a")!)!.Value.Checked);

        document.SetChecked(document.GetElement("a")!, false);
        await document.RestyleFormAsync(measuring);
        Assert.False(LabelIsRed(await TestDocument.PaintAsync(context, document), document, "l"));
    }

    [Fact]
    public async Task CheckingARadioMovesTheLookFromTheOldOneToTheNewOne()
    {
        var (context, document, measuring) = await LoadAsync();
        Assert.True(LabelIsRed(await TestDocument.PaintAsync(context, document), document, "l1"));

        document.Activate(document.GetElement("r2")!);
        await document.RestyleFormAsync(measuring);

        var canvas = await TestDocument.PaintAsync(context, document);
        Assert.False(LabelIsRed(canvas, document, "l1"));
        Assert.True(LabelIsRed(canvas, document, "l2"));
    }

    [Fact]
    public async Task TypingIntoAFieldIsNotAStateAChangeOfStyleReads()
    {
        var (_, document, measuring) = await LoadAsync();
        document.SetValue(document.GetElement("t")!, "hello");

        Assert.False(document.NeedsRestyle);
        var result = await document.RestyleFormAsync(measuring);
        Assert.False(result.Applied);
    }

    [Fact]
    public async Task WithNoRuleThatReadsFormStateNothingNeedsRestyling()
    {
        var (_, document, measuring) = await LoadAsync("input { margin: 0 }");

        document.SetChecked(document.GetElement("a")!, true);

        Assert.False(document.NeedsRestyle);
        Assert.False((await document.RestyleFormAsync(measuring)).Applied);
    }

    [Fact]
    public async Task ARestyleForAnotherReasonAlsoSatisfiesTheWaitingChange()
    {
        var (context, document, measuring) = await LoadAsync(Rule + " input:focus { outline: 1px solid blue }");
        document.SetChecked(document.GetElement("a")!, true);
        Assert.True(document.NeedsRestyle);

        // A focus rule loads the source again, with the checked box as it now is.
        await document.SetFocusAsync(document.GetElement("t"), true, measuring);

        Assert.False(document.NeedsRestyle);
        Assert.True(LabelIsRed(await TestDocument.PaintAsync(context, document), document, "l"));
    }

    [Fact]
    public async Task ADisabledRuleFollowsToo()
    {
        var (context, document, measuring) = await LoadAsync("input:required + label { background: rgb(255,0,0) }");
        Assert.False(document.NeedsRestyle);

        // :required is read, but nothing a host can set changes it; a check box is not required, so the label stays plain.
        document.SetChecked(document.GetElement("a")!, true);

        Assert.True(document.NeedsRestyle);
        await document.RestyleFormAsync(measuring);
        Assert.False(LabelIsRed(await TestDocument.PaintAsync(context, document), document, "l"));
    }

    [Fact]
    public async Task RestylingWithoutALayoutOrANullCanvasIsRefused()
    {
        var (_, document, _) = await LoadAsync();

        await Assert.ThrowsAsync<ArgumentNullException>(() => document.RestyleFormAsync(null!));
    }
}
