using PeachBrackets.Css;

namespace PeachBrackets.Engine.Tests.CSS;

public class ColorSchemeGrammarTests
{
    [Theory]
    [InlineData("normal", false, false, false)]
    [InlineData("light", true, false, false)]
    [InlineData("dark", false, true, false)]
    [InlineData("light dark", true, true, false)]
    [InlineData("DARK LIGHT", true, true, false)]
    [InlineData("only dark", false, true, true)]
    [InlineData("dark only", false, true, true)]
    [InlineData("light dark only", true, true, true)]
    [InlineData("light my-scheme", true, false, false)]
    [InlineData("my-scheme", false, false, false)]
    public void ParsesTheValidForms(string text, bool light, bool dark, bool only)
    {
        var scheme = ColorScheme.TryParse(text);

        Assert.Equal(new ColorScheme(light, dark, only), scheme);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("only")]
    [InlineData("normal light")]
    [InlineData("light normal")]
    [InlineData("only light only")]
    [InlineData("light only dark")]
    [InlineData("inherit light")]
    [InlineData("light, dark")]
    [InlineData("1px")]
    public void RejectsTheInvalidForms(string text)
    {
        Assert.Null(ColorScheme.TryParse(text));
    }

    [Fact]
    public void ParseFallsBackToNormal()
    {
        Assert.Equal(ColorScheme.Normal, ColorScheme.Parse("nonsense nonsense only only"));
        Assert.Equal(ColorScheme.Normal, ColorScheme.Parse(null));
    }

    [Theory]
    [InlineData("normal", false, false)]
    [InlineData("normal", true, false)]
    [InlineData("light", true, false)]
    [InlineData("dark", false, true)]
    [InlineData("light dark", false, false)]
    [InlineData("light dark", true, true)]
    [InlineData("only dark", false, true)]
    public void TheUsedSchemeFollowsThePreferenceOnlyWhenBothAreNamed(string text, bool preferDark, bool expectedDark)
    {
        Assert.Equal(expectedDark, ColorScheme.Parse(text).UsesDark(preferDark));
    }

    [Fact]
    public void TheSchemeScopeNestsAndRestores()
    {
        Assert.False(ColorSchemeScope.IsDark);

        using (ColorSchemeScope.Enter(true))
        {
            Assert.True(ColorSchemeScope.IsDark);
            using (ColorSchemeScope.Enter(false)) Assert.False(ColorSchemeScope.IsDark);
            Assert.True(ColorSchemeScope.IsDark);
            Assert.Equal(new Color(255, 255, 255), Colors.GetColor("CanvasText"));
        }

        Assert.False(ColorSchemeScope.IsDark);
        Assert.Equal(new Color(0, 0, 0), Colors.GetColor("CanvasText"));
    }
}
