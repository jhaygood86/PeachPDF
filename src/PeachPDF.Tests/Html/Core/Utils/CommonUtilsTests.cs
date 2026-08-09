using PeachPDF.CSS;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Utils;

namespace PeachPDF.Tests.Html.Core.Utils
{
    public class CommonUtilsTests
    {
        [Theory]
        [InlineData(0x61, false)]      // 'a'
        [InlineData(0x4E2D, true)]     // '中'
        [InlineData(0x4e00, true)]
        [InlineData(0xFA2D, true)]
        [InlineData(0x4dff, false)]
        [InlineData(0x1F600, false)]   // an astral emoji is outside the BMP Asian range (and, as a Rune, can never be a lone surrogate)
        public void IsAsianCharacter_ChecksRange(int codepoint, bool expected)
        {
            Assert.Equal(expected, CommonUtils.IsAsianCharacter(new System.Text.Rune(codepoint)));
        }

        [Theory]
        [InlineData('5', false, true)]
        [InlineData('a', false, false)]
        [InlineData('a', true, true)]
        [InlineData('F', true, true)]
        [InlineData('g', true, false)]
        public void IsDigit_ChecksDecimalOrHex(char ch, bool hex, bool expected)
        {
            Assert.Equal(expected, CommonUtils.IsDigit(ch, hex));
        }

        [Theory]
        [InlineData("123", true)]
        [InlineData("-123", true)]
        [InlineData("", false)]
        [InlineData("12a", false)]
        public void IsInteger_ChecksAllDigitsOrMinus(string value, bool expected)
        {
            Assert.Equal(expected, CommonUtils.IsInteger(value.AsSpan()));
        }

        [Theory]
        [InlineData('7', false, 7)]
        [InlineData('a', false, 0)]
        [InlineData('a', true, 10)]
        [InlineData('F', true, 15)]
        [InlineData('g', true, 0)]
        public void ToDigit_ConvertsCharToNumericValue(char ch, bool hex, int expected)
        {
            Assert.Equal(expected, CommonUtils.ToDigit(ch, hex));
        }

        [Fact]
        public void Max_ReturnsComponentWiseMaximum()
        {
            var result = CommonUtils.Max(new RSize(10, 20), new RSize(30, 5));

            Assert.Equal(new RSize(30, 20), result);
        }

        [Fact]
        public void GetFirstValueOrDefault_NonEmptyDictionary_ReturnsFirstValue()
        {
            var dic = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };

            var value = CommonUtils.GetFirstValueOrDefault(dic, -1);

            Assert.Equal(1, value);
        }

        [Fact]
        public void GetFirstValueOrDefault_EmptyDictionary_ReturnsDefault()
        {
            var dic = new Dictionary<string, int>();

            var value = CommonUtils.GetFirstValueOrDefault(dic, -1);

            Assert.Equal(-1, value);
        }

        [Fact]
        public void GetFirstValueOrDefault_NullDictionary_ReturnsDefault()
        {
            var value = CommonUtils.GetFirstValueOrDefault<string, int>(null, -1);

            Assert.Equal(-1, value);
        }

        [Theory]
        [InlineData("hello world", 0, 0, 5)]
        [InlineData("  hello", 0, 2, 5)]
        [InlineData("hello", 10, -1, 0)]
        public void GetNextSubString_FindsWhitespaceDelimitedWord(string str, int start, int expectedIndex, int expectedLength)
        {
            var index = CommonUtils.GetNextSubString(str, start, out var length);

            Assert.Equal(expectedIndex, index);
            Assert.Equal(expectedLength, length);
        }

        [Fact]
        public void SubStringEquals_CaseInsensitiveMatch_ReturnsTrue()
        {
            Assert.True(CommonUtils.SubStringEquals("Hello World", 0, 5, "hello"));
        }

        [Fact]
        public void SubStringEquals_DifferentLength_ReturnsFalse()
        {
            Assert.False(CommonUtils.SubStringEquals("Hello World", 0, 5, "hell"));
        }

        [Fact]
        public void SubStringEquals_OutOfRange_ReturnsFalse()
        {
            Assert.False(CommonUtils.SubStringEquals("Hi", 0, 5, "hello"));
        }

        [Theory]
        [InlineData(0, Keywords.UpperAlpha, "")]
        [InlineData(1, Keywords.UpperAlpha, "A")]
        [InlineData(27, Keywords.UpperAlpha, "AA")]
        [InlineData(1, Keywords.LowerAlpha, "a")]
        [InlineData(1, Keywords.LowerLatin, "a")]
        [InlineData(1, Keywords.UpperLatin, "A")]
        [InlineData(4, Keywords.LowerRoman, "iv")]
        [InlineData(4, Keywords.UpperRoman, "IV")]
        public void ConvertToAlphaNumber_KnownStyles(int number, string style, string expected)
        {
            Assert.Equal(expected, CommonUtils.ConvertToAlphaNumber(number, style));
        }

        [Fact]
        public void ConvertToAlphaNumber_LowerGreek_ProducesNonEmptyResult()
        {
            var result = CommonUtils.ConvertToAlphaNumber(1, Keywords.LowerGreek);

            Assert.NotEmpty(result);
        }

        [Theory]
        [InlineData(Keywords.Armenian)]
        [InlineData(Keywords.Georgian)]
        [InlineData(Keywords.Hebrew)]
        public void ConvertToAlphaNumber_SpecificAlphabets_ProduceNonEmptyResult(string style)
        {
            var result = CommonUtils.ConvertToAlphaNumber(5, style);

            Assert.NotEmpty(result);
        }

        [Theory]
        [InlineData(Keywords.Hiragana)]
        [InlineData(Keywords.HiraganaIroha)]
        [InlineData(Keywords.Katakana)]
        [InlineData(Keywords.KatakanaIroha)]
        public void ConvertToAlphaNumber_KanaAlphabets_ProduceNonEmptyResult(string style)
        {
            var result = CommonUtils.ConvertToAlphaNumber(5, style);

            Assert.NotEmpty(result);
        }

        [Fact]
        public void ConvertToAlphaNumber_ZeroWithAnyStyle_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, CommonUtils.ConvertToAlphaNumber(0, Keywords.Hebrew));
        }

        [Theory]
        [InlineData(Keywords.LowerArmenian, true)]
        [InlineData(Keywords.UpperArmenian, true)]
        [InlineData(Keywords.ArabicIndic, false)]      // "numeric" family - handled by ConvertToPositionalNumber, not this method
        [InlineData(Keywords.EthiopicNumeric, false)]  // its own algorithm
        [InlineData(Keywords.CjkEarthlyBranch, false)] // "fixed" family
        [InlineData("bogus-style", false)]
        public void IsAlphabeticCounterStyle_ChecksKnownStyles(string style, bool expected)
        {
            Assert.Equal(expected, CommonUtils.IsAlphabeticCounterStyle(style));
        }

        [Theory]
        [InlineData(1, Keywords.LowerArmenian, "ա")]
        [InlineData(1, Keywords.UpperArmenian, "Ա")]
        [InlineData(1000, Keywords.Armenian, "Ռ")]       // previously silently dropped above 999
        [InlineData(1000, Keywords.UpperArmenian, "Ռ")]
        [InlineData(1000, Keywords.LowerArmenian, "ռ")]
        [InlineData(15, Keywords.Hebrew, "טו")]      // Tetragrammaton-avoidance override
        [InlineData(16, Keywords.Hebrew, "טז")]
        [InlineData(19, Keywords.Hebrew, "יט")]      // no override needed - naive combination already matches
        [InlineData(1000, Keywords.Hebrew, "א׳")]    // previously silently dropped above 999
        public void ConvertToAlphaNumber_ArmenianAndHebrewBoundaries(int number, string style, string expected)
        {
            Assert.Equal(expected, CommonUtils.ConvertToAlphaNumber(number, style));
        }

        [Fact]
        public void ConvertToAlphaNumber_HiraganaIroha_DiffersFromDictionaryOrderHiragana()
        {
            var dictionaryOrder = CommonUtils.ConvertToAlphaNumber(2, Keywords.Hiragana);
            var irohaOrder = CommonUtils.ConvertToAlphaNumber(2, Keywords.HiraganaIroha);

            Assert.NotEqual(dictionaryOrder, irohaOrder);
            Assert.Equal("ろ", irohaOrder); // ろ - second character of the iroha ordering
        }

        [Fact]
        public void ConvertToAlphaNumber_KatakanaIroha_DiffersFromDictionaryOrderKatakana()
        {
            var dictionaryOrder = CommonUtils.ConvertToAlphaNumber(2, Keywords.Katakana);
            var irohaOrder = CommonUtils.ConvertToAlphaNumber(2, Keywords.KatakanaIroha);

            Assert.NotEqual(dictionaryOrder, irohaOrder);
            Assert.Equal("ロ", irohaOrder); // ロ - second character of the iroha ordering
        }

        [Theory]
        [InlineData(Keywords.Hiragana)]
        [InlineData(Keywords.HiraganaIroha)]
        [InlineData(Keywords.Katakana)]
        [InlineData(Keywords.KatakanaIroha)]
        public void ConvertToAlphaNumber_KanaAlphabets_DoNotThrowPastFirstWraparound(string style)
        {
            // hiragana/katakana are 48 characters; hiragana-iroha/katakana-iroha are 47 (excluding ん) -
            // ConvertToSpecificNumbers2 previously hard-coded a 48-character alphabet's arithmetic, which
            // threw IndexOutOfRangeException for any 47-character alphabet value >= 48.
            for (var number = 1; number <= 200; number++)
            {
                var result = CommonUtils.ConvertToAlphaNumber(number, style);
                Assert.NotEmpty(result);
            }
        }

        [Theory]
        [InlineData(0, Keywords.Devanagari, "०")]
        [InlineData(12, Keywords.Devanagari, "१२")]
        [InlineData(-3, Keywords.Devanagari, "-३")]
        [InlineData(9, Keywords.Thai, "๙")]
        [InlineData(10, Keywords.CjkDecimal, "一〇")]
        [InlineData(5, "bogus-style", null)]
        public void ConvertToPositionalNumber_ChecksSubstitutionAndUnknownStyle(int number, string style, string? expected)
        {
            Assert.Equal(expected, CommonUtils.ConvertToPositionalNumber(number, style));
        }

        [Theory]
        [InlineData(1, Keywords.CjkEarthlyBranch, "子")]
        [InlineData(12, Keywords.CjkEarthlyBranch, "亥")]
        [InlineData(13, Keywords.CjkEarthlyBranch, null)]  // beyond the fixed 12-symbol range - falls back to decimal
        [InlineData(0, Keywords.CjkEarthlyBranch, null)]
        [InlineData(1, Keywords.CjkHeavenlyStem, "甲")]
        [InlineData(10, Keywords.CjkHeavenlyStem, "癸")]
        [InlineData(11, Keywords.CjkHeavenlyStem, null)]   // beyond the fixed 10-symbol range
        [InlineData(1, "bogus-style", null)]
        public void ConvertToFixedCjkSymbol_ChecksRangeAndUnknownStyle(int number, string style, string? expected)
        {
            Assert.Equal(expected, CommonUtils.ConvertToFixedCjkSymbol(number, style));
        }

        [Theory]
        [InlineData(0, "")]
        [InlineData(-5, "")]
        [InlineData(1, "፩")]
        [InlineData(100, "፻")]
        [InlineData(78010092, "፸፰፻፩፼፺፪")]    // CSS Counter Styles Level 3 §7.2's own worked example
        [InlineData(10000092, "፲፻፼፺፪")]                // an even-indexed zero group still gets its ten-thousand separator
        public void ConvertToEthiopicNumber_MatchesSpecAlgorithm(int number, string expected)
        {
            Assert.Equal(expected, CommonUtils.ConvertToEthiopicNumber(number));
        }
    }
}
