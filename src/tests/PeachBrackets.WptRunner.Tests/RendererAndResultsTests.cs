namespace PeachBrackets.WptRunner.Tests;

[Collection("Console")]
public sealed class RendererAndResultsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("wpt-render-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData("a.css", "text/css")]
    [InlineData("a.png", "image/png")]
    [InlineData("a.JPG", "image/jpeg")]
    [InlineData("a.jpeg", "image/jpeg")]
    [InlineData("a.gif", "image/gif")]
    [InlineData("a.svg", "image/svg+xml")]
    [InlineData("a.webp", "image/webp")]
    [InlineData("a.ttf", "font/ttf")]
    [InlineData("a.otf", "font/otf")]
    [InlineData("a.woff", "font/woff")]
    [InlineData("a.woff2", "font/woff2")]
    [InlineData("a.xht", "text/html")]
    [InlineData("a.htm", "text/html")]
    [InlineData("a.bin", "application/octet-stream")]
    public void AResourcesContentTypeFollowsItsExtension(string name, string expected) =>
        Assert.Equal(expected, WptRenderer.ContentType(name));

    [Fact]
    public async Task AStylesheetFromAnotherDirectoryIsLoaded_AndMissingOrOutsideOnesAreRefused()
    {
        Directory.CreateDirectory(Path.Combine(_root, "support"));
        File.WriteAllText(Path.Combine(_root, "support", "s.css"), "div{width:40px;height:20px;background:green}");
        File.WriteAllText(Path.Combine(_root, "t.html"),
            "<link rel=match href=r.html><link rel=stylesheet href=support/s.css><link rel=stylesheet href=support/missing.css>" +
            "<link rel=stylesheet href=../outside.css><style>html,body{margin:0}</style><div></div>");
        File.WriteAllText(Path.Combine(_root, "r.html"),
            "<style>html,body{margin:0}</style><div style=\"width:40px;height:20px;background:green\"></div>");

        var result = await new ReftestEvaluator(new WptRenderer(_root)).EvaluateAsync("t.html");

        Assert.Equal(Outcome.Pass, result.Outcome);
    }

    [Fact]
    public async Task AFailureTheEngineRaises_IsReportedAsAnError_AndTracedWhenAsked()
    {
        var trace = new StringWriter();
        var previous = Console.Error;
        Environment.SetEnvironmentVariable("PEACHBRACKETS_WPT_TRACE", "1");
        Console.SetError(trace);
        try
        {
            var result = await new ReftestEvaluator(new WptRenderer(_root)).EvaluateAsync("does-not-exist.html");

            Assert.Equal(Outcome.Fail, result.Outcome);
            Assert.StartsWith("error:", result.Reason);
            Assert.Contains("does-not-exist.html", trace.ToString());
        }
        finally
        {
            Console.SetError(previous);
            Environment.SetEnvironmentVariable("PEACHBRACKETS_WPT_TRACE", null);
        }
    }

    [Fact]
    public void AnErrorReason_IsCutAt120Characters_InTheSummary()
    {
        var results = new Dictionary<string, TestResult>
        {
            ["a.html"] = new(Outcome.Fail, "error: " + new string('x', 200)),
        };

        var module = ModuleResult.From("c", "m", 1, 1, results);
        var summary = Path.Combine(_root, "out");
        ResultsWriter.Write(summary, module);
        ResultsWriter.WriteSummary(summary, "c");

        var text = File.ReadAllText(Directory.GetFiles(summary, "summary.md", SearchOption.AllDirectories)[0]);
        Assert.Contains(new string('x', 100), text);
        Assert.DoesNotContain(new string('x', 150), text);
    }
}
