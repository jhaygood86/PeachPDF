namespace PeachBrackets.WptRunner.Tests;

/// <summary>
/// The testharness.js tier: a page's subtests are read back through the report script, a .any.js file is wrapped in its window page, and the
/// files a module holds are found. The harness here is a few lines of script standing in for the real testharness.js, so no checkout is needed.
/// </summary>
public sealed class TestharnessEvaluatorTests : IDisposable
{
    // The same entry points as the real harness, with the status codes testharness.js uses (0 pass, 1 fail; harness 0 OK, 1 error).
    private const string FakeHarness = """
        var callbacks = [], results = [];
        function setup() {}
        function add_completion_callback(f) { callbacks.push(f); }
        function test(fn, name) {
            var t = { name: name, status: 0, message: null };
            try { fn(); } catch (e) { t.status = 1; t.message = String(e); }
            results.push(t);
        }
        function done(status, message) { callbacks.forEach(function (f) { f(results, { status: status || 0, message: message || null }); }); }
        """;

    private readonly string _root = Directory.CreateTempSubdirectory("wpt-th-").FullName;

    public TestharnessEvaluatorTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "resources"));
        Directory.CreateDirectory(Path.Combine(_root, "dom"));
        File.WriteAllText(Path.Combine(_root, "resources", "testharness.js"), FakeHarness);
        File.WriteAllText(Path.Combine(_root, "resources", "testharnessreport.js"), "// replaced by the runner");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(string path, string content)
    {
        var full = Path.Combine(_root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private const string Page = "<!doctype html><title>t</title><script src=\"/resources/testharness.js\"></script><script src=\"/resources/testharnessreport.js\"></script>";

    private Task<TestResult> RunAsync(string path) => new TestharnessEvaluator(_root).EvaluateAsync(path);

    [Fact]
    public async Task APageWhoseSubtestsAllPass_Passes()
    {
        Write("dom/pass.html", Page + "<script>test(function () {}, 'a'); test(function () {}, 'b'); done();</script>");

        var result = await RunAsync("dom/pass.html");

        Assert.Equal(Outcome.Pass, result.Outcome);
        Assert.Equal(new SubtestCounts(2, 2), result.Subtests);
    }

    [Fact]
    public async Task AFailingSubtest_FailsThePageAndIsNamedInTheReason()
    {
        Write("dom/fail.html", Page + "<script>test(function () {}, 'ok'); test(function () { throw new Error('boom'); }, 'bad one'); done();</script>");

        var result = await RunAsync("dom/fail.html");

        Assert.Equal(Outcome.Fail, result.Outcome);
        Assert.Equal(new SubtestCounts(1, 2), result.Subtests);
        Assert.Contains("1 of 2 failed", result.Reason);
        Assert.Contains("bad one", result.Reason);
        Assert.Contains("boom", result.Reason);
    }

    [Fact]
    public async Task AHarnessError_FailsEvenWhenEverySubtestPassed()
    {
        Write("dom/error.html", Page + "<script>test(function () {}, 'a'); done(1, 'harness broke');</script>");

        var result = await RunAsync("dom/error.html");

        Assert.Equal(Outcome.Fail, result.Outcome);
        Assert.Contains("harness ERROR", result.Reason);
        Assert.Contains("harness broke", result.Reason);
    }

    [Fact]
    public async Task APageWithNoSubtests_Fails()
    {
        Write("dom/empty.html", Page + "<script>done();</script>");

        var result = await RunAsync("dom/empty.html");

        Assert.Equal(Outcome.Fail, result.Outcome);
        Assert.Equal("no subtests", result.Reason);
    }

    [Fact]
    public async Task APageThatNeverReports_FailsWithTheScriptError()
    {
        Write("dom/silent.html", Page + "<script>nope.nothing();</script>");

        var result = await RunAsync("dom/silent.html");

        Assert.Equal(Outcome.Fail, result.Outcome);
        Assert.Contains("no result", result.Reason);
        Assert.Contains("nope", result.Reason);
        Assert.Null(result.Subtests);
    }

    [Fact]
    public async Task AnAnyJsFile_IsRunInTheWindowPageTheServerWouldMake()
    {
        Write("dom/helper.js", "function helper() { return 7; }");
        Write("dom/thing.any.js", "// META: script=helper.js\ntest(function () { if (helper() !== 7) throw new Error('no helper'); }, 'uses the META script');");

        var result = await RunAsync("dom/thing.any.js");

        Assert.Equal(Outcome.Pass, result.Outcome);
    }

    [Fact]
    public async Task APageFindsTestdriverWithoutAFile()
    {
        Write("dom/driver.html", Page + "<script src=\"/resources/testdriver-vendor.js\"></script><script>test(function () {}, 'a'); done();</script>");

        Assert.Equal(Outcome.Pass, (await RunAsync("dom/driver.html")).Outcome);
    }

    [Theory]
    [InlineData("dom/a.sub.html", "needs server-side substitution")]
    [InlineData("dom/a.worker.js", "worker global")]
    [InlineData("dom/a.serviceworker.js", "worker global")]
    public async Task WhatNeedsAServerOrAWorker_IsSkippedWithItsReason(string path, string reason)
    {
        var result = await RunAsync(path);

        Assert.Equal(Outcome.Skipped, result.Outcome);
        Assert.Equal(reason, result.Reason);
    }

    [Fact]
    public async Task AnAnyJsFileForWorkersOnly_IsSkipped()
    {
        Write("dom/workers.any.js", "// META: global=dedicatedworker\ntest(function () {}, 'x');");

        var result = await RunAsync("dom/workers.any.js");

        Assert.Equal(Outcome.Skipped, result.Outcome);
        Assert.Equal("no window global", result.Reason);
    }

    [Fact]
    public void TheWrapper_LoadsTheHarnessThenEachMetaScriptThenTheTest()
    {
        var page = TestharnessEvaluator.Wrap("dom/x.any.js", "// META: script=/resources/a.js\n// META: global=window,worker\n// META: script=b.js\n")!;

        var order = new[] { "src=\"/resources/testharness.js\"", "src=\"/resources/testharnessreport.js\"", "src=\"/resources/a.js\"", "src=\"b.js\"", "src=\"x.any.js\"", "done();" };
        var last = -1;
        foreach (var item in order)
        {
            var at = page.IndexOf(item, StringComparison.Ordinal);
            Assert.True(at > last, $"{item} should come after the one before it");
            last = at;
        }

        Assert.Null(TestharnessEvaluator.Wrap("dom/x.any.js", "// META: global=worker\n"));
        Assert.NotNull(TestharnessEvaluator.Wrap("dom/x.window.js", "// META: global=worker\n"));
    }

    [Fact]
    public void Discovery_FindsTestharnessPagesAndScriptFilesButNotReftestsOrSupportFiles()
    {
        Write("dom/nodes/one.html", Page + "<script>test(function () {}, 'a');</script>");
        Write("dom/nodes/two.any.js", "test(function () {}, 'a');");
        Write("dom/nodes/three.window.js", "test(function () {}, 'a');");
        Write("dom/nodes/plain.html", "<!doctype html><p>no harness");
        Write("dom/nodes/helper.js", "function f() {}");
        Write("dom/nodes/resources/support.html", Page);
        Write("dom/crashtests/crash.html", Page);
        Write("dom/single.html", Page);

        var found = Modules.DiscoverTestharness(_root, new WptModule("m", ["dom/nodes", "dom/single.html", "dom/missing"]));

        Assert.Equal(["dom/nodes/one.html", "dom/nodes/three.window.js", "dom/nodes/two.any.js", "dom/single.html"], found);
    }
}
