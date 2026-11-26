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

    [Theory]
    [InlineData("(aspect-ratio: 4/3)", true)]
    [InlineData("(min-aspect-ratio: 1)", true)]
    [InlineData("(max-aspect-ratio: 1/1)", false)]
    [InlineData("(resolution: 1dppx)", true)]
    [InlineData("(min-resolution: 96dpi)", true)]
    [InlineData("(min-resolution: 2x)", false)]
    [InlineData("(min-resolution: 1dpcm)", true)]
    [InlineData("(color)", true)]
    [InlineData("(min-color: 4)", true)]
    [InlineData("(monochrome)", false)]
    [InlineData("(orientation: portrait)", false)]
    [InlineData("(orientation: sideways)", false)]
    [InlineData("((min-width: 700px) and (max-width: 900px))", true)]
    [InlineData("(min-width: 700px) or (max-width: 100px)", true)]
    [InlineData("(min-width: 900px) or (max-width: 100px)", false)]
    [InlineData("not ((min-width: 900px))", true)]
    [InlineData("(width >= 700px)", true)]
    [InlineData("(width < 700px)", false)]
    [InlineData("(width = 800px)", true)]
    [InlineData("(min-width)", true)]
    [InlineData("(foo: bar)", false)]
    [InlineData("(min-width: 1px) and (max-width: 2px) or (min-width: 1px)", false)]
    [InlineData("only screen", true)]
    [InlineData("tv", false)]
    [InlineData("screen foo", false)]
    [InlineData("all and (min-width: 9000px)", false)]
    [InlineData("not print", true)]
    [InlineData("unknown(1) , (min-width: 1px)", true)]
    public void MoreMediaQueries_AreEvaluatedAgainstTheViewport(string media, bool expected) => Assert.Equal(expected, ImageSources.MediaMatches(media, Viewport));

    [Theory]
    [InlineData("calc(100vw / 2)", 400)]
    [InlineData("calc(2 * 10px)", 20)]
    [InlineData("calc(10px * 2)", 20)]
    [InlineData("calc((10px + 5px) * 2)", 30)]
    [InlineData("calc(10px - 4px)", 6)]
    [InlineData("max(10px, 5px)", 10)]
    [InlineData("1e1px", 10)]
    [InlineData("calc(1px + 2px) , 9px", 3)]
    public void MoreSizesLengths_AreResolved(string sizes, double expected) =>
        Assert.Equal(expected, ImageSources.SourceSize(sizes, Viewport, out _));
}
