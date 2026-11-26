using PeachDrawing.Text.Internal.Text;

namespace PeachDrawing.Text.Tests.Text;

/// <summary>The language-system tag a BCP 47 tag selects in a font's layout tables.</summary>
public class OpenTypeLanguageTagsTests
{
    [Theory]
    [InlineData("tr", "TRK ")]
    [InlineData("TR-tr", "TRK ")]
    [InlineData("en", "ENG ")]
    public void ATagShorterThanFourCharactersIsPaddedWithSpacesAsAFontWritesIt(string language, string expected) =>
        Assert.Equal(expected, OpenTypeLanguageTags.ResolveSystemTag(language));

    [Fact]
    public void ALanguageWithoutATag_ResolvesToNothing() => Assert.Null(OpenTypeLanguageTags.ResolveSystemTag("zz-nothing"));
}
