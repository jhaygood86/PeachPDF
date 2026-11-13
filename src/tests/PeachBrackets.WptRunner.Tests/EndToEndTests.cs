namespace PeachBrackets.WptRunner.Tests;

/// <summary>Renders tiny fixture pairs through the real PeachBrackets pipeline: a known pass, a known fail and a blank page.</summary>
public sealed class EndToEndTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("wpt-e2e-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(string name, string html) => File.WriteAllText(Path.Combine(_root, name), html);

    private Task<TestResult> RunAsync(string name) => new ReftestEvaluator(new WptRenderer(_root)).EvaluateAsync(name);

    private const string Box = "<!DOCTYPE html><style>html,body{margin:0}</style><div style=\"width:100px;height:50px;background:green\"></div>";

    [Fact]
    public async Task TwoDifferentWaysToDrawTheSameBox_Match()
    {
        Write("t.html", "<link rel=match href=r.html><!DOCTYPE html><style>html,body{margin:0} div{width:100px;height:50px;background:green}</style><div></div>");
        Write("r.html", Box);

        Assert.Equal(Outcome.Pass, (await RunAsync("t.html")).Outcome);
    }

    [Fact]
    public async Task ABoxThatIsOnePixelTooWide_DoesNotMatch()
    {
        Write("t.html", "<link rel=match href=r.html><!DOCTYPE html><style>html,body{margin:0}</style><div style=\"width:101px;height:50px;background:green\"></div>");
        Write("r.html", Box);

        var result = await RunAsync("t.html");

        Assert.Equal(Outcome.Fail, result.Outcome);
        Assert.Contains("differs (max 255, 50 px)", result.Reason);
    }

    [Fact]
    public async Task TwoEmptyPages_AreBlank_NotAPass()
    {
        Write("t.html", "<link rel=match href=r.html><!DOCTYPE html><p></p>");
        Write("r.html", "<!DOCTYPE html>");

        Assert.Equal(Outcome.Blank, (await RunAsync("t.html")).Outcome);
    }

    [Fact]
    public async Task ARelativeStylesheet_IsLoadedFromTheCheckout()
    {
        Write("s.css", "div{width:100px;height:50px;background:green}");
        Write("t.html", "<link rel=match href=r.html><!DOCTYPE html><style>html,body{margin:0}</style><link rel=stylesheet href=\"s.css\"><div></div>");
        Write("r.html", Box);

        Assert.Equal(Outcome.Pass, (await RunAsync("t.html")).Outcome);
    }

    [Fact]
    public async Task TheViewport_IsEightHundredBySixHundredPixels()
    {
        Write("t.html", "<!DOCTYPE html>");

        var image = await new WptRenderer(_root).RenderAsync("t.html");

        Assert.Equal((800, 600), (image.Width, image.Height));
    }
}
