namespace PeachBrackets.Tests;

public class RangeAndSelectTests
{
    private const string Head = "<style>@page { margin: 0 } input, select { display: block; margin: 0; position: absolute; left: 10pt; top: 10pt }</style>";

    private static async Task<HtmlDocument> LoadAsync(string body)
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap(body, Head), new HtmlDocumentOptions { MediaType = MediaType.Screen, PaintFormControls = true });
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

    [Fact]
    public async Task AnOptionOrOptgroupStartTagEndsTheOptionBeforeIt()
    {
        var document = await LoadAsync("<select id=s><option value=1>One<option selected>Two<optgroup label=g><option>Three<option disabled>Four<optgroup label=h><option>Five</select>");

        var options = document.GetSelectOptions(document.GetElement("s")!);

        Assert.Equal(["One", "Two", "Three", "Four", "Five"], options.Select(o => o.Label));
        Assert.Equal([false, true, false, false, false], options.Select(o => o.Selected));
        Assert.Equal([false, false, false, true, false], options.Select(o => o.Disabled));
    }

    [Fact]
    public async Task AFileInputAsksForItsKindsAndRemembersTheNamesOfTheFilesChosen()
    {
        var document = await LoadAsync("<input id=one type=file accept='.png,image/*'><input id=many type=file multiple><input id=off type=file disabled><input id=t>");
        var one = document.GetElement("one")!;
        var many = document.GetElement("many")!;

        Assert.Equal((false, ".png,image/*"), (document.GetFileInputInfo(one)!.Value.Multiple, document.GetFileInputInfo(one)!.Value.Accept));
        Assert.True(document.GetFileInputInfo(many)!.Value.Multiple);
        Assert.Null(document.GetFileInputInfo(document.GetElement("t")!));

        Assert.True(document.SetFileNames(one, ["photo.png"]).Applied);
        Assert.Equal("photo.png", document.GetFormState(one)!.Value.Value);
        Assert.False(document.SetFileNames(one, ["a.png", "b.png"]).Applied);

        Assert.True(document.SetFileNames(many, ["a.txt", "b.txt", "  ", "c.txt"]).Applied);
        Assert.Equal("a.txt, b.txt, c.txt", document.GetFormState(many)!.Value.Value);
        Assert.True(document.SetFileNames(many, []).Applied);
        Assert.Equal(string.Empty, document.GetFormState(many)!.Value.Value);

        Assert.False(document.SetFileNames(document.GetElement("off")!, ["x"]).Applied);
        Assert.False(document.SetFileNames(document.GetElement("t")!, ["x"]).Applied);
    }

    [Fact]
    public async Task ListItemsDescriptionsAndTableSectionsEndWhenTheNextOneStarts()
    {
        var document = await LoadAsync(
            "<ul id=l><li>a<li>b<ul><li>b1<li>b2</ul><li><p>c<li>d</ul>"
            + "<dl id=d><dt>t1<dd>d1<dt>t2<dd><p>d2<dt>t3<dd>d3</dl>"
            + "<table id=t><thead><tr><th>h1<th>h2<tbody><tr><td>1<td>2<tr><td>3<td>4<tfoot><tr><td>f1<td>f2</table>");
        var tree = document.GetAccessibilityTree();

        var list = tree.Nodes.First(n => n.ElementId == "l");
        Assert.Equal(4, list.Children.Count(c => c.Role == PeachBrackets.Accessibility.AccessibilityRole.ListItem));
        Assert.Equal("a", list.Children.First(c => c.Role == PeachBrackets.Accessibility.AccessibilityRole.ListItem).ReadingText.Trim());

        Assert.Equal(3, tree.Nodes.Count(n => n.Role == PeachBrackets.Accessibility.AccessibilityRole.Term));
        Assert.Equal(3, tree.Nodes.Count(n => n.Role == PeachBrackets.Accessibility.AccessibilityRole.Definition));

        var table = tree.Nodes.First(n => n.ElementId == "t");
        Assert.Equal((4, 2), (table.RowCount, table.ColumnCount));
    }

    [Fact]
    public async Task FormattingStillOpenWhenAnEndIsImpliedIsOpenedAgainInTheNextElement()
    {
        var document = await LoadAsync("<ul id=l><li>one <b>bold<li>two<li>three</ul><p>para <i>italic<p>next</p>");
        var tree = document.GetAccessibilityTree();

        var items = tree.Nodes.First(n => n.ElementId == "l").Children.Where(c => c.Role == PeachBrackets.Accessibility.AccessibilityRole.ListItem).ToList();

        Assert.Equal(3, items.Count);
        // "two" and "three" carry the bold that was left open in the first item.
        Assert.Equal(700, items[1].DescendantsAndSelf().First(n => n.Role == PeachBrackets.Accessibility.AccessibilityRole.Text).FontWeight);
        Assert.Equal(700, items[2].DescendantsAndSelf().First(n => n.Role == PeachBrackets.Accessibility.AccessibilityRole.Text).FontWeight);
        Assert.Equal(400, items[0].DescendantsAndSelf().First(n => n.Role == PeachBrackets.Accessibility.AccessibilityRole.Text).FontWeight);
        Assert.Equal(2, tree.Nodes.Count(n => n.Role == PeachBrackets.Accessibility.AccessibilityRole.Paragraph));
        Assert.Contains(tree.Nodes.Last(n => n.Role == PeachBrackets.Accessibility.AccessibilityRole.Paragraph).DescendantsAndSelf(), n => n.IsItalic);
    }
}
