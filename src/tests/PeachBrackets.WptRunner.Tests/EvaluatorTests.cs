namespace PeachBrackets.WptRunner.Tests;

/// <summary>Drives the reftest rules with a fake renderer, so each rule is checked on its own and no checkout is needed.</summary>
public sealed class EvaluatorTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("wpt-eval-").FullName;
    private readonly Dictionary<string, RgbaImage> _images = [];

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class FakeRenderer(string root, Dictionary<string, RgbaImage> images) : IReftestRenderer
    {
        public string FullPath(string wptPath) => Path.Combine(root, wptPath);

        public Task<RgbaImage> RenderAsync(string wptPath) =>
            images.TryGetValue(wptPath, out var image) ? Task.FromResult(image) : throw new InvalidOperationException($"no image for {wptPath}", new FormatException("inner cause"));
    }

    private static RgbaImage Image(byte r, byte g = 0, byte b = 0)
    {
        var pixels = new byte[4 * 4 * 4];
        for (var i = 0; i < pixels.Length; i += 4)
            (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (r, g, b, 255);
        return new RgbaImage(4, 4, pixels);
    }

    private static readonly RgbaImage White = Image(255, 255, 255);

    private void Write(string path, string content, RgbaImage? image = null)
    {
        File.WriteAllText(FullPath(path), content);
        if (image is not null)
            _images[path] = image;
    }

    private string FullPath(string path) => Path.Combine(_root, path);

    private Task<TestResult> RunAsync(string path) => new ReftestEvaluator(new FakeRenderer(_root, _images)).EvaluateAsync(path);

    [Fact]
    public async Task AMatchingReference_Passes()
    {
        Write("t.html", "<link rel=match href=r.html>", Image(200));
        Write("r.html", "", Image(200));

        Assert.Equal(new TestResult(Outcome.Pass), await RunAsync("t.html"));
    }

    [Fact]
    public async Task ADifferentReference_FailsAndNamesTheDifference()
    {
        Write("t.html", "<link rel=match href=r.html>", Image(200));
        Write("r.html", "", Image(100));

        var result = await RunAsync("t.html");

        Assert.Equal(Outcome.Fail, result.Outcome);
        Assert.Equal("== r.html differs (max 100, 16 px)", result.Reason);
    }

    [Fact]
    public async Task AMatchThatDrewNothing_IsBlank_NeverAPass()
    {
        Write("t.html", "<link rel=match href=r.html>", White);
        Write("r.html", "", White);

        Assert.Equal(Outcome.Blank, (await RunAsync("t.html")).Outcome);
    }

    [Fact]
    public async Task AFuzzyAllowance_AcceptsASmallDifference()
    {
        Write("t.html", "<link rel=match href=r.html><meta name=fuzzy content=\"maxDifference=0-10;totalPixels=0-16\">", Image(200));
        Write("r.html", "", Image(195));

        Assert.Equal(Outcome.Pass, (await RunAsync("t.html")).Outcome);
    }

    [Fact]
    public async Task AnyOneOfSeveralMatchReferences_IsEnough()
    {
        Write("t.html", "<link rel=match href=a.html><link rel=match href=b.html>", Image(200));
        Write("a.html", "", Image(1));
        Write("b.html", "", Image(200));

        Assert.Equal(Outcome.Pass, (await RunAsync("t.html")).Outcome);
    }

    [Fact]
    public async Task AMismatchReference_MustDiffer()
    {
        Write("t.html", "<link rel=mismatch href=r.html>", Image(200));
        Write("r.html", "", Image(200));
        Assert.Equal(new TestResult(Outcome.Fail, "!= r.html identical"), await RunAsync("t.html"));

        _images["r.html"] = Image(10);
        Assert.Equal(Outcome.Pass, (await RunAsync("t.html")).Outcome);
    }

    [Fact]
    public async Task AReferenceChain_MustHoldAllTheWayDown()
    {
        Write("t.html", "<link rel=match href=r1.html>", Image(200));
        Write("r1.html", "<link rel=match href=r2.html>", Image(200));
        Write("r2.html", "", Image(50));

        Assert.Equal(Outcome.Fail, (await RunAsync("t.html")).Outcome);

        _images["r2.html"] = Image(200);
        Assert.Equal(Outcome.Pass, (await RunAsync("t.html")).Outcome);
    }

    [Fact]
    public async Task AReferenceThatLinksBackToTheTest_DoesNotLoopForever()
    {
        Write("t.html", "<link rel=match href=r.html>", Image(200));
        Write("r.html", "<link rel=match href=t.html>", Image(200));

        Assert.Equal(Outcome.Pass, (await RunAsync("t.html")).Outcome);
    }

    [Fact]
    public async Task ImagesOfDifferentSizes_FailWithTheSizes()
    {
        Write("t.html", "<link rel=match href=r.html>", Image(200));
        Write("r.html", "", new RgbaImage(2, 2, new byte[16]));

        var result = await RunAsync("t.html");

        Assert.Equal(Outcome.Fail, result.Outcome);
        Assert.Contains("size 4x4 vs 2x2", result.Reason);
    }

    [Theory]
    [InlineData("<script>1</script><link rel=match href=r.html>", "script-dependent")]
    [InlineData("<script src=/resources/testharness.js></script>", "testharness.js test")]
    [InlineData("<p>no reference", "no reference")]
    [InlineData("<link rel=match href=absent.html>", "reference file missing: absent.html")]
    public async Task ATestThatCannotRun_IsSkippedWithAReason(string source, string reason)
    {
        Write("t.html", source, Image(1));

        Assert.Equal(new TestResult(Outcome.Skipped, reason), await RunAsync("t.html"));
    }

    [Fact]
    public async Task AReferenceThatNeedsScript_SkipsTheTest()
    {
        Write("t.html", "<link rel=match href=r.html>", Image(1));
        Write("r.html", "<script>x</script>", Image(1));

        Assert.Equal(new TestResult(Outcome.Skipped, "script-dependent"), await RunAsync("t.html"));
    }

    [Fact]
    public async Task ARenderingError_FailsWithTheRootCause()
    {
        Write("t.html", "<link rel=match href=r.html>"); // no image registered: the fake renderer throws
        Write("r.html", "");

        var result = await RunAsync("t.html");

        Assert.Equal(Outcome.Fail, result.Outcome);
        Assert.Equal("error: InvalidOperationException: no image for t.html <- FormatException: inner cause", result.Reason);
    }

    [Fact]
    public async Task TheImagesCompared_CanBeDumpedAsPngs()
    {
        Write("t.html", "<link rel=match href=r.html>", Image(200));
        Write("r.html", "", Image(200));
        var dumps = Path.Combine(_root, "dumps");

        await new ReftestEvaluator(new FakeRenderer(_root, _images), dumps).EvaluateAsync("t.html");

        Assert.Equal(["r.html.png", "t.html.png"], Directory.GetFiles(dumps).Select(Path.GetFileName).Order().ToArray());
    }
}
