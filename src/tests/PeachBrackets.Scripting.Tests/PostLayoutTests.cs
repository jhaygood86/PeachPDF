namespace PeachBrackets.Scripting.Tests;

/// <summary>Scripts that run after the document is laid out: post-layout functions, passes, the complete event and the page count.</summary>
public class PostLayoutTests
{
    // Six 50pt paragraphs: 300pt of content, which is two 200pt pages. (A single tall empty block is not split across pages.)
    private const string Tall = "<p style='margin:0;height:50pt'>a</p><p style='margin:0;height:50pt'>b</p><p style='margin:0;height:50pt'>c</p><p style='margin:0;height:50pt'>d</p><p style='margin:0;height:50pt'>e</p><p style='margin:0;height:50pt'>f</p>";

    // Appends 600pt of paragraphs (twelve of them) to the body.
    private const string AddMore = "var d = document.createElement('div'); d.innerHTML = new Array(13).join(\"<p style='margin:0;height:50pt'>x</p>\"); document.body.appendChild(d);";

    private static async Task<(HtmlDocument Document, CollectingLogger Logger)> RunAsync(string html, int maxPasses = 16)
    {
        var logger = new CollectingLogger();
        var options = new JintScriptOptions { Logger = logger };
        var context = await TestDocument.NewContextAsync();
        var htmlOptions = new HtmlDocumentOptions
        {
            MediaType = MediaType.Print,
            PageWidthPoints = 300,
            PageHeightPoints = 200,
            ScriptHost = new JintScriptHost(options),
            MeasuringCanvasFactory = () => context.CreateCanvas(1, 1),
            MaxLayoutPasses = maxPasses,
            RecordTextRuns = true,
        };
        htmlOptions.SetMargins(0);
        var document = await HtmlDocument.LoadAsync(TestDocument.Wrap(html), context, htmlOptions);
        using var measuring = context.CreateCanvas(1, 1);
        await document.LayoutAsync(measuring);
        return (document, logger);
    }

    [Fact]
    public async Task APostLayoutFunction_RunsAfterLayoutAndSeesThePageCount()
    {
        var (document, logger) = await RunAsync(Tall + "<script>Log.info('before ' + Prince.pageCount); Prince.registerPostLayoutFunc(function () { Log.info('after ' + Prince.pageCount); });</script>");
        using (document)
        {
            Assert.Equal(["Information:before 0", "Information:after 2"], logger.Messages);
            Assert.Equal(2, document.Pages.Count);
        }
    }

    [Fact]
    public async Task AChangeAfterLayout_IsLaidOutAgain()
    {
        var (document, logger) = await RunAsync(Tall + "<script>Prince.registerPostLayoutFunc(function () { " + AddMore + " });</script>");
        using (document)
        {
            Assert.Equal(5, document.Pages.Count);
            Assert.Empty(logger.Errors);
        }
    }

    [Fact]
    public async Task AFunctionThatRegistersItselfAndChangesTheDocument_StopsAtThePassLimit()
    {
        var (document, logger) = await RunAsync(
            "<p id=p>x</p><script>var n = 0; function again() { n++; document.getElementById('p').textContent = 'pass ' + n; Prince.registerPostLayoutFunc(again); } Prince.registerPostLayoutFunc(again); Prince.addEventListener('complete', function () { Log.info('n=' + n); });</script>",
            maxPasses: 5);
        using (document)
        {
            Assert.Equal(["Information:n=5"], logger.Messages);
        }
    }

    [Fact]
    public async Task ASelfRegisteringFunctionThatChangesNothing_RunsAgainWithoutALayout()
    {
        var (document, logger) = await RunAsync(
            "<script>var n = 0; function again() { n++; if (n < 4) Prince.registerPostLayoutFunc(again); } Prince.registerPostLayoutFunc(again); Prince.addEventListener('complete', function () { Log.info('n=' + n); });</script>");
        using (document)
        {
            Assert.Equal(["Information:n=4"], logger.Messages);
        }
    }

    [Fact]
    public async Task Complete_FiresOnceAfterTheLastPass_WithTheFinalPageCount()
    {
        var (document, logger) = await RunAsync(
            Tall + "<script>Prince.oncomplete = function () { Log.info('oncomplete ' + Prince.pageCount); };"
            + "Prince.addEventListener('complete', function (e) { Log.info('listener ' + e.type); }, false);"
            + "Prince.registerPostLayoutFunc(function () { " + AddMore + " });</script>");
        using (document)
        {
            Assert.Equal(["Information:listener complete", "Information:oncomplete 5"], logger.Messages);
            Assert.Equal(5, document.Pages.Count);
        }
    }

    [Fact]
    public async Task AChangeInComplete_IsNotLaidOut()
    {
        var (document, _) = await RunAsync(Tall + "<script>Prince.addEventListener('complete', function () { " + AddMore + " });</script>");
        using (document)
        {
            Assert.Equal(2, document.Pages.Count);
        }
    }

    [Fact]
    public async Task APostLayoutFunctionThatThrows_IsLoggedAndTheOthersStillRun()
    {
        var (document, logger) = await RunAsync("<script>Prince.registerPostLayoutFunc(function () { nope(); }); Prince.registerPostLayoutFunc(function () { Log.info('second'); });</script>");
        using (document)
        {
            Assert.Contains(logger.Errors, e => e.Contains("nope", StringComparison.Ordinal));
            Assert.Equal(["Information:second"], logger.Messages);
        }
    }

    [Fact]
    public async Task APageWithNothingRegistered_IsLaidOutOnce_AndNoLimitStillStopsWhenTheScriptDoes()
    {
        var (document, logger) = await RunAsync("<p>plain</p><script>Log.info('only');</script>");
        using (document)
        {
            Assert.Equal(["Information:only"], logger.Messages);
        }

        var (again, _) = await RunAsync(
            "<p id=p>x</p><script>var n = 0; Prince.registerPostLayoutFunc(function f() { if (++n < 40) { document.getElementById('p').textContent = 'n' + n; Prince.registerPostLayoutFunc(f); } });</script>",
            maxPasses: 0);
        using (again)
        {
            Assert.Contains(again.TextRuns, r => r.Text.Contains("n39", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task ReadyStateChange_FiresAsTheDocumentLoads()
    {
        var (document, logger) = await RunAsync("<script>document.addEventListener('readystatechange', function () { Log.info(document.readyState); });</script>");
        using (document)
        {
            Assert.Equal(["Information:interactive", "Information:complete"], logger.Messages);
        }
    }
}
