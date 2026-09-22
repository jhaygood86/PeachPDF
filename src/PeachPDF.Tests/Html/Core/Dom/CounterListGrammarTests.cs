using PeachPDF.Html.Core.Dom;
using Xunit;

namespace PeachPDF.Tests.Html.Core.Dom
{
    /// <summary>
    /// Unit tests for the one shared reader of the <c>[ &lt;counter-name&gt; &lt;integer&gt;? ]+ | none</c>
    /// grammar behind <c>counter-reset</c>, <c>counter-set</c> and <c>counter-increment</c>. The applying
    /// loops in <see cref="CssCounterEngine"/> keep their own per-property defaults, so everything asserted
    /// here is deliberately about what a declaration *says*, never about what it does.
    /// </summary>
    public class CounterListGrammarTests
    {
        [Fact]
        public void Parse_BareName_StatesNoExplicitValue()
        {
            var entries = CounterListGrammar.Parse("footnote");

            var entry = Assert.Single(entries);
            Assert.Equal("footnote", entry.Name);
            Assert.False(entry.IsReversed);
            // Null, not 0 - the distinction each per-property default depends on.
            Assert.Null(entry.Value);
        }

        [Fact]
        public void Parse_NameAndInteger_StatesThatValue()
        {
            var entry = Assert.Single(CounterListGrammar.Parse("footnote 5"));

            Assert.Equal("footnote", entry.Name);
            Assert.Equal(5, entry.Value);
        }

        [Fact]
        public void Parse_SeveralPairs_KeepsDeclaredOrderAndPairsEachValueWithItsOwnName()
        {
            var entries = CounterListGrammar.Parse("chapter 1 footnote 3");

            Assert.Equal(2, entries.Count);
            Assert.Equal("chapter", entries[0].Name);
            Assert.Equal(1, entries[0].Value);
            Assert.Equal("footnote", entries[1].Name);
            Assert.Equal(3, entries[1].Value);
        }

        [Fact]
        public void Parse_TwoBareNames_BothStateNoValue()
        {
            // The shape behind "counter-increment: a b" incrementing both by one.
            var entries = CounterListGrammar.Parse("a b");

            Assert.Equal(2, entries.Count);
            Assert.All(entries, e => Assert.Null(e.Value));
        }

        [Fact]
        public void Parse_Reversed_ReportsTheInnerNameAndTheFlag()
        {
            var entry = Assert.Single(CounterListGrammar.Parse("reversed(list-item) 4"));

            Assert.Equal("list-item", entry.Name);
            Assert.True(entry.IsReversed);
            Assert.Equal(4, entry.Value);
        }

        [Fact]
        public void Parse_BareReversed_StatesNoValueSoTheCallerResolvesIt()
        {
            var entry = Assert.Single(CounterListGrammar.Parse("reversed(list-item)"));

            Assert.True(entry.IsReversed);
            Assert.Null(entry.Value);
        }

        [Theory]
        [InlineData("none")]
        [InlineData("NONE")]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void Parse_NothingDeclared_IsEmpty(string? declaration)
        {
            Assert.Empty(CounterListGrammar.Parse(declaration));
        }

        [Fact]
        public void Parse_LeadingInteger_IsSkippedRatherThanNamingACounterAfterANumber()
        {
            var entry = Assert.Single(CounterListGrammar.Parse("7 footnote"));

            Assert.Equal("footnote", entry.Name);
        }

        [Fact]
        public void Parse_NegativeInteger_IsAValueNotAName()
        {
            var entry = Assert.Single(CounterListGrammar.Parse("footnote -2"));

            Assert.Equal("footnote", entry.Name);
            Assert.Equal(-2, entry.Value);
        }

        [Theory]
        [InlineData("footnote", true)]
        [InlineData("FOOTNOTE", true)]
        [InlineData("chapter", false)]
        public void Mentions_AsksOnlyWhetherTheNameAppears(string counterName, bool expected)
        {
            Assert.Equal(expected, CounterListGrammar.Mentions("footnote 3", counterName));
        }

        [Fact]
        public void Mentions_None_IsFalse()
        {
            Assert.False(CounterListGrammar.Mentions("none", "footnote"));
        }

        [Fact]
        public void TryGetValue_NameNotMentioned_ReportsFalseAndLeavesTheDefault()
        {
            Assert.False(CounterListGrammar.TryGetValue("chapter 1", "footnote", defaultValue: 9, out var value));
            Assert.Equal(9, value);
        }

        [Fact]
        public void TryGetValue_NamedWithoutAnInteger_UsesTheCallersOwnDefault()
        {
            Assert.True(CounterListGrammar.TryGetValue("footnote", "footnote", defaultValue: 1, out var value));
            Assert.Equal(1, value);
        }

        [Fact]
        public void TryGetValue_RepeatedName_LastOneWins()
        {
            Assert.True(CounterListGrammar.TryGetValue("footnote 2 footnote 7", "footnote", defaultValue: 0, out var value));
            Assert.Equal(7, value);
        }
    }
}
