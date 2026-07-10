using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Network;

namespace PeachPDF.Tests.Html.Core.Utils
{
    public class CommonUtilsTests
    {
        [Theory]
        [InlineData('a', false)]
        [InlineData('中', true)]
        [InlineData((char)0x4e00, true)]
        [InlineData((char)0xFA2D, true)]
        [InlineData((char)0x4dff, false)]
        public void IsAsianCharacter_ChecksRange(char ch, bool expected)
        {
            Assert.Equal(expected, CommonUtils.IsAsianCharacter(ch));
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
        public void TryGetUri_ValidAbsoluteUri_ReturnsUri()
        {
            var uri = CommonUtils.TryGetUri("https://example.com/path");

            Assert.NotNull(uri);
        }

        [Fact]
        public void TryGetUri_Invalid_ReturnsNull()
        {
            var uri = CommonUtils.TryGetUri("not a uri \0");

            Assert.Null(uri);
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

        [Fact]
        public void TryGetFileInfo_ValidPath_ReturnsFileInfo()
        {
            var info = CommonUtils.TryGetFileInfo("some-file.txt");

            Assert.NotNull(info);
        }

        [Fact]
        public void TryGetFileInfo_InvalidPath_ReturnsNull()
        {
            // An embedded NUL is rejected by FileInfo's constructor on every OS. An
            // excessively long path was tried previously, but only Windows validates path
            // length eagerly in the FileInfo constructor -- macOS/Linux accept it, so that
            // input isn't reliably invalid across platforms.
            var info = CommonUtils.TryGetFileInfo("invalid\0path.txt");

            Assert.Null(info);
        }

        [Fact]
        public void GetLocalFileName_SimpleImageUrl_ProducesFileWithExtension()
        {
            var uri = new RUri("https://example.com/images/photo.png");

            var info = CommonUtils.GetLocalFileName(uri);

            Assert.NotNull(info);
            Assert.EndsWith(".png", info!.Name);
        }

        [Fact]
        public void GetLocalFileName_UrlWithQueryString_HandlesParams()
        {
            var uri = new RUri("https://example.com/images/photo.png?size=large");

            var info = CommonUtils.GetLocalFileName(uri);

            Assert.NotNull(info);
        }

        [Fact]
        public void GetLocalFileName_UrlWithoutSlash_ReturnsNull()
        {
            // AbsoluteUri for a well-formed http(s) URL always has at least the scheme's "//",
            // so exercise the "no slash found" branch via a URI whose AbsoluteUri has none.
            var uri = new RUri("mailto:someone@example.com");

            var info = CommonUtils.GetLocalFileName(uri);

            Assert.Null(info);
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
        [InlineData(0, CssConstants.UpperAlpha, "")]
        [InlineData(1, CssConstants.UpperAlpha, "A")]
        [InlineData(27, CssConstants.UpperAlpha, "AA")]
        [InlineData(1, CssConstants.LowerAlpha, "a")]
        [InlineData(1, CssConstants.LowerLatin, "a")]
        [InlineData(4, CssConstants.LowerRoman, "iv")]
        [InlineData(4, CssConstants.UpperRoman, "IV")]
        public void ConvertToAlphaNumber_KnownStyles(int number, string style, string expected)
        {
            Assert.Equal(expected, CommonUtils.ConvertToAlphaNumber(number, style));
        }

        [Fact]
        public void ConvertToAlphaNumber_LowerGreek_ProducesNonEmptyResult()
        {
            var result = CommonUtils.ConvertToAlphaNumber(1, CssConstants.LowerGreek);

            Assert.NotEmpty(result);
        }

        [Theory]
        [InlineData(CssConstants.Armenian)]
        [InlineData(CssConstants.Georgian)]
        [InlineData(CssConstants.Hebrew)]
        public void ConvertToAlphaNumber_SpecificAlphabets_ProduceNonEmptyResult(string style)
        {
            var result = CommonUtils.ConvertToAlphaNumber(5, style);

            Assert.NotEmpty(result);
        }

        [Theory]
        [InlineData(CssConstants.Hiragana)]
        [InlineData(CssConstants.HiraganaIroha)]
        [InlineData(CssConstants.Katakana)]
        [InlineData(CssConstants.KatakanaIroha)]
        public void ConvertToAlphaNumber_KanaAlphabets_ProduceNonEmptyResult(string style)
        {
            var result = CommonUtils.ConvertToAlphaNumber(5, style);

            Assert.NotEmpty(result);
        }

        [Fact]
        public void ConvertToAlphaNumber_ZeroWithAnyStyle_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, CommonUtils.ConvertToAlphaNumber(0, CssConstants.Hebrew));
        }
    }
}
