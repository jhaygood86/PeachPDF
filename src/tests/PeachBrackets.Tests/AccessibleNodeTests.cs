namespace PeachBrackets.Tests;

public class AccessibleNodeTests
{
    private static async Task<IReadOnlyList<AccessibleNode>> NodesAsync(string body)
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap(body), new HtmlDocumentOptions { RecordTextRuns = true });
        return document.GetAccessibleNodes();
    }

    [Fact]
    public async Task ABlockIsOneNodeWithItsLinesJoined_AndAHeadingKnowsItsLevel()
    {
        var nodes = await NodesAsync("<h2>A heading</h2><p>First line of a paragraph that is long enough to wrap onto more than one line when the page is narrow, and so it goes on.</p><ul><li>An item</li></ul>");

        Assert.Collection(nodes,
            n => { Assert.Equal(AccessibleRole.Heading, n.Role); Assert.Equal(2, n.HeadingLevel); Assert.Equal("A heading", n.Text); },
            n => { Assert.Equal(AccessibleRole.Text, n.Role); Assert.StartsWith("First line of a paragraph", n.Text); Assert.EndsWith("and so it goes on.", n.Text); },
            n => { Assert.Equal(AccessibleRole.ListItem, n.Role); Assert.Equal("An item", n.Text); });
        Assert.All(nodes, n => Assert.True(n.Bounds.Width > 0 && n.Bounds.Height > 0));
        Assert.True(nodes[1].Bounds.Top > nodes[0].Bounds.Top);
    }

    [Fact]
    public async Task ALinkIsAChildNodeOfItsBlock_WithWhereItLeads()
    {
        var nodes = await NodesAsync("<p>Read <a href='https://example.com/a'>the docs</a> and <a href='#end'>jump</a>.</p><p id=end>The end</p>");

        Assert.Equal(AccessibleRole.Text, nodes[0].Role);
        Assert.Equal("Read the docs and jump.", nodes[0].Text); // the link and the text around it run together as written
        var first = nodes[1];
        Assert.Equal(AccessibleRole.Link, first.Role);
        Assert.Equal("the docs", first.Text);
        Assert.Equal("https://example.com/a", first.Uri);
        Assert.Equal(0, first.ParentIndex);
        Assert.Equal("#end", nodes[2].Uri);
        Assert.Equal("The end", nodes[3].Text);
        Assert.Equal(-1, nodes[3].ParentIndex);
    }

    [Fact]
    public async Task ABlockThatCrossesAPageBreakIsOneNodePerPage()
    {
        var (_, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap(string.Concat(Enumerable.Repeat("<p>A paragraph of ordinary text.</p>", 12))),
            new HtmlDocumentOptions { RecordTextRuns = true, PageHeightPoints = 100, MarginTopPoints = 0, MarginBottomPoints = 0 });

        var pages = document.GetAccessibleNodes().Select(n => n.PageIndex).Distinct().ToList();

        Assert.True(pages.Count > 1);
    }

    [Fact]
    public async Task AnEmptyDocumentHasNoNodes() => Assert.Empty(await NodesAsync(""));
}
