using System.Text.Json;

namespace PeachBrackets.WptRunner.Tests;

/// <summary>The command line, run in-process over a tiny stand-in for a WPT checkout (a passing and a failing reftest in <c>css/css-lists</c>).</summary>
[Collection("Console")]
public sealed class CliTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("wpt-cli-").FullName;

    public CliTests()
    {
        var dir = Path.Combine(_root, "css", "css-lists");
        Directory.CreateDirectory(dir);
        const string Green = "<!DOCTYPE html><style>html,body{margin:0}</style><div style=\"width:40px;height:20px;background:green\"></div>";
        File.WriteAllText(Path.Combine(dir, "pass.html"), "<link rel=match href=ref.html>" + Green);
        File.WriteAllText(Path.Combine(dir, "ref.html"), Green);
        File.WriteAllText(Path.Combine(dir, "fail.html"),
            "<link rel=match href=ref.html><!DOCTYPE html><style>html,body{margin:0}</style><div style=\"width:41px;height:20px;background:green\"></div>");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static async Task<(int Exit, string Out, string Err)> RunAsync(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var (previousOut, previousErr) = (Console.Out, Console.Error);
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            return (await Cli.RunAsync(args), stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousErr);
        }
    }

    [Fact]
    public async Task NoArguments_PrintsTheUsage_AndFails()
    {
        var (exit, output, _) = await RunAsync();

        Assert.Equal(2, exit);
        Assert.Contains("PeachBrackets WPT reftest runner", output);
    }

    [Fact]
    public async Task Help_PrintsTheUsage()
    {
        var (exit, output, _) = await RunAsync("--help");

        Assert.Equal(0, exit);
        Assert.Contains("PeachBrackets WPT reftest runner", output);
    }

    [Fact]
    public async Task AnUnknownCommand_Fails()
    {
        var (exit, _, err) = await RunAsync("frobnicate");

        Assert.Equal(2, exit);
        Assert.Contains("unknown command", err);
    }

    [Fact]
    public async Task RunWithoutAWptDirectory_Fails()
    {
        var (exit, _, err) = await RunAsync("run");

        Assert.Equal(2, exit);
        Assert.Contains("--wpt", err);
    }

    [Fact]
    public async Task RunWithAnUnknownModule_Fails()
    {
        var (exit, _, err) = await RunAsync("run", "--wpt", _root, "--module", "no-such-module");

        Assert.Equal(2, exit);
        Assert.Contains("No such module", err);
    }

    [Fact]
    public async Task RunReportsTheModulesCounts()
    {
        var (exit, output, _) = await RunAsync("run", "--wpt", _root, "--module", "css-lists", "--parallel", "1");

        Assert.Equal(0, exit);
        Assert.Contains("css-lists", output);
        Assert.Contains("pass     1", output);
        Assert.Contains("fail     1", output);
    }

    [Fact]
    public async Task RunWithAFilterAndASample_SelectsTheTests()
    {
        var (_, output, _) = await RunAsync("run", "--wpt", _root, "--module", "css-lists", "--filter", "pass", "--sample", "1");

        Assert.Contains("total     1", output);
    }

    [Fact]
    public async Task RunOfOneTest_ListsItsResult_AndDumpsTheImages()
    {
        var dump = Path.Combine(_root, "dump");

        var (_, output, _) = await RunAsync("run", "--wpt", _root, "--module", "css-lists", "--filter", "fail.html", "--dump", dump);

        Assert.Contains("css-lists", output);
        Assert.NotEmpty(Directory.GetFiles(dump, "*.png"));
    }

    [Fact]
    public async Task RunWritesTheResultsAndSummary_WhenAskedTo()
    {
        var outDir = Path.Combine(_root, "out");

        await RunAsync("run", "--wpt", _root, "--module", "css-lists", "--out", outDir);

        Assert.NotEmpty(Directory.GetFiles(outDir, "css-lists.json", SearchOption.AllDirectories));
        Assert.NotEmpty(Directory.GetFiles(outDir, "summary.md", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task AModuleBelowItsFloor_FailsTheRun()
    {
        var floors = Path.Combine(_root, "floors.json");
        File.WriteAllText(floors, JsonSerializer.Serialize(new { formatVersion = 1, wptCommit = "x", floors = new Dictionary<string, int> { ["css-lists"] = 5 } }));

        var (exit, _, err) = await RunAsync("run", "--wpt", _root, "--module", "css-lists", "--floors", floors);

        Assert.Equal(1, exit);
        Assert.Contains("REGRESSION", err);
    }

    [Fact]
    public async Task AModuleAboveItsFloor_SaysSo_AndUpdateFloorsRaisesIt()
    {
        var floors = Path.Combine(_root, "floors.json");
        File.WriteAllText(floors, JsonSerializer.Serialize(new { formatVersion = 1, wptCommit = "x", floors = new Dictionary<string, int> { ["css-lists"] = 0 } }));

        var (_, above, _) = await RunAsync("run", "--wpt", _root, "--module", "css-lists", "--floors", floors);
        var (exit, updated, _) = await RunAsync("run", "--wpt", _root, "--module", "css-lists", "--floors", floors, "--update-floors");

        Assert.Contains("above its floor", above);
        Assert.Equal(0, exit);
        Assert.Contains("floors written", updated);
        Assert.Equal(1, JsonDocument.Parse(File.ReadAllText(floors)).RootElement.GetProperty("floors").GetProperty("css-lists").GetInt32());
    }

    private string ExpectedFailures(params string[] paths)
    {
        var file = Path.Combine(_root, "expected.json");
        File.WriteAllText(file, JsonSerializer.Serialize(new { formatVersion = 1, tests = paths.ToDictionary(p => p, _ => "the reference cannot be matched") }));
        return file;
    }

    [Fact]
    public async Task AnExpectedFailureThatFails_IsFine()
    {
        var (exit, _, err) = await RunAsync("run", "--wpt", _root, "--module", "css-lists", "--expected-failures", ExpectedFailures("css/css-lists/fail.html"));

        Assert.Equal(0, exit);
        Assert.DoesNotContain("UNEXPECTED PASS", err);
    }

    [Fact]
    public async Task AnExpectedFailureThatPasses_FailsTheRun()
    {
        var (exit, _, err) = await RunAsync("run", "--wpt", _root, "--module", "css-lists", "--expected-failures", ExpectedFailures("css/css-lists/pass.html"));

        Assert.Equal(1, exit);
        Assert.Contains("UNEXPECTED PASS: css/css-lists/pass.html", err);
        Assert.Contains("the reference cannot be matched", err);
    }

    [Fact]
    public async Task AnExpectedFailureNoModuleRuns_IsStale_InAFullRun()
    {
        var (exit, _, err) = await RunAsync("run", "--wpt", _root, "--expected-failures", ExpectedFailures("css/css-lists/gone.html"));

        Assert.Equal(1, exit);
        Assert.Contains("STALE: css/css-lists/gone.html", err);
    }
}
