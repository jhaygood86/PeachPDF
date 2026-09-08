namespace PeachPDF.Tests.CSS
{
    using PeachPDF.CSS;
    using Xunit;

    public class TextSourceTests
    {
        [Fact]
        public void Text_ReturnsTheOriginalString()
        {
            var source = new TextSource("hello world");

            Assert.Equal("hello world", source.Text);
        }

        [Fact]
        public void Length_MatchesStringLength()
        {
            var source = new TextSource("café");

            Assert.Equal(4, source.Length);
        }

        [Fact]
        public void Indexer_ReadsEachCharacter()
        {
            var source = new TextSource("abc");

            Assert.Equal('a', source[0]);
            Assert.Equal('b', source[1]);
            Assert.Equal('c', source[2]);
        }

        [Fact]
        public void ReadCharacter_ReadsSequentiallyThenReturnsEndOfFile()
        {
            var source = new TextSource("ab");

            Assert.Equal('a', source.ReadCharacter());
            Assert.Equal('b', source.ReadCharacter());
            Assert.Equal(Symbols.EndOfFile, source.ReadCharacter());
            // Matches the stream-backed branch: Index keeps advancing on every call, even past the end -
            // LexerBase.NormalizeForward's CRLF peek-and-give-back relies on this (see the regression
            // test below and CssTokenizationTests.LexerOnlyCarriageReturn).
            Assert.Equal(3, source.Index);
        }

        [Fact]
        public void ReadCharacter_OnEmptySource_ReturnsEndOfFileAndAdvancesIndex()
        {
            var source = new TextSource("");

            Assert.Equal(Symbols.EndOfFile, source.ReadCharacter());
            Assert.Equal(1, source.Index);
        }

        [Fact]
        public void Index_CanBeSetAndReadBack()
        {
            var source = new TextSource("abcdef") { Index = 3 };

            Assert.Equal('d', source.ReadCharacter());
            Assert.Equal(4, source.Index);
        }

        [Fact]
        public void ReadCharacters_ReturnsRequestedSlice()
        {
            var source = new TextSource("hello world");

            Assert.Equal("hello", source.ReadCharacters(5));
            Assert.Equal(5, source.Index);
            Assert.Equal(" worl", source.ReadCharacters(5));
        }

        [Fact]
        public void ReadCharacters_PastEnd_ClampsReturnedTextButAdvancesIndexByTheFullRequest()
        {
            var source = new TextSource("abc");

            var result = source.ReadCharacters(10);

            Assert.Equal("abc", result);
            Assert.Equal(10, source.Index);
        }

        [Fact]
        public void ReadCharacters_StartingPastEnd_ReturnsEmptyString()
        {
            var source = new TextSource("abc") { Index = 5 };

            var result = source.ReadCharacters(3);

            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void MemoryConstructor_BehavesIdenticallyToStringConstructor()
        {
            // TextSource(ReadOnlyMemory<char>) is what CssStreamLoader's decoded output flows through -
            // must behave exactly like the string constructor for every basic operation.
            var source = new TextSource("hello world".AsMemory());

            Assert.Equal("hello world", source.Text);
            Assert.Equal(11, source.Length);
            Assert.Equal('h', source[0]);
            Assert.Equal('h', source.ReadCharacter());
            Assert.Equal("ello", source.ReadCharacters(4));
        }

        [Fact]
        public void Slice_ReturnsTheRequestedRangeOfTheSourceMemory()
        {
            var source = new TextSource("hello world");

            var slice = source.Slice(6, 5);

            Assert.Equal("world", slice.ToString());
        }

        // Regression for a bug caught while writing the string-backed fast path: a lone trailing '\r'
        // (no source text after it) must leave Index at the true end (1), not one short of it - see
        // CssTokenizationTests.LexerOnlyCarriageReturn_PositionsAtTrueEndOfInput for the Lexer-level
        // symptom (a second Get() re-reading the same '\r' instead of reaching EndOfFile).
        [Fact]
        public void ReadCharacter_PeekPastEndAfterReadingLastCharacter_LeavesIndexAtTrueEnd()
        {
            var source = new TextSource("\r");

            Assert.Equal('\r', source.ReadCharacter());
            Assert.Equal(1, source.Index);

            // Mirrors LexerBase.NormalizeForward's CRLF lookahead: peek one more character (EndOfFile,
            // since nothing follows), see it isn't '\n', and back up by one.
            Assert.Equal(Symbols.EndOfFile, source.ReadCharacter());
            source.Index--;

            Assert.Equal(1, source.Index);
        }
    }
}
