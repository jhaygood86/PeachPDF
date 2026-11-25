using PeachBrackets.Scripting.Internal;

namespace PeachBrackets.Scripting.Tests;

/// <summary>srcset and sizes as the HTML standard reads them, and the choice they make.</summary>
public class ImageSourcesTests
{
    private static readonly ImageViewport Viewport = new(800, 600);

    [Fact]
    public void ASrcsetIsSplitIntoCandidates_AndOnesWithBadDescriptorsAreDropped()
    {
        var candidates = ImageSources.ParseSrcset(" a.png 1x,b.png 2x ,c.png 300w, d.png 1x 2x, e.png 0w, f.png 100h, g.png, h.png 1.5x");

        Assert.Equal(["a.png", "b.png", "c.png", "g.png", "h.png"], candidates.Select(c => c.Url));
        Assert.Equal([1d, 2d, null, null, 1.5], candidates.Select(c => c.Density));
        Assert.Equal(300, candidates[2].Width);
    }

    [Theory]
    [InlineData("100px", 100)]
    [InlineData("(min-width: 600px) 300px, 100px", 300)]
    [InlineData("(min-width: 1000px) 300px, 100px", 100)]
    [InlineData("calc(50px + 25px)", 75)]
    [InlineData("min(1px, 100px)", 1)]
    [InlineData("50vw", 400)]
    [InlineData(@"1\p\x", 1)]
    [InlineData("/* c */ 7px /* c */", 7)]
    [InlineData("(),1px", 1)]
    [InlineData("not (min-width: 0) 100vw, 1px", 1)]
    [InlineData("(unknown-feature) 100vw, 2px", 2)]
    [InlineData("-5px, 3px", 3)]
    [InlineData("", 800)]
    public void ASizesListGivesTheFirstMatchingLength(string sizes, double expected) =>
        Assert.Equal(expected, ImageSources.SourceSize(sizes, Viewport, out _));

    [Fact]
    public void TheSmallestSharpEnoughCandidateIsChosen()
    {
        var candidates = ImageSources.ParseSrcset("small.png 400w, large.png 1200w");

        Assert.Equal("small.png", ImageSources.Select(candidates, null, 400, 1));
        Assert.Equal("large.png", ImageSources.Select(candidates, null, 400, 3));
        Assert.Equal("large.png", ImageSources.Select(candidates, null, 1200, 2));
        Assert.Equal("one.png", ImageSources.Select(ImageSources.ParseSrcset("two.png 2x"), "one.png", 800, 1));
        Assert.Null(ImageSources.Select([], null, 800, 1));
    }

    [Theory]
    [InlineData("print", false)]
    [InlineData("screen and (min-width: 700px)", true)]
    [InlineData("(max-width: 700px)", false)]
    [InlineData("not all", false)]
    [InlineData("(orientation: landscape)", true)]
    [InlineData("(width >= 800px)", true)]
    public void AMediaListMatchesTheViewport(string media, bool expected) => Assert.Equal(expected, ImageSources.MediaMatches(media, Viewport));
}
