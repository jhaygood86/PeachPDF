using PeachBrackets;
using Xunit;

namespace PeachBrackets.Tests
{
    public class HostHelpersTests
    {
        [Fact]
        public void DataUri_DecodesBase64AfterPercentDecoding()
        {
            Assert.True(DataUri.TryDecode("data:image/png;base64,SGk%3D", out var mime, out var bytes));
            Assert.Equal("image/png", mime);
            Assert.Equal("Hi"u8.ToArray(), bytes);
        }

        [Fact]
        public void DataUri_DefaultsToPlainTextAndRejectsOtherSchemes()
        {
            Assert.True(DataUri.TryDecode("data:,a%20b", out var mime, out var bytes));
            Assert.Equal("text/plain", mime);
            Assert.Equal("a b"u8.ToArray(), bytes);

            Assert.False(DataUri.TryDecode("https://example.com/", out _, out _));
            Assert.False(DataUri.TryDecode(null, out _, out _));
        }

        [Fact]
        public void FontDefaults_ListsTheGenericFamiliesAndADefault()
        {
            Assert.False(string.IsNullOrEmpty(FontDefaults.DefaultFamily));
            Assert.True(FontDefaults.DefaultSizePoints > 0);
            Assert.Contains(FontDefaults.GenericFamilies, g => g.Keyword == "serif");
            Assert.DoesNotContain(FontDefaults.GenericFamilies, g => g.Keyword == "system-ui");
        }

        [Fact]
        public void FontDefaults_ToAxisSettings_AddsTheAutomaticOpticalSizeFirst()
        {
            var axes = FontDefaults.ToAxisSettings("wght=700", 16);

            Assert.Equal(2, axes.Count);
            Assert.Equal(16, axes[0].Value);
            Assert.Equal("wght", axes[1].Tag);
            Assert.Equal(700, axes[1].Value);
        }
    }
}
