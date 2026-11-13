namespace PeachBrackets.WptRunner.Tests;

public sealed class ResultsAndSourceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("wpt-res-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ModuleResult_CountsEachOutcome_ListsTestsInPathOrder_AndComputesTheRateOverRunnableTests()
    {
        var results = new Dictionary<string, TestResult>
        {
            ["b.html"] = new(Outcome.Pass),
            ["a.html"] = new(Outcome.Pass),
            ["c.html"] = new(Outcome.Fail, "== r differs"),
            ["d.html"] = new(Outcome.Skipped, "script-dependent"),
            ["e.html"] = new(Outcome.Blank),
        };

        var module = ModuleResult.From("abc", "m", discovered: 9, stride: 2, results);

        Assert.Equal(["a.html", "b.html"], module.Pass);
        Assert.Equal(5, module.Counts.Total);
        Assert.Equal((2, 1, 1, 1), (module.Counts.Pass, module.Counts.Fail, module.Counts.Blank, module.Counts.Skipped));
        Assert.Equal("0.5000", module.Rate); // 2 passes of 4 runnable: a blank test is runnable but never a pass
        Assert.Equal((9, 2), (module.Discovered, module.SampleStride));
    }

    [Fact]
    public void AModuleWithNothingRunnable_HasARateOfZero()
    {
        var module = ModuleResult.From("abc", "m", 1, 1, new Dictionary<string, TestResult> { ["a.html"] = new(Outcome.Skipped, "x") });

        Assert.Equal("0.0000", module.Rate);
    }

    [Fact]
    public void TheSummary_IsRebuiltFromEveryModuleFilePresent_AndGroupsLikeFailures()
    {
        ResultsWriter.Write(_root, ModuleResult.From("abc", "m1", 2, 1, new Dictionary<string, TestResult>
        {
            ["a.html"] = new(Outcome.Fail, "== r.html differs (max 5, 3 px)"),
            ["b.html"] = new(Outcome.Fail, "== s.html differs (max 9, 7 px)"),
        }));
        ResultsWriter.Write(_root, ModuleResult.From("abc", "m2", 1, 1, new Dictionary<string, TestResult> { ["c.html"] = new(Outcome.Pass) }));

        ResultsWriter.WriteSummary(_root, "abc");
        var summary = File.ReadAllText(Path.Combine(_root, "abc", "summary.md"));

        Assert.Contains("| m1 | 1 | 2 | 0 | 2 | 0 | 0 | 0.0000 |", summary);
        Assert.Contains("| m2 | 1 | 1 | 1 | 0 | 0 | 0 | 1.0000 |", summary);
        Assert.Contains("- 2 x == <ref> differs (pixels)", summary);
    }

    [Fact]
    public void TheFloorsFile_RoundTripsInOrdinalOrder()
    {
        var file = Path.Combine(_root, "floors.json");
        var floors = new Floors { WptCommit = "abc" };
        floors.Modules["z"] = 3;
        floors.Modules["a"] = 9;
        floors.Save(file);

        var loaded = Floors.Load(file);

        Assert.Equal(new[] { "a", "z" }, loaded.Modules.Keys);
        Assert.Equal(9, loaded.Modules["a"]);
        Assert.Empty(Floors.Load(Path.Combine(_root, "missing.json")).Modules);
    }

    [Fact]
    public void Prepare_SetsABase_AndLeavesReferencesRelativeForTheEngineToResolve()
    {
        var html = WptRenderer.Prepare("css/a/t.html",
            "<html><head><link rel=stylesheet href=\"/fonts/ahem.css\"><style>@import \"local.css\";</style></head><body>x</body></html>");

        Assert.Contains("<base href=\"http://wpt.test/css/a/\">", html);
        Assert.Contains("href=\"/fonts/ahem.css\"", html);
        Assert.Contains("@import \"local.css\"", html);
    }

    [Fact]
    public void Prepare_AddsAHead_WhenThereIsNone()
    {
        Assert.StartsWith("<head><base href=\"http://wpt.test/css/\"></head>", WptRenderer.Prepare("css/t.html", "<p>x"));
    }

    [Fact]
    public void Prepare_ReadsAnXhtmlFileAsHtml()
    {
        var html = WptRenderer.Prepare("css/t.xht", "<?xml version=\"1.0\"?><html><head><style><![CDATA[ p { color: red } ]]></style></head></html>");

        Assert.DoesNotContain("CDATA", html);
        Assert.DoesNotContain("<?xml", html);
        Assert.Contains("p { color: red }", html);
    }

    [Fact]
    public void Discover_FindsReftestsAndTestharnessFiles_ButNotSupportFiles()
    {
        Directory.CreateDirectory(Path.Combine(_root, "css", "m", "support"));
        File.WriteAllText(Path.Combine(_root, "css", "m", "a.html"), "<link rel=match href=r.html>");
        File.WriteAllText(Path.Combine(_root, "css", "m", "b.html"), "<script src=/resources/testharness.js></script>");
        File.WriteAllText(Path.Combine(_root, "css", "m", "r.html"), "<p>reference only");
        File.WriteAllText(Path.Combine(_root, "css", "m", "support", "s.html"), "<link rel=match href=r.html>");
        File.WriteAllText(Path.Combine(_root, "css", "m", "c.txt"), "<link rel=match href=r.html>");

        var found = Modules.Discover(_root, new WptModule("m", ["css/m", "css/absent"]));

        Assert.Equal(["css/m/a.html", "css/m/b.html"], found);
    }
}
