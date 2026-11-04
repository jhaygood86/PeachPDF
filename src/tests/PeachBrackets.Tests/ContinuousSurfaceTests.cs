namespace PeachBrackets.Tests;

/// <summary>Screen media has no pages: the document is one surface as wide as the widget and as tall as its content.</summary>
public class ContinuousSurfaceTests
{
    private static HtmlDocumentOptions Screen(double width = 400, double? viewportHeight = null)
    {
        var options = new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = width, ViewportHeightPoints = viewportHeight };
        options.SetMargins(0);
        return options;
    }

    private static HtmlDocumentOptions WithTextRuns(HtmlDocumentOptions options)
    {
        options.RecordTextRuns = true;
        return options;
    }

    [Fact]
    public async Task ADocumentTallerThanAnyPage_IsOneSurface()
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap("<div style='height:30000pt'></div><div style='height:50pt'></div>"), Screen());

        Assert.Single(document.Pages);
        Assert.Equal(30050, document.Pages[0].HeightPoints, 1);
        Assert.Equal(400, document.Pages[0].WidthPoints, 1);
        Assert.Equal(30050, document.GetContentHeight(0), 1);
    }

    [Fact]
    public async Task APrintDocument_IsStillPaged()
    {
        var options = new HtmlDocumentOptions { PageWidthPoints = 400, PageHeightPoints = 300 };
        options.SetMargins(0);
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap(string.Concat(Enumerable.Repeat("<div style='height:100pt'>x</div>", 10))), options);

        Assert.True(document.Pages.Count > 1);
        Assert.Equal(300, document.Pages[0].HeightPoints, 1);
    }

    [Fact]
    public async Task ASurfaceIsAtLeastAsTallAsTheViewport()
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap("<div style='height:50pt'></div>"), Screen(viewportHeight: 300));

        Assert.Equal(300, document.Pages[0].HeightPoints, 1);
        Assert.Equal(50, document.GetContentHeight(0), 1);
    }

    [Fact]
    public async Task ViewportUnits_ResolveAgainstTheViewport_NotTheContent()
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap("<div id=a style='height:50vh'></div>"), Screen(viewportHeight: 400));

        Assert.Equal(200, document.Pages[0].HeightPoints - 200, 1);
        Assert.Equal(200, document.GetContentHeight(0), 1);
    }

    [Fact]
    public async Task APageRule_DoesNotApplyToAContinuousSurface()
    {
        var (_, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap("<div style='height:100pt'></div>", "<style>@page { size: 100pt 50pt; margin: 30pt }</style>"), Screen());

        Assert.Single(document.Pages);
        Assert.Equal(400, document.Pages[0].WidthPoints, 1);
        Assert.Equal(100, document.Pages[0].HeightPoints, 1);
    }

    [Fact]
    public async Task APageBreak_HasNoEffectOnAContinuousSurface()
    {
        var (_, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap("<div style='height:20pt'></div><div style='break-before:page;height:20pt'></div>"), Screen());

        Assert.Single(document.Pages);
        Assert.Equal(40, document.GetContentHeight(0), 1);
    }

    [Fact]
    public async Task AColumnBreak_StillWorksInsideAMulticolContainer()
    {
        var (_, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap("<div style='column-count:2;column-gap:0'><div style='height:20pt'></div><div style='break-before:column;height:20pt'></div></div>"), Screen());

        Assert.Single(document.Pages);
        Assert.Equal(20, document.GetContentHeight(0), 1);
    }

    [Fact]
    public async Task AFootnoteFloat_IsDroppedLikeABrowserDropsIt()
    {
        var (_, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap("<p style='margin:0'>text<span style='float:footnote'>note</span></p>"), WithTextRuns(Screen()));

        Assert.Single(document.Pages);
        Assert.Contains(document.TextRuns, run => run.Text.Contains("note"));
    }

    [Fact]
    public async Task TheMediaType_DecidesWhichMediaRulesApply()
    {
        const string head = "<style>@media screen { #a { height: 10pt } } @media print { #a { height: 70pt } }</style>";
        var (_, screen) = await TestDocument.LoadAsync(TestDocument.Wrap("<div id=a></div>", head), Screen());
        var print = new HtmlDocumentOptions { MediaType = MediaType.Print };
        print.SetMargins(0);
        var (_, paged) = await TestDocument.LoadAsync(TestDocument.Wrap("<div id=a></div>", head), print);

        Assert.Equal(10, screen.GetContentHeight(0), 1);
        Assert.Equal(70, paged.GetContentHeight(0), 1);
    }
}
