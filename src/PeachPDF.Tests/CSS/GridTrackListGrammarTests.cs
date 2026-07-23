namespace PeachPDF.Tests.CSS
{
    using PeachPDF.CSS;
    using PeachPDF.Html.Core.Parse;
    using System.Linq;
    using Xunit;

    /// <summary>
    /// Tests for the shared <see cref="GridTrackListGrammar"/> — the grid <c>&lt;track-list&gt;</c>
    /// (<c>grid-template-columns</c>/<c>-rows</c>) and <c>&lt;track-size&gt;+</c> (<c>grid-auto-columns</c>/
    /// <c>-rows</c>) value grammars.
    /// </summary>
    public class GridTrackListGrammarTests : CssConstructionFunctions
    {
        private static GridTemplate Parse(string value) =>
            GridTrackListGrammar.TryParse(CssValueParser.GetCssTokens(value));

        [Theory]
        [InlineData("100px")]
        [InlineData("100px 200px")]
        [InlineData("1fr 2fr")]
        [InlineData("25% 75%")]
        [InlineData("auto")]
        [InlineData("auto 1fr auto")]
        [InlineData("min-content max-content")]
        [InlineData("minmax(100px, 1fr)")]
        [InlineData("minmax(min-content, max-content)")]
        [InlineData("fit-content(200px)")]
        [InlineData("repeat(3, 100px)")]
        [InlineData("repeat(2, 1fr 2fr)")]
        [InlineData("repeat(auto-fill, minmax(200px, 1fr))")]
        [InlineData("repeat(auto-fit, 100px)")]
        [InlineData("100px repeat(2, 1fr) 100px")]
        public void ValidTrackLists_Parse(string value)
        {
            Assert.NotNull(Parse(value));
        }

        [Theory]
        [InlineData("")]
        [InlineData("none")]                       // 'none' is accepted by the property, not the grammar
        [InlineData("banana")]
        [InlineData("minmax(1fr, 2fr)")]           // a flex min is invalid in minmax()
        [InlineData("minmax(100px)")]              // minmax needs two args
        [InlineData("repeat(0, 100px)")]           // repeat count must be >= 1
        [InlineData("repeat(auto-fill)")]          // repeat needs a track body
        [InlineData("-1fr")]                        // negative flex
        [InlineData("repeat(2, [x] 1fr)")]          // named lines inside repeat() are out of v1 scope
        [InlineData("[unclosed 100px")]             // an unclosed [ is invalid
        public void InvalidTrackLists_ReturnNull(string value)
        {
            Assert.Null(Parse(value));
        }

        [Theory]
        [InlineData("[sidebar-start] 200px [sidebar-end] 1fr")]
        [InlineData("[a b] 100px [c]")]
        [InlineData("[start] repeat(3, 100px) [end]")]
        public void NamedLines_Parse(string value)
        {
            Assert.NotNull(Parse(value));
        }

        [Fact]
        public void NamedLines_RecordSortedLineNumbers()
        {
            // [a] 100px [b] 100px [a] — 'a' labels lines 1 and 3, 'b' labels line 2.
            var template = Parse("[a] 100px [b] 100px [a]");
            Assert.NotNull(template);
            Assert.Equal(new[] { 1, 3 }, template.LineNames["a"]);
            Assert.Equal(new[] { 2 }, template.LineNames["b"]);
        }

        [Fact]
        public void RepeatFixed_ExpandsInline()
        {
            var template = Parse("repeat(3, 100px)");
            Assert.NotNull(template);
            Assert.Equal(3, template.Tracks.Count);
            Assert.All(template.Tracks, t => Assert.Equal(GridTrackKind.Length, t.Kind));
        }

        [Fact]
        public void Fr_ParsesAsFlexFactor()
        {
            var template = Parse("1fr 2fr");
            Assert.NotNull(template);
            Assert.Equal(GridTrackKind.Flex, template.Tracks[0].Kind);
            Assert.Equal(1.0, template.Tracks[0].Flex, 5);
            Assert.Equal(2.0, template.Tracks[1].Flex, 5);
        }

        [Fact]
        public void Minmax_CapturesBothBreadths()
        {
            var template = Parse("minmax(100px, 1fr)");
            Assert.NotNull(template);
            var track = Assert.Single(template.Tracks);
            Assert.Equal(GridTrackKind.Minmax, track.Kind);
            Assert.Equal(GridTrackKind.Length, track.Min.Kind);
            Assert.Equal(GridTrackKind.Flex, track.Max.Kind);
        }

        [Fact]
        public void AutoRepeat_RecordedWithKindAndInsertIndex()
        {
            var template = Parse("100px repeat(auto-fill, 50px) 100px");
            Assert.NotNull(template);
            Assert.Equal(GridAutoRepeatKind.AutoFill, template.AutoRepeat);
            Assert.Equal(2, template.Tracks.Count);           // the two fixed 100px tracks
            Assert.Equal(1, template.AutoRepeatInsertIndex);  // between them
            var repeated = Assert.Single(template.AutoRepeatTracks);
            Assert.Equal(GridTrackKind.Length, repeated.Kind);
        }

        [Fact]
        public void TwoAutoRepeats_AreRejected()
        {
            Assert.Null(Parse("repeat(auto-fill, 50px) repeat(auto-fit, 50px)"));
        }

        [Theory]
        [InlineData("100px", 1)]
        [InlineData("100px 200px auto", 3)]
        public void TrackSizeList_ParsesForAutoColumns(string value, int expected)
        {
            var list = GridTrackListGrammar.TryParseTrackSizeList(CssValueParser.GetCssTokens(value));
            Assert.NotNull(list);
            Assert.Equal(expected, list.Count);
        }

        [Theory]
        [InlineData("repeat(2, 100px)")]  // no repeat() allowed in a track-size list
        [InlineData("")]
        [InlineData("banana")]
        public void TrackSizeList_RejectsInvalid(string value)
        {
            Assert.Null(GridTrackListGrammar.TryParseTrackSizeList(CssValueParser.GetCssTokens(value)));
        }
    }
}
