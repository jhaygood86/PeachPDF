using PeachBrackets.Accessibility;

namespace PeachBrackets.Tests;

public class AccessibilityStructureTests
{
    private static async Task<AccessibilityTree> TreeAsync(string body)
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap(body), new HtmlDocumentOptions { MediaType = MediaType.Screen });
        return document.GetAccessibilityTree();
    }

    private static AccessibilityNode Cell(AccessibilityTree tree, string name) =>
        Assert.Single(tree.Nodes, n => n.Name == name && n.Role is AccessibilityRole.Cell or AccessibilityRole.ColumnHeader or AccessibilityRole.RowHeader or AccessibilityRole.GridCell);

    // ---- tables --------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task CellsAndRowsKnowTheirPlaceInTheTable()
    {
        var tree = await TreeAsync(
            "<table><thead><tr><th>A</th><th>B</th></tr></thead><tbody><tr><td>a1</td><td>b1</td></tr><tr><td>a2</td><td>b2</td></tr></tbody></table>");

        var table = Assert.Single(tree.Nodes, n => n.Role == AccessibilityRole.Table);
        Assert.Equal(3, table.RowCount);
        Assert.Equal(2, table.ColumnCount);
        Assert.Equal((0, 0), (Cell(tree, "A").RowIndex, Cell(tree, "A").ColumnIndex));
        Assert.Equal((0, 1), (Cell(tree, "B").RowIndex, Cell(tree, "B").ColumnIndex));
        Assert.Equal((1, 1), (Cell(tree, "b1").RowIndex, Cell(tree, "b1").ColumnIndex));
        Assert.Equal((2, 0), (Cell(tree, "a2").RowIndex, Cell(tree, "a2").ColumnIndex));
        Assert.Equal([0, 1, 2], tree.Nodes.Where(n => n.Role == AccessibilityRole.Row).Select(n => n.RowIndex).ToArray());
    }

    [Fact]
    public async Task SpansPushTheCellsThatFollow()
    {
        var tree = await TreeAsync(
            "<table><tr><td rowspan=2>tall</td><td colspan=2>wide</td></tr><tr><td>x</td><td>y</td></tr></table>");

        var tall = Cell(tree, "tall");
        Assert.Equal((0, 0, 2, 1), (tall.RowIndex, tall.ColumnIndex, tall.RowSpan, tall.ColumnSpan));
        var wide = Cell(tree, "wide");
        Assert.Equal((0, 1, 1, 2), (wide.RowIndex, wide.ColumnIndex, wide.RowSpan, wide.ColumnSpan));
        // The second row starts after the column the tall cell takes.
        Assert.Equal((1, 1), (Cell(tree, "x").RowIndex, Cell(tree, "x").ColumnIndex));
        Assert.Equal((1, 2), (Cell(tree, "y").RowIndex, Cell(tree, "y").ColumnIndex));
        Assert.Equal(3, Assert.Single(tree.Nodes, n => n.Role == AccessibilityRole.Table).ColumnCount);
    }

    [Fact]
    public async Task ADataCellKnowsTheHeadersOfItsColumnAndRow()
    {
        var tree = await TreeAsync(
            "<table><tr><th></th><th>Col1</th><th>Col2</th></tr><tr><th scope=row>Row1</th><td>d11</td><td>d12</td></tr></table>");

        var cell = Cell(tree, "d12");
        Assert.Equal(["Col2"], cell.ColumnHeaders.Select(h => h.Name).ToArray());
        Assert.Equal(["Row1"], cell.RowHeaders.Select(h => h.Name).ToArray());
        Assert.Empty(Cell(tree, "Col1").ColumnHeaders);
    }

    [Fact]
    public async Task TheHeadersAttributeNamesTheHeadersExactly()
    {
        var tree = await TreeAsync(
            "<table><tr><th id=a>A</th><th id=b>B</th></tr><tr><td headers='b'>x</td></tr></table>");

        Assert.Equal(["B"], Cell(tree, "x").ColumnHeaders.Select(h => h.Name).ToArray());
    }

    [Fact]
    public async Task AriaCanSetTheIndexesAndCountsOfAGrid()
    {
        var tree = await TreeAsync(
            "<div role=grid aria-rowcount=100 aria-colcount=20><div role=row aria-rowindex=5><div role=gridcell aria-colindex=7>g</div></div></div>");

        var grid = Assert.Single(tree.Nodes, n => n.Role == AccessibilityRole.Grid);
        Assert.Equal((100, 20), (grid.RowCount, grid.ColumnCount));
        var cell = Cell(tree, "g");
        Assert.Equal((4, 6), (cell.RowIndex, cell.ColumnIndex));
    }

    [Fact]
    public async Task ATableInsideATableIsNumberedOnItsOwn()
    {
        var tree = await TreeAsync(
            "<table><tr><td>outer<table><tr><td>in1</td><td>in2</td></tr></table></td></tr></table>");

        var tables = tree.Nodes.Where(n => n.Role == AccessibilityRole.Table).ToArray();
        Assert.Equal(2, tables.Length);
        Assert.Equal((1, 1), (tables[0].RowCount, tables[0].ColumnCount));
        Assert.Equal((1, 2), (tables[1].RowCount, tables[1].ColumnCount));
        Assert.Equal(1, Cell(tree, "in2").ColumnIndex);
    }

    // ---- sets ----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheItemsOfAListKnowTheirPlaceInIt()
    {
        var tree = await TreeAsync("<ul><li>one</li><li>two</li><li>three</li></ul><ol><li>solo</li></ol>");

        var items = tree.Nodes.Where(n => n.Role == AccessibilityRole.ListItem).ToArray();
        Assert.Equal([(1, 3), (2, 3), (3, 3), (1, 1)], items.Select(i => (i.PositionInSet, i.SetSize)).ToArray());
        Assert.Equal((0, 0), (Assert.Single(tree.Nodes, n => n.Role == AccessibilityRole.List && n.Children.Count == 3).PositionInSet, 0));
    }

    [Fact]
    public async Task AriaCanSayWhereAnItemIsInASetThatIsOnlyPartlyThere()
    {
        var tree = await TreeAsync("<div role=listbox><div role=option aria-posinset=7 aria-setsize=50>seventh</div><div role=option>next</div></div>");

        var options = tree.Nodes.Where(n => n.Role == AccessibilityRole.Option).ToArray();
        Assert.Equal((7, 50), (options[0].PositionInSet, options[0].SetSize));
        Assert.Equal((8, 50), (options[1].PositionInSet, options[1].SetSize));
    }

    // ---- relations -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ElementsPointAtEachOtherByIdAndTheRelationsAreResolved()
    {
        var tree = await TreeAsync(
            "<button id=b aria-controls='panel' aria-describedby='hint' aria-details='more' aria-flowto='after missing'>Toggle</button>"
            + "<div id=panel role=region aria-label='Panel'>p</div><p id=hint>Hint</p><p id=more>More</p><p id=after>After</p>"
            + "<input id=i aria-errormessage='err' aria-invalid=true><p id=err>Bad value</p>"
            + "<div role=listbox id=lb aria-activedescendant='o2'><div role=option id=o1>1</div><div role=option id=o2>2</div></div>"
            + "<div id=owner role=group aria-owns='owned'>x</div><span id=owned role=note>owned</span>");

        var button = Assert.Single(tree.Nodes, n => n.Role == AccessibilityRole.Button);
        Assert.Equal(["Panel"], button.Controls.Select(n => n.Name).ToArray());
        Assert.Equal(["Hint"], button.DescribedBy.Select(n => n.ReadingText).ToArray());
        Assert.Equal(["More"], button.Details.Select(n => n.ReadingText).ToArray());
        // An id that is nowhere is left out.
        Assert.Equal(["After"], button.FlowTo.Select(n => n.ReadingText).ToArray());

        var input = Assert.Single(tree.Nodes, n => n.Role == AccessibilityRole.TextBox);
        Assert.Equal(["Bad value"], input.ErrorMessages.Select(n => n.ReadingText).ToArray());
        Assert.True(input.Invalid);

        var listbox = Assert.Single(tree.Nodes, n => n.Role == AccessibilityRole.ListBox);
        Assert.Equal("2", listbox.ActiveDescendant!.Name);

        Assert.Equal(["owned"], Assert.Single(tree.Nodes, n => n.ElementId == "owner").Owns.Select(n => n.ReadingText).ToArray());
    }

    [Fact]
    public async Task APartNamedByAnotherListsItAsItsLabel()
    {
        var tree = await TreeAsync("<p id=l>Label text</p><section aria-labelledby='l'>body</section><div role=group aria-labelledby='l l'>g</div>");

        var named = tree.Nodes.Where(n => n.Role is AccessibilityRole.Region or AccessibilityRole.Group).ToArray();
        Assert.Equal(2, named.Length);
        Assert.All(named, n => Assert.Equal(["Label text"], n.LabelledBy.Select(l => l.ReadingText).ToArray()));
    }

    [Fact]
    public async Task APartDoesNotPointAtItself()
    {
        var tree = await TreeAsync("<div id=me role=group aria-controls='me' aria-owns='me'>x</div>");

        var node = Assert.Single(tree.Nodes, n => n.ElementId == "me");
        Assert.Empty(node.Controls);
        Assert.Empty(node.Owns);
    }
}
