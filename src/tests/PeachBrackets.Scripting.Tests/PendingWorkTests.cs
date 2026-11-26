using Microsoft.Extensions.Logging;
using PeachBrackets.ScriptHosting;

namespace PeachBrackets.Scripting.Tests;

/// <summary>What a run says it was left waiting for, and the click activation of checkboxes.</summary>
public class PendingWorkTests
{
    private static async Task<CollectingLogger> RunAsync(string script, string body = "")
    {
        var logger = new CollectingLogger();
        var context = await TestDocument.NewContextAsync();
        var options = new HtmlDocumentOptions
        {
            MediaType = MediaType.Screen,
            PageWidthPoints = 400,
            ScriptHost = new JintScriptHost(new JintScriptOptions { Logger = logger, MaxVirtualTime = TimeSpan.FromSeconds(5) }),
            MeasuringCanvasFactory = () => context.CreateCanvas(1, 1),
        };
        using var document = await HtmlDocument.LoadAsync(TestDocument.Wrap(body + "<script>" + script + "</script>"), context, options);
        Assert.True(logger.Errors.Count == 0, string.Join(" / ", logger.Errors));
        return logger;
    }

    [Fact]
    public async Task ARunThatEndsWithTimersLeft_SaysWhichAndWhy()
    {
        var logger = await RunAsync("function later() {} setTimeout(later, 60000); document.addEventListener('ping', function () {}); document.dispatchEvent(new Event('ping'));");

        var entry = Assert.Single(logger.Entries, e => e.Id == ScriptLogEvents.PendingWork);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("1 timers waiting", entry.Text);
        Assert.Contains("past the 5000 ms virtual limit: later", entry.Text);
        Assert.Contains("ping@document", entry.Text);
    }

    [Fact]
    public async Task ARunThatEndsWithNothingLeft_SaysSoAtDebugLevel()
    {
        var logger = await RunAsync("setTimeout(function () {}, 10);");

        var entry = Assert.Single(logger.Entries, e => e.Id == ScriptLogEvents.PendingWork);
        Assert.Equal(LogLevel.Debug, entry.Level);
        Assert.StartsWith("nothing scheduled; last events:", entry.Text);
    }

    [Fact]
    public async Task Clicking_TogglesACheckbox_UnlessAListenerCancels_AndFiresInputAndChange()
    {
        var logger = await RunAsync("""
            var a = document.getElementById('a'), b = document.getElementById('b'), seen = [];
            a.addEventListener('input', function () { seen.push('input'); });
            a.addEventListener('change', function () { seen.push('change'); });
            a.click();
            b.addEventListener('click', function (e) { e.preventDefault(); });
            b.click();
            document.getElementById('d').click();
            Log.info([a.checked, b.checked, document.getElementById('d').checked, seen.join()].join('|'));
            """, "<input type=checkbox id=a><input type=checkbox id=b><input type=checkbox id=d disabled>");

        Assert.Equal(["true|false|false|input,change"], logger.Texts);
    }
}
