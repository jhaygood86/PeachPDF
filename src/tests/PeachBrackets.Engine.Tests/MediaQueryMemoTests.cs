using PeachBrackets.Css;
using PeachBrackets.Html;

namespace PeachBrackets.Engine.Tests;

/// <summary>
/// The cascade asks whether a rule's <c>@media</c> chain matches once per candidate rule per box, so the answer is remembered; these check that
/// what is remembered never outlives the context it was worked out for.
/// </summary>
public class MediaQueryMemoTests
{
    private static MediaList[] Chain(params string[] queries)
    {
        // One media list per nesting level, as the rule index keeps them: read back from the @media rules of a parsed style sheet.
        var parser = new StylesheetParser();
        return queries.Select(q => ((MediaRule)parser.Parse("@media " + q + " { a { color: red } }").Rules[0]).Media).ToArray();
    }

    private static MediaQueryContext Context(double widthPt, string mediaType = "screen") =>
        new(mediaType, widthPt, 800, 96d, ColorSchemePreference.Light);

    [Fact]
    public void TheAnswerFollowsTheContext_WhenTheSameChainIsAskedOfDifferentOnes()
    {
        var chain = Chain("(min-width: 600px)");

        // Alternating contexts must never return the answer remembered for the other one.
        for (var i = 0; i < 4; i++)
        {
            Assert.False(MediaQueryMatcher.Matches(chain, Context(300)));
            Assert.True(MediaQueryMatcher.Matches(chain, Context(900)));
        }
    }

    [Fact]
    public void ARepeatedQuestion_GivesTheSameAnswer()
    {
        var chain = Chain("screen and (min-width: 600px)");
        var wide = Context(900);

        var first = MediaQueryMatcher.Matches(chain, wide);
        for (var i = 0; i < 5; i++)
            Assert.Equal(first, MediaQueryMatcher.Matches(chain, wide));

        Assert.True(first);
    }

    [Fact]
    public void TheMediaType_IsPartOfTheContext()
    {
        var chain = Chain("print");

        Assert.False(MediaQueryMatcher.Matches(chain, Context(900, "screen")));
        Assert.True(MediaQueryMatcher.Matches(chain, Context(900, "print")));
        Assert.False(MediaQueryMatcher.Matches(chain, Context(900, "screen")));
    }

    [Fact]
    public void EachLevelOfANestedChain_MustMatch_AndADifferentChainIsAnsweredOnItsOwn()
    {
        var both = Chain("(min-width: 600px)", "(max-width: 1000px)");
        var wideOnly = Chain("(min-width: 600px)");

        // The context is in points and the query in CSS pixels (600px = 450pt, 1000px = 750pt).
        Assert.True(MediaQueryMatcher.Matches(wideOnly, Context(900)));
        Assert.False(MediaQueryMatcher.Matches(both, Context(900)));
        Assert.True(MediaQueryMatcher.Matches(both, Context(600)));
    }

    [Fact]
    public void ARuleInNoMediaBlock_AlwaysMatches()
    {
        Assert.True(MediaQueryMatcher.Matches(null, Context(300)));
    }
}
