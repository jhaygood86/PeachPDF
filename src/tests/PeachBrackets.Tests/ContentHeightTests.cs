namespace PeachBrackets.Tests;

public class ContentHeightTests
{
    private static HtmlDocumentOptions NoMargins()
    {
        var options = new HtmlDocumentOptions();
        options.SetMargins(0);
        return options;
    }

    [Fact]
    public async Task TheHeightOfAPage_IsWhereItsContentEnds_NotTheHeightOfThePage()
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap("<div style='height:100pt'></div><div style='height:20pt'></div>"), NoMargins());

        Assert.Equal(120, document.GetContentHeight(0), 1);
        Assert.True(document.Pages[0].HeightPoints > 800);
    }

    [Fact]
    public async Task TheBottomMarginOfTheLastBoxIsPartOfTheHeight()
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap("<div style='height:50pt;margin-bottom:12pt'></div>"), NoMargins());

        Assert.Equal(62, document.GetContentHeight(0), 1);
    }

    [Fact]
    public async Task AWrappedParagraph_IsMeasuredByItsLines()
    {
        var (_, wide) = await TestDocument.LoadAsync(TestDocument.Wrap("<p style='margin:0;width:500pt;font-size:12pt;line-height:14pt'>word word word word word word word word word word</p>"), NoMargins());
        var (_, narrow) = await TestDocument.LoadAsync(TestDocument.Wrap("<p style='margin:0;width:60pt;font-size:12pt;line-height:14pt'>word word word word word word word word word word</p>"), NoMargins());

        Assert.InRange(wide.GetContentHeight(0), 13, 16);
        Assert.True(narrow.GetContentHeight(0) > 3 * wide.GetContentHeight(0));
    }

    [Fact]
    public async Task AnEmptyDocument_HasNoHeight()
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap(""), NoMargins());

        Assert.Equal(0, document.GetContentHeight(0), 1);
    }

    [Fact]
    public async Task APageThatDoesNotExist_IsRejected_AndSoIsAnUnlaidOutDocument()
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap("<p>x</p>"), NoMargins());
        Assert.Throws<ArgumentOutOfRangeException>(() => document.GetContentHeight(3));

        var (_, unlaid) = await TestDocument.LoadAsync(TestDocument.Wrap("<p>x</p>"), NoMargins(), layout: false);
        Assert.Throws<InvalidOperationException>(() => unlaid.GetContentHeight(0));
    }

    [Fact]
    public async Task ThePageMargins_AreNotPartOfTheHeight()
    {
        var options = new HtmlDocumentOptions();
        options.SetMargins(30);
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap("<div style='height:40pt'></div>"), options);

        Assert.Equal(40, document.GetContentHeight(0), 1);
    }
}
