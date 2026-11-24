namespace PeachBrackets.Scripting.Tests;

/// <summary>The CSS <c>prince-script()</c> content item: text a function the document's scripts registered computes while the document is laid out.</summary>
public class ScriptFunctionTests
{
    private static async Task<(HtmlDocument Document, CollectingLogger Logger)> RunAsync(string html, bool withHost = true)
    {
        var logger = new CollectingLogger();
        var context = await TestDocument.NewContextAsync();
        var options = new HtmlDocumentOptions
        {
            MediaType = MediaType.Print,
            PageWidthPoints = 300,
            PageHeightPoints = 200,
            RecordTextRuns = true,
            MeasuringCanvasFactory = () => context.CreateCanvas(1, 1),
        };
        if (withHost)
            options.ScriptHost = new JintScriptHost(new JintScriptOptions { Logger = logger });
        options.SetMargins(0);

        var document = await HtmlDocument.LoadAsync(TestDocument.Wrap(html), context, options);
        using var measuring = context.CreateCanvas(1, 1);
        await document.LayoutAsync(measuring);
        return (document, logger);
    }

    private static string Text(HtmlDocument document) => string.Join("|", document.TextRuns.Select(r => r.Text));

    [Fact]
    public async Task AFunctionsResult_IsTheContent()
    {
        var (document, logger) = await RunAsync(
            "<style>p::after { content: prince-script(myfunc); }</style><p>before</p><script>Prince.addScriptFunc('myfunc', function () { return ' and after'; });</script>");
        using (document)
        {
            Assert.Contains("before and after", Text(document));
            Assert.Empty(logger.Errors);
        }
    }

    [Fact]
    public async Task TheArgumentsAreTheTextOfTheContentItemsGiven()
    {
        var (document, _) = await RunAsync(
            "<style>ol { list-style: none; margin: 0; padding: 0 } li::before { content: prince-script(words, counter(list-item), ': ', attr(data-n)); }</style>"
            + "<ol><li data-n='x'>a</li><li data-n='y'>b</li><li data-n='z'>c</li></ol>"
            + "<script>Prince.addScriptFunc('words', function (n, sep, attr) { return ['one', 'two', 'three'][n - 1] + sep + attr + ' '; });</script>");
        using (document)
        {
            var text = Text(document);
            Assert.Contains("one: x a", text);
            Assert.Contains("two: y b", text);
            Assert.Contains("three: z c", text);
        }
    }

    [Theory]
    [InlineData("'page ' + page + ' of ' + pages", true)]
    [InlineData("''", false)]
    public async Task AMarginBoxIsMadeFromItPerPage_WithThePageCounters(string body, bool ink)
    {
        var rows = string.Concat(Enumerable.Repeat("<p style='margin:0;height:50pt'>row</p>", 6));
        var logger = new CollectingLogger();
        var context = await TestDocument.NewContextAsync();
        var calls = new List<string>();
        var options = new HtmlDocumentOptions
        {
            MediaType = MediaType.Print,
            PageWidthPoints = 300,
            PageHeightPoints = 200,
            ScriptHost = new JintScriptHost(new JintScriptOptions { Logger = logger }),
            MeasuringCanvasFactory = () => context.CreateCanvas(1, 1),
        };
        options.SetMargins(20);
        var html = TestDocument.Wrap(
            "<style>@page { margin: 20pt; @top-center { content: prince-script(stamp, counter(page), counter(pages)); font-size: 12pt; } }</style>" + rows
            + "<script>Prince.addScriptFunc('stamp', function (page, pages) { Log.info(page + '/' + pages); return " + body + "; });</script>");

        using var document = await HtmlDocument.LoadAsync(html, context, options);
        using (var measuring = context.CreateCanvas(1, 1))
            await document.LayoutAsync(measuring);

        Assert.True(document.Pages.Count > 1);
        using var canvas = await TestDocument.PaintAsync(context, document);
        Assert.Equal(ink, TestDocument.HasInk(canvas, 90, 2, 120, 16));
        _ = calls;

        // The function was asked for with the numbers of the page being made and of the whole document.
        Assert.Contains(logger.Messages, m => m == "Information:1/" + document.Pages.Count);
    }

    [Fact]
    public async Task ABookmarkLabelCanBeMadeFromIt()
    {
        var (document, _) = await RunAsync(
            "<style>h1 { bookmark-level: 1; bookmark-label: prince-script(label, attr(id)); }</style><h1 id=intro>Introduction</h1>"
            + "<script>Prince.addScriptFunc('label', function (id) { return 'Section ' + id; });</script>");
        using (document)
        {
            Assert.Equal("Section intro", Assert.Single(document.Annotations.Bookmarks).Title);
        }
    }

    [Fact]
    public async Task WithoutAFunctionOfThatNameOrAHost_TheItemIsEmpty()
    {
        var (unknown, _) = await RunAsync("<style>p::after { content: '[' prince-script(nothing) ']'; }</style><p>x</p>");
        using (unknown)
        {
            Assert.Contains("x[]", Text(unknown));
        }

        var (noHost, _) = await RunAsync("<style>p::after { content: '[' prince-script(f) ']'; }</style><p>x</p><script>Prince.addScriptFunc('f', function () { return 'no'; });</script>", withHost: false);
        using (noHost)
        {
            Assert.Contains("x[]", Text(noHost));
        }
    }

    [Fact]
    public async Task AFunctionThatThrows_IsLoggedAndContributesNothing()
    {
        var (document, logger) = await RunAsync(
            "<style>p::after { content: '<' prince-script(bad) prince-script(nothingReturned) '>'; }</style><p>x</p>"
            + "<script>Prince.addScriptFunc('bad', function () { oops(); }); Prince.addScriptFunc('nothingReturned', function () {});</script>");
        using (document)
        {
            Assert.Contains("x<>", Text(document));
            Assert.Contains(logger.Errors, e => e.Contains("oops", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task TheItemIsValidCssOnlyWithANameAndItems()
    {
        // An invalid declaration is dropped, so the paragraph keeps its own text and nothing is generated.
        var (document, _) = await RunAsync(
            "<style>#a::after { content: prince-script(); } #b::after { content: prince-script(f,); } #c::after { content: prince-script(f 'x'); } #d::after { content: prince-script('f', 'x'); }</style>"
            + "<p id=a>A</p><p id=b>B</p><p id=c>C</p><p id=d>D</p><script>Prince.addScriptFunc('f', function (x) { return '+' + x; });</script>");
        using (document)
        {
            var text = Text(document);
            Assert.DoesNotContain("A+", text);
            Assert.DoesNotContain("B+", text);
            Assert.DoesNotContain("C+", text);
            Assert.Contains("D+x", text);
        }
    }

    [Fact]
    public async Task ANameThatTakesAnUnknownArgument_AddScriptFuncRefusesNonFunctions()
    {
        var (document, logger) = await RunAsync("<script>try { Prince.addScriptFunc('x', 5); } catch (e) { Log.info('refused'); } try { Prince.addScriptFunc('', function () {}); } catch (e) { Log.info('refused'); }</script>");
        using (document)
        {
            Assert.Equal(["Information:refused", "Information:refused"], logger.Messages);
        }
    }
}
