using PeachPDF.Html.Core.Dom;
using System.Globalization;

namespace PeachPDF.Tests.Html.Core.Dom
{
    /// <summary>
    /// <see cref="CssLayoutEngine.UpdateTrailingGraphemeContext"/> has a no-allocation path for plain ASCII; it must agree with the
    /// general one (the last grapheme cluster of context plus text) everywhere, CR LF and mixed input included.
    /// </summary>
    public class TrailingGraphemeContextTests
    {
        private static string Reference(string preceding, string text)
        {
            var combined = preceding + text;
            if (combined.Length == 0) return string.Empty;

            return combined[StringInfo.ParseCombiningCharacters(combined)[^1]..];
        }

        [Theory]
        [InlineData("", "word")]
        [InlineData("a", "word")]
        [InlineData("a", "w")]
        [InlineData("", "x")]
        [InlineData("\r", "\n")]
        [InlineData("\r", "\nabc")]
        [InlineData("a", "b\r\n")]
        [InlineData("", "abc\r")]
        [InlineData("e", "́")]
        [InlineData("", "é")]
        [InlineData("a", "é")]
        [InlineData("\U0001F1E9", "\U0001F1EA")]
        [InlineData("\U0001F468‍", "\U0001F469")]
        [InlineData("a", "")]
        [InlineData("", "")]
        [InlineData("؀", "1")]
        [InlineData("ab", "cd")]
        [InlineData("1", "2")]
        [InlineData("\t", " ")]
        public void AgreesWithTheGeneralPath(string preceding, string text)
        {
            Assert.Equal(Reference(preceding, text), CssLayoutEngine.UpdateTrailingGraphemeContext(preceding, text));
        }

        [Fact]
        public void AgreesOnRandomAsciiAndMixedStrings()
        {
            var random = new Random(11);
            char[] alphabet = ['a', 'b', 'Z', '0', '9', ' ', '-', '.', '\t', '\r', '\n', '́', '‍', 'é', '\uD83D', '\uDE00'];

            string Make() => new(Enumerable.Range(0, random.Next(0, 6)).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());

            for (var i = 0; i < 2000; i++)
            {
                var preceding = Make();
                var text = Make();

                Assert.Equal(Reference(preceding, text), CssLayoutEngine.UpdateTrailingGraphemeContext(preceding, text));
            }
        }
    }
}
