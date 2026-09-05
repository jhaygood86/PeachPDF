using PeachPDF.Text;
using Xunit;

namespace PeachPDF.Tests.Text
{
    /// <summary>Coverage for <see cref="OpenTypeScriptTags"/>'s curated Unicode-script-to-OpenType-tag
    /// table, including the legacy-tag exceptions (trailing-space-padded/collapsed tags) that make this
    /// not a mechanical lowercase of the script name.</summary>
    public class OpenTypeScriptTagsTests
    {
        [Theory]
        [InlineData("Arabic", "arab")]
        [InlineData("Latin", "latn")]
        [InlineData("Hebrew", "hebr")]
        [InlineData("Syriac", "syrc")]
        [InlineData("Devanagari", "deva")]
        [InlineData("Thaana", "thaa")]
        [InlineData("Mongolian", "mong")]
        public void Resolve_KnownScript_ReturnsExpectedTag(string script, string expectedTag)
        {
            Assert.Equal(expectedTag, OpenTypeScriptTags.Resolve(script));
        }

        [Theory]
        [InlineData("Nko", "nko ")] // trailing-space-padded legacy tag
        [InlineData("Lao", "lao ")]
        [InlineData("Vai", "vai ")]
        [InlineData("Yi", "yi  ")] // two trailing spaces
        [InlineData("Hiragana", "kana")] // Hiragana and Katakana collapse to the single combined script
        [InlineData("Katakana", "kana")]
        public void Resolve_LegacyExceptionScripts_ReturnsExpectedTag(string script, string expectedTag)
        {
            Assert.Equal(expectedTag, OpenTypeScriptTags.Resolve(script));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Common")] // not a real script - resolving Common/Inherited is a text-run concern, not this table's job
        [InlineData("Inherited")]
        [InlineData("Tifinagh")] // a real script simply absent from the curated subset
        public void Resolve_UnrecognizedOrNonScriptValue_ReturnsNull(string? script)
        {
            Assert.Null(OpenTypeScriptTags.Resolve(script));
        }
    }
}
