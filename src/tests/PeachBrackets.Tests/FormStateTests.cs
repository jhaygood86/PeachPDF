using PeachBrackets.Accessibility;

namespace PeachBrackets.Tests;

public class FormStateTests
{
    private const string Body = """
        <form id=f1>
          <input id=agree type=checkbox name=agree>
          <input id=off type=checkbox disabled>
          <input id=a type=radio name=pick value=a checked>
          <input id=b type=radio name=pick value=b>
          <input id=c type=radio name=pick value=c>
          <input id=name value='Ada'>
          <input id=locked value='x' readonly>
          <input id=level type=range min=0 max=10 value=3>
          <select id=size><option value=s>Small<option value=l selected>Large<option>Medium</select>
          <input id=go type=submit>
        </form>
        <form id=f2><input id=other type=radio name=pick value=z checked></form>
        <p id=text>plain</p>
        """;

    private static async Task<HtmlDocument> LoadAsync()
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap(Body, "<style>@page { margin: 0 } input, select { display: block }</style>"), new HtmlDocumentOptions { Media = "screen" });
        return document;
    }

    private static string Value(HtmlDocument document, string id) => document.GetFormState(document.GetElement(id)!)!.Value.Value;

    [Fact]
    public async Task AControlReportsWhatItHolds()
    {
        var document = await LoadAsync();

        var name = document.GetFormState(document.GetElement("name")!)!.Value;
        Assert.Equal(("text", "Ada", false, false), (name.Type, name.Value, name.Checked, name.Disabled));
        Assert.True(document.GetFormState(document.GetElement("a")!)!.Value.Checked);
        Assert.Equal("pick", document.GetFormState(document.GetElement("a")!)!.Value.Name);
        Assert.True(document.GetFormState(document.GetElement("off")!)!.Value.Disabled);
        Assert.Equal("l", Value(document, "size"));
        Assert.Null(document.GetFormState(document.GetElement("text")!));
    }

    [Fact]
    public async Task ActivatingACheckBoxTogglesItAndSaysWhatToRepaint_AndTheTreeFollows()
    {
        var document = await LoadAsync();
        var agree = document.GetElement("agree")!;
        Assert.Equal(ToggleState.False, document.GetAccessibilityTree().Nodes.Single(n => n.ElementId == "agree").Checked);

        var result = document.Activate(agree);

        Assert.True(result.Applied);
        var region = Assert.Single(result.DirtyRegions);
        var bounds = document.GetElementBounds("agree")!.Value;
        Assert.True(region.Bounds.Contains(bounds.X + 1, bounds.Y + 1));
        Assert.True(document.GetFormState(agree)!.Value.Checked);
        Assert.Equal(ToggleState.True, document.GetAccessibilityTree().Nodes.Single(n => n.ElementId == "agree").Checked);

        Assert.True(document.Activate(agree).Applied);
        Assert.False(document.GetFormState(agree)!.Value.Checked);
    }

    [Fact]
    public async Task CheckingARadioButtonUnchecksTheRestOfItsGroupInItsForm()
    {
        var document = await LoadAsync();

        var result = document.SetChecked(document.GetElement("c")!, true);

        Assert.True(result.Applied);
        Assert.Equal(2, result.DirtyRegions.Count); // a is unchecked, c is checked
        bool Checked(string id) => document.GetFormState(document.GetElement(id)!)!.Value.Checked;
        Assert.False(Checked("a"));
        Assert.False(Checked("b"));
        Assert.True(Checked("c"));
        Assert.True(Checked("other"), "a radio of the same name in another form is another group");
    }

    [Fact]
    public async Task ARadioButtonCannotBeUncheckedDirectly_AndADisabledControlDoesNotChange()
    {
        var document = await LoadAsync();

        Assert.False(document.SetChecked(document.GetElement("a")!, false).Applied);
        Assert.False(document.SetChecked(document.GetElement("off")!, true).Applied);
        Assert.False(document.SetChecked(document.GetElement("name")!, true).Applied);
        Assert.False(document.SetChecked(document.GetElement("text")!, true).Applied);
        Assert.False(document.Activate(document.GetElement("name")!).Applied);
        Assert.True(document.SetChecked(document.GetElement("a")!, true).Applied); // already checked: nothing to repaint
    }

    [Fact]
    public async Task ATextFieldTakesAValue_AndAReadOnlyOneDoesNot()
    {
        var document = await LoadAsync();

        Assert.True(document.SetValue(document.GetElement("name")!, "Grace").Applied);
        Assert.Equal("Grace", Value(document, "name"));
        Assert.False(document.SetValue(document.GetElement("locked")!, "y").Applied);
        Assert.Equal("x", Value(document, "locked"));
        Assert.False(document.SetValue(document.GetElement("agree")!, "y").Applied);
        Assert.False(document.SetValue(document.GetElement("text")!, "y").Applied);
    }

    [Fact]
    public async Task ARangeKeepsItsValueBetweenItsLimits_AndTakesOnlyNumbers()
    {
        var document = await LoadAsync();
        var level = document.GetElement("level")!;

        Assert.True(document.SetValue(level, "99").Applied);
        Assert.Equal("10", Value(document, "level"));
        Assert.True(document.SetValue(level, "-4").Applied);
        Assert.Equal("0", Value(document, "level"));
        Assert.False(document.SetValue(level, "high").Applied);
    }

    [Fact]
    public async Task ADropDownChoosesTheOptionWithTheValue_OrTheTextWhenItHasNone()
    {
        var document = await LoadAsync();
        var size = document.GetElement("size")!;

        Assert.True(document.SetValue(size, "s").Applied);
        Assert.Equal("s", Value(document, "size"));
        Assert.True(document.SetValue(size, "Medium").Applied);
        Assert.Equal("Medium", Value(document, "size"));
        Assert.False(document.SetValue(size, "nothing").Applied);
        Assert.Equal("Medium", Value(document, "size"));
    }
}
