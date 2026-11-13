namespace PeachBrackets.WptRunner.Tests;

public class ReftestParserTests
{
    [Fact]
    public void Links_AreResolvedAgainstTheDirectoryOfTheFile()
    {
        var info = ReftestParser.Parse("css/css-a/b/test.html", """
            <link rel="match" href="ref.html"><link rel=mismatch href='../other/no.html'><link rel="help" href="x.html">
            """);

        Assert.Collection(info.Links,
            l => Assert.Equal(new ReftestLink(ReftestRelation.Match, "css/css-a/b/ref.html"), l),
            l => Assert.Equal(new ReftestLink(ReftestRelation.Mismatch, "css/css-a/other/no.html"), l));
    }

    [Fact]
    public void ALeadingSlash_IsTheWptRoot_AndAFragmentIsDropped()
    {
        var info = ReftestParser.Parse("css/x/t.html", "<link rel=match href=\"/css/y/ref.html#frag\">");

        Assert.Equal("css/y/ref.html", Assert.Single(info.Links).Path);
    }

    [Theory]
    [InlineData("<script src=a.js></script>", "script-dependent")]
    [InlineData("<script src=\"/resources/testharness.js\"></script>", "testharness.js test")]
    [InlineData("<html class=\"reftest-wait\">", "reftest-wait (needs script)")]
    public void ATestThatNeedsScript_IsSkippedWithAReason(string source, string reason)
    {
        var info = ReftestParser.Parse("css/t.html", "<link rel=match href=r.html>" + source);

        Assert.Equal(reason, info.SkipReason);
    }

    [Fact]
    public void ATestWithoutScript_HasNoSkipReason()
    {
        Assert.Null(ReftestParser.Parse("css/t.html", "<link rel=match href=r.html><p>x").SkipReason);
    }

    [Theory]
    [InlineData("maxDifference=0-5;totalPixels=10", 0, 5, 10, 10)]
    [InlineData("maxDifference=3;totalPixels=2-8", 3, 3, 2, 8)]
    public void Fuzzy_ASingleNumberIsExact_AndARangeIsInclusive(string content, int maxMin, int maxMax, int pxMin, int pxMax)
    {
        var info = ReftestParser.Parse("css/t.html", $"<link rel=match href=r.html><meta name=fuzzy content=\"{content}\">");

        Assert.Equal(new Fuzzy(maxMin, maxMax, pxMin, pxMax), info.Fuzzies[string.Empty]);
    }

    [Fact]
    public void Fuzzy_APrefixLimitsTheAllowanceToThatReference()
    {
        var info = ReftestParser.Parse("css/d/t.html", "<meta name=fuzzy content=\"r.html:maxDifference=1-2;totalPixels=3-4\">");

        var fuzzy = Assert.Single(info.Fuzzies);
        Assert.Equal("css/d/r.html", fuzzy.Key);
        Assert.True(fuzzy.Value.Allows(2, 3));
        Assert.False(fuzzy.Value.Allows(3, 3));
        Assert.False(fuzzy.Value.Allows(1, 5));
    }

    [Fact]
    public void WithoutFuzzy_OnlyAnIdenticalImageIsAllowed()
    {
        Assert.True(Fuzzy.None.Allows(0, 0));
        Assert.False(Fuzzy.None.Allows(1, 1));
    }
}
