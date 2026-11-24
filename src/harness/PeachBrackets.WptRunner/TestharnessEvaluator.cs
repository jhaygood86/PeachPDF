using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PeachBrackets;
using PeachBrackets.Scripting;
using PeachDrawing;
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
    private const string Origin = "http://wpt.test";
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

        string source;
        if (wptPath.EndsWith(".js", StringComparison.Ordinal))
        {
            var wrapped = Wrap(wptPath, File.ReadAllText(Full(wptPath)));
            if (wrapped is null)
                return new TestResult(Outcome.Skipped, "no window global");
            source = WptRenderer.Prepare(wptPath, wrapped);
        }
        else
        {
            source = WptRenderer.Prepare(wptPath, File.ReadAllText(Full(wptPath)));
        }

        var lines = new List<string>();
        var errors = new List<string>();
        var context = new RasterRenderContext { ResourceLoader = LoadAsync };
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
                Console = (_, text) =>
                {
                    if (text.StartsWith(ResultPrefix, StringComparison.Ordinal))
                        lock (lines) lines.Add(text[ResultPrefix.Length..]);
                },
                ScriptError = message => { lock (errors) errors.Add(message); },
            }),
            MeasuringCanvasFactory = () => context.CreateCanvas(1, 1),
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

    private static string Trim(string text)
    {
        text = text.ReplaceLineEndings(" ");
        return text.Length > 220 ? text[..220] : text;
    }

    /// <summary>The page the WPT server generates for the window global of a <c>.any.js</c> / <c>.window.js</c> file; null when the file asks for no window.</summary>
    internal static string? Wrap(string wptPath, string script)
    {
        var scripts = new List<string>();
        string[]? globals = null;
        foreach (Match m in MetaLine().Matches(script))
        {
            var key = m.Groups[1].Value;
            var value = m.Groups[2].Value.Trim();
            if (key == "script")
                scripts.Add(value);
            else if (key == "global")
                globals = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        }

        // With no `global` a .any.js runs in a window as well as the workers; "worker" or "dedicatedworker" alone has no window.
        if (wptPath.EndsWith(".any.js", StringComparison.Ordinal) && globals is not null && !globals.Contains("window"))
            return null;

        var name = wptPath[(wptPath.LastIndexOf('/') + 1)..];
        var page = new StringBuilder();
        page.Append("<!doctype html>\n<meta charset=utf-8>\n<title>").Append(System.Net.WebUtility.HtmlEncode(name)).Append("</title>\n");
        page.Append("<script src=\"/resources/testharness.js\"></script>\n<script src=\"/resources/testharnessreport.js\"></script>\n<div id=log></div>\n");
        foreach (var s in scripts)
            page.Append("<script src=\"").Append(System.Net.WebUtility.HtmlEncode(s)).Append("\"></script>\n");
        page.Append("<script src=\"").Append(System.Net.WebUtility.HtmlEncode(name)).Append("\"></script>\n<script>done();</script>\n");
        return page.ToString();
    }

    private string Full(string wptPath) => Path.Combine(wptRoot, wptPath.Replace('/', Path.DirectorySeparatorChar));

    private async ValueTask<RNetworkResponse?> LoadAsync(Uri uri)
    {
        if (uri.IsAbsoluteUri && uri.Scheme == "data")
            return await HostResourceLoader.LoadAsync(uri);

        if (!uri.IsAbsoluteUri || !uri.AbsoluteUri.StartsWith(Origin + "/", StringComparison.Ordinal))
            return null;

        var relative = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        if (relative == "resources/testharnessreport.js")
            return new RNetworkResponse(new MemoryStream(Encoding.UTF8.GetBytes(ReportScript)), new Dictionary<string, string[]> { ["Content-Type"] = ["text/javascript"] });

        // The vendor file wires testdriver.js to a browser's automation; there is none here, so it is empty and the actions fail when used.
        if (relative == "resources/testdriver-vendor.js")
            return new RNetworkResponse(new MemoryStream(), new Dictionary<string, string[]> { ["Content-Type"] = ["text/javascript"] });

        var path = Path.GetFullPath(Full(relative));
        if (!path.StartsWith(Path.GetFullPath(wptRoot), StringComparison.Ordinal) || !File.Exists(path))
            return null;

        var bytes = await File.ReadAllBytesAsync(path);
        var type = Path.GetExtension(path).Equals(".js", StringComparison.OrdinalIgnoreCase) ? "text/javascript" : WptRenderer.ContentType(path);
        return new RNetworkResponse(new MemoryStream(bytes), new Dictionary<string, string[]> { ["Content-Type"] = [type] });
    }
}
