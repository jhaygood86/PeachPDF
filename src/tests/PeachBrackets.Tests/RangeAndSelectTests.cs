namespace PeachBrackets.Tests;

public class RangeAndSelectTests
{
    private const string Head = "<style>@page { margin: 0 } input, select { display: block; margin: 0; position: absolute; left: 10pt; top: 10pt }</style>";

    private static async Task<HtmlDocument> LoadAsync(string body)
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap(body, Head), new HtmlDocumentOptions { Media = "screen", PaintFormControls = true });
        return document;
    }

    private static double Value(HtmlDocument document, string id) => double.Parse(document.GetFormState(document.GetElement(id)!)!.Value.Value, System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task APointOnTheTrackSetsTheRangeFromItsEndsToItsMiddle()
    {
        var document = await LoadAsync("<input id=r type=range min=0 max=100 style='width:200pt;height:20pt'>");
        var bounds = document.GetElementBounds("r")!.Value;
        var element = document.GetElement("r")!;

        Assert.True(document.SetRangeAtPoint(element, 0, bounds.X - 50).Applied);
        Assert.Equal(0, Value(document, "r"));
        document.SetRangeAtPoint(element, 0, bounds.X + bounds.Width + 50);
        Assert.Equal(100, Value(document, "r"));
        document.SetRangeAtPoint(element, 0, bounds.X + (bounds.Width / 2));
        Assert.InRange(Value(document, "r"), 48, 52);
    }

    [Fact]
    public async Task ARangeSnapsToItsStep_AndIgnoresTheStepWhenItIsAny()
    {
        var document = await LoadAsync("<input id=a type=range min=0 max=100 step=25 style='width:200pt;height:20pt'><input id=b type=range min=0 max=1 step=any style='width:200pt;height:20pt;top:60pt'>");
        var a = document.GetElementBounds("a")!.Value;
        var b = document.GetElementBounds("b")!.Value;

        document.SetRangeAtPoint(document.GetElement("a")!, 0, a.X + (a.Width * 0.3));
        document.SetRangeAtPoint(document.GetElement("b")!, 0, b.X + (b.Width * 0.3));

        Assert.Equal(0, Value(document, "a") % 25);
        Assert.NotEqual(0, Value(document, "b") * 1000 % 1);
    }

    [Fact]
    public async Task OnlyAnEnabledRangeOnItsPageTakesAPoint()
    {
        var document = await LoadAsync("<input id=r type=range disabled><input id=t value=x style='top:50pt'><select id=s><option>a</select>");

        Assert.False(document.SetRangeAtPoint(document.GetElement("r")!, 0, 50).Applied);
        Assert.False(document.SetRangeAtPoint(document.GetElement("t")!, 0, 50).Applied);
        Assert.False(document.SetRangeAtPoint(document.GetElement("s")!, 0, 50).Applied);
    }

    [Fact]
    public async Task ARangeOnAnotherPageIsNotSet()
    {
        var document = await LoadAsync("<input id=r type=range style='width:200pt;height:20pt'>");

        Assert.False(document.SetRangeAtPoint(document.GetElement("r")!, 3, 50).Applied);
    }

    [Fact]
    public async Task ADropDownListsItsOptionsAndAnythingElseHasNone()
    {
        var document = await LoadAsync("<select id=s><option value=1>One</option><option selected>Two</option><option disabled value=3>Three</option></select><input id=i>");

        var options = document.GetSelectOptions(document.GetElement("s")!);

        Assert.Equal(["1", "Two", "3"], options.Select(o => o.Value));
        Assert.Equal(["One", "Two", "Three"], options.Select(o => o.Label));
        Assert.Equal([false, true, false], options.Select(o => o.Selected));
        Assert.Equal([false, false, true], options.Select(o => o.Disabled));
        Assert.Empty(document.GetSelectOptions(document.GetElement("i")!));
    }
}
