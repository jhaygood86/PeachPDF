using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PeachBrackets;
using PeachBrackets.Scripting;
using PeachDrawing;
using PeachBrackets.WptRunner.Server;
using PeachDrawing.Core;

namespace PeachBrackets.WptRunner;

/// <summary>What the runner needs from a test evaluator, whether it compares pixels or runs a testharness.js page.</summary>
internal interface IWptEvaluator
{
    Task<TestResult> EvaluateAsync(string wptPath);
}

/// <summary>
/// Runs a testharness.js test in the Jint script host and reads its subtests back. The page loads the real <c>resources/testharness.js</c>;
/// only <c>testharnessreport.js</c> is replaced, with a script that logs the result as one JSON line through <c>console.log</c>.
/// A <c>.any.js</c> or <c>.window.js</c> file is wrapped in the page the WPT server would generate for its window global.
/// </summary>
internal sealed partial class TestharnessEvaluator(string wptRoot) : IWptEvaluator
{
    private const string ResultPrefix = "WPT-RESULT ";

    // Triage aid: with PEACHBRACKETS_WPT_SUBTESTS=<file>, every failing subtest is appended there (path, status, name, message; tab separated).
    private static readonly object SubtestLogLock = new();

    // The harness reports its result through this: the numbers are testharness.js's own status codes.
    private const string ReportScript = """
        setup({ output: false });
        add_completion_callback(function (tests, status) {
            console.log("WPT-RESULT " + JSON.stringify({
                status: status.status,
                message: status.message,
                tests: tests.map(function (t) { return { name: t.name, status: t.status, message: t.message }; })
            }));
        });
        """;

    [GeneratedRegex(@"^//\s*META:\s*(\w+)=(.*)$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex MetaLine();

    private static readonly string[] SubtestStatuses = ["PASS", "FAIL", "TIMEOUT", "NOTRUN", "PRECONDITION_FAILED"];
    private static readonly string[] HarnessStatuses = ["OK", "ERROR", "TIMEOUT", "PRECONDITION_FAILED"];

    /// <summary>Why a test file is not run (it needs a server feature or a global PeachBrackets has no model of), or null.</summary>
    public static string? SkipReason(string wptPath)
    {
        var name = wptPath[(wptPath.LastIndexOf('/') + 1)..];
        if (name.Contains(".sub.", StringComparison.Ordinal))
            return "needs server-side substitution";
        if (name.Contains(".worker.", StringComparison.Ordinal) || name.Contains(".serviceworker.", StringComparison.Ordinal) || name.Contains(".sharedworker.", StringComparison.Ordinal))
            return "worker global";
        return null;
    }

    /// <summary>Whether <paramref name="wptPath"/> is a test this evaluator runs: a page that loads testharness.js, or a <c>.any.js</c>/<c>.window.js</c> file.</summary>
    public static bool IsTest(string wptPath, Func<string> read)
    {
        if (wptPath.EndsWith(".any.js", StringComparison.Ordinal) || wptPath.EndsWith(".window.js", StringComparison.Ordinal))
            return true;
        return Path.GetExtension(wptPath).ToLowerInvariant() is ".html" or ".htm" or ".xht" or ".xhtml" or ".svg"
            && read().Contains("testharness.js", StringComparison.Ordinal);
    }

    public async Task<TestResult> EvaluateAsync(string wptPath)
    {
        if (SkipReason(wptPath) is { } skip)
            return new TestResult(Outcome.Skipped, skip);

        var server = new WptServer(wptRoot);
        server.Override("resources/testharnessreport.js", ReportScript);
        // The vendor file wires testdriver.js to a browser's automation; there is none here, so it is empty and the actions fail when used.
        server.Override("resources/testdriver-vendor.js", string.Empty);

        // A .any.js or .window.js file stands for the page the server generates; any other test is the page itself, with .sub. substitution done.
        var served = wptPath.EndsWith(".js", StringComparison.Ordinal) ? wptPath[..^3] + ".html" : wptPath;
        var page = server.Handle(new ServerRequest("GET", new Uri(WptServer.Origin + "/" + served)));
        if (page.Status != 200)
            return wptPath.EndsWith(".js", StringComparison.Ordinal)
                ? new TestResult(Outcome.Skipped, "no window global")
                : new TestResult(Outcome.Fail, $"error: the server answered {page.Status} for the test");

        var text = page.Text;
        if (UnportedHandler(wptPath, text) is { } handler)
            return new TestResult(Outcome.Skipped, $"needs {handler}, which has no ported handler");

        var source = WptRenderer.Prepare(wptPath, text);

        var lines = new List<string>();
        var errors = new List<string>();
        var context = new RasterRenderContext { ResourceLoader = uri => LoadAsync(server, uri) };
        var options = new HtmlDocumentOptions
        {
            MediaType = MediaType.Screen,
            PageWidthPoints = WptRenderer.ViewportWidth * 0.75,
            ViewportHeightPoints = WptRenderer.ViewportHeight * 0.75,
            Syntax = WptRenderer.IsXmlDocument(wptPath) ? DocumentSyntax.Xml : DocumentSyntax.Html,
            ScriptHost = new JintScriptHost(new JintScriptOptions
            {
                ScriptTimeout = TimeSpan.FromSeconds(20),
                MaxVirtualTime = TimeSpan.FromSeconds(60),
                MaxCallbacks = 100_000,
                Logger = new CollectingLogger(lines, errors),
            }),
            MeasuringCanvasFactory = () => context.CreateCanvas(1, 1),
            ScriptNetwork = new ServerNetwork(server),
        };
        options.SetMargins(0);

        using (await HtmlDocument.LoadAsync(source, context, options))
        {
        }

        lock (lines)
        {
            if (lines.Count == 0)
            {
                var why = errors.Count == 0 ? "the harness never reported" : errors[0];
                return new TestResult(Outcome.Fail, "error: no result: " + Trim(why));
            }

            return Interpret(wptPath, lines[^1], errors);
        }
    }

    private static TestResult Interpret(string wptPath, string json, List<string> errors)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var harness = root.GetProperty("status").GetInt32();
        var tests = root.GetProperty("tests").EnumerateArray().ToList();

        var passed = 0;
        string? firstFailure = null;
        foreach (var t in tests)
        {
            var status = t.GetProperty("status").GetInt32();
            if (status == 0)
            {
                passed++;
                continue;
            }

            var name = t.GetProperty("name").GetString();
            var message = t.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
            if (Environment.GetEnvironmentVariable("PEACHBRACKETS_WPT_SUBTESTS") is { Length: > 0 } log)
                lock (SubtestLogLock) File.AppendAllText(log, $"{wptPath}\t{status}\t{name?.ReplaceLineEndings(" ")}\t{message?.ReplaceLineEndings(" ")}\n");
            firstFailure ??= $"{SubtestStatuses[Math.Clamp(status, 0, SubtestStatuses.Length - 1)]}: {name}: {message}";
        }

        var subtests = new SubtestCounts(passed, tests.Count);
        if (harness != 0)
        {
            var message = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
            var reason = $"harness {HarnessStatuses[Math.Clamp(harness, 0, HarnessStatuses.Length - 1)]}: {message ?? (errors.Count > 0 ? errors[0] : "")}";
            return new TestResult(Outcome.Fail, Trim(reason), subtests);
        }

        if (tests.Count == 0)
            return new TestResult(Outcome.Fail, "no subtests", subtests);
        if (firstFailure is not null)
            return new TestResult(Outcome.Fail, Trim($"{tests.Count - passed} of {tests.Count} failed; first {firstFailure}"), subtests);
        return new TestResult(Outcome.Pass, null, subtests);
    }

    // Reads the harness's result line out of what the scripts log, and keeps the script errors for the reason a test gives when it never reports.
    private sealed class CollectingLogger(List<string> lines, List<string> errors) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var text = formatter(state, exception);
            if (eventId == ScriptLogEvents.ScriptFailed || eventId == ScriptLogEvents.ScriptLoadFailed)
                lock (errors) errors.Add(text);
            else if (text.StartsWith(ResultPrefix, StringComparison.Ordinal))
                lock (lines) lines.Add(text[ResultPrefix.Length..]);
        }
    }

    private static string Trim(string text)
    {
        text = text.ReplaceLineEndings(" ");
        return text.Length > 220 ? text[..220] : text;
    }

    /// <summary>The page the WPT server generates for the window global of a <c>.any.js</c> / <c>.window.js</c> file; null when the file asks for no window.</summary>
    internal static string? Wrap(string wptPath, string script) => WptServer.GenerateTestPage(wptPath, script);

    [GeneratedRegex(@"[\w./-]+\.py\b", RegexOptions.CultureInvariant)]
    private static partial Regex PythonReference();

    // The first .py file a test names that exists in the checkout but that the server has no handler for, or null.
    private string? UnportedHandler(string wptPath, string text)
    {
        var directory = ReftestParser.DirectoryOf(wptPath);
        foreach (Match match in PythonReference().Matches(text))
        {
            var resolved = ReftestParser.Resolve(directory, match.Value);
            if (File.Exists(Full(resolved)) && WptHandlers.Find(resolved) is null)
                return resolved;
        }

        return null;
    }

    private string Full(string wptPath) => Path.Combine(wptRoot, wptPath.Replace('/', Path.DirectorySeparatorChar));

    private static async ValueTask<RNetworkResponse?> LoadAsync(WptServer server, Uri uri)
    {
        if (uri.IsAbsoluteUri && uri.Scheme == "data")
            return await HostResourceLoader.LoadAsync(uri);

        return await server.LoadAsync(uri);
    }
}
