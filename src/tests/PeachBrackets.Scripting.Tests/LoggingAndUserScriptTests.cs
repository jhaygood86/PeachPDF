using Microsoft.Extensions.Logging;

namespace PeachBrackets.Scripting.Tests;

/// <summary>What scripts write goes to the host's <see cref="ILogger"/>, and the host can supply scripts of its own.</summary>
public class LoggingAndUserScriptTests
{
    private static async Task<HtmlDocument> LoadAsync(string body, JintScriptOptions options)
    {
        var context = await TestDocument.NewContextAsync();
        return await HtmlDocument.LoadAsync(
            TestDocument.Wrap(body),
            context,
            new HtmlDocumentOptions
            {
                MediaType = MediaType.Screen,
                PageWidthPoints = 400,
                ScriptHost = new JintScriptHost(options),
                MeasuringCanvasFactory = () => context.CreateCanvas(1, 1),
            });
    }

    [Fact]
    public async Task ConsoleAndLogCalls_ArriveAtTheirLevels()
    {
        var logger = new CollectingLogger();
        using var document = await LoadAsync("""
            <script>
            console.log('a'); console.info('b'); console.warn('c'); console.error('d'); console.debug('e');
            Log.debug('f'); Log.info('g'); Log.warning('h'); Log.error('i');
            console.assert(false, 'j');
            </script>
            """, new JintScriptOptions { Logger = logger });

        Assert.Equal(
            ["Information:a", "Information:b", "Warning:c", "Error:d", "Debug:e", "Debug:f", "Information:g", "Warning:h", "Error:i", "Error:Assertion failed: j"],
            logger.Messages);
    }

    [Fact]
    public async Task LogData_IsAStructuredEntryUnderItsOwnEventId()
    {
        var logger = new CollectingLogger();
        using var document = await LoadAsync("<script>Log.data('total-page-count', 7);</script>", new JintScriptOptions { Logger = logger });

        var entry = Assert.Single(logger.Entries, e => e.Id == ScriptLogEvents.Data);
        var data = Assert.IsType<ScriptDataEntry>(entry.State);
        Assert.Equal(("total-page-count", "7"), (data.Name, data.Value));
        Assert.Equal(["name", "value"], data.Select(p => p.Key));
        Assert.Equal(["total-page-count", "7"], data.Select(p => (string?)p.Value));
        Assert.Equal("total-page-count=7", entry.Text);
        Assert.Throws<ArgumentOutOfRangeException>(() => data[2]);
    }

    [Fact]
    public async Task AScriptThatThrows_IsLoggedAsAnErrorAndTheDocumentStillLoads()
    {
        var logger = new CollectingLogger();
        using var document = await LoadAsync("<script>nope.nothing();</script><script>Log.info('after');</script>", new JintScriptOptions { Logger = logger });

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error && e.Id == ScriptLogEvents.ScriptFailed && e.Text.Contains("nope", StringComparison.Ordinal));
        Assert.Equal(["Information:after"], logger.Messages);
    }

    [Fact]
    public async Task WithoutALogger_ScriptsRunAndNothingIsWritten()
    {
        using var document = await LoadAsync("<script>console.log('x'); Log.data('a', 'b'); nope();</script><p id=p>ok</p>", new JintScriptOptions());

        Assert.NotNull(document);
    }

    [Fact]
    public async Task TheLogObjectCanBeLeftOut()
    {
        var logger = new CollectingLogger();
        using var document = await LoadAsync("<script>console.log(typeof Log);</script>", new JintScriptOptions { Logger = logger, PrinceCompatibility = false });

        Assert.Equal(["Information:undefined"], logger.Messages);
    }

    [Fact]
    public async Task UserScriptsRunBeforeTheDocumentsOwnScripts()
    {
        var logger = new CollectingLogger();
        var options = new JintScriptOptions { Logger = logger };
        options.UserScripts.Add(new UserScript("one.js", "Log.info('user 1 ' + typeof document.getElementById('p'));"));
        options.UserScripts.Add(new UserScript("two.js", "Log.info('user 2');"));
        using var document = await LoadAsync("<p id=p></p><script>Log.info('author');</script>", options);

        Assert.Equal(["Information:user 1 object", "Information:user 2", "Information:author"], logger.Messages);
    }

    [Fact]
    public async Task UserScriptsRunWhenTheDocumentsOwnScriptsAreOff()
    {
        var logger = new CollectingLogger();
        var options = new JintScriptOptions { Logger = logger, RunAuthorScripts = false };
        options.UserScripts.Add(new UserScript("fix.js", "var p = document.getElementById('p'); p.style.width = '100pt'; p.style.height = '20pt'; Log.info('user');"));
        using var document = await LoadAsync("<p id=p></p><script>Log.info('author');</script>", options);

        Assert.Equal(["Information:user"], logger.Messages);

        using var measuring = (await TestDocument.NewContextAsync()).CreateCanvas(1, 1);
        await document.LayoutAsync(measuring);
        Assert.Equal(100, document.GetElementBounds("p")!.Value.Width, 1);
    }

    [Fact]
    public async Task AFailingUserScript_IsReportedByItsName()
    {
        var logger = new CollectingLogger();
        var options = new JintScriptOptions { Logger = logger };
        options.UserScripts.Add(new UserScript("broken.js", "throw new Error('bad');"));
        options.UserScripts.Add(new UserScript("fine.js", "Log.info('still runs');"));
        using var document = await LoadAsync("<p></p>", options);

        Assert.Contains(logger.Errors, e => e.Contains("bad", StringComparison.Ordinal));
        Assert.Equal(["Information:still runs"], logger.Messages);
    }
}
