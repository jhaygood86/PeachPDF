using PeachBrackets.Apple.Logic;

namespace PeachBrackets.Apple.Tests;

public class InputTraitsTests
{
    [Theory]
    [InlineData("password", "Default", true, "done", false)]
    [InlineData("email", "EmailAddress", false, "done", false)]
    [InlineData("url", "Url", false, "go", false)]
    [InlineData("tel", "PhonePad", false, "done", false)]
    [InlineData("number", "DecimalPad", false, "done", false)]
    [InlineData("search", "Default", false, "search", true)]
    [InlineData("textarea", "Default", false, "default", true)]
    [InlineData("text", "Default", false, "done", true)]
    public void EachFieldTypeAsksForItsKeyboard(string type, string keyboard, bool secure, string returnKey, bool autocorrect)
    {
        var traits = AppleInputTraits.For(type);
        Assert.Equal(keyboard, traits.Keyboard.ToString());
        Assert.Equal(secure, traits.Secure);
        Assert.Equal(returnKey, traits.ReturnKey);
        Assert.Equal(autocorrect, traits.Autocorrect);
    }
}
