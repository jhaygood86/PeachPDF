using PeachBrackets.Scripting.Internal.Idna;

namespace PeachBrackets.Scripting.Tests;

/// <summary>Domain to ASCII (UTS #46) as the URL standard needs it.</summary>
public class IdnaTests
{
    [Theory]
    [InlineData("web-platform.test", "web-platform.test")]
    [InlineData("EXAMPLE.com", "example.com")]
    [InlineData("faß.de", "xn--fa-hia.de")]
    [InlineData("Faß.de", "xn--fa-hia.de")]
    [InlineData("xn--fa-hia.de", "xn--fa-hia.de")]
    [InlineData("à.א̈", "xn--0ca.xn--ssa73l")]
    [InlineData("a。b", "a.b")]
    public void AValidDomainBecomesItsAsciiForm(string input, string expected) => Assert.Equal(expected, Uts46.ToAscii(input));

    [Theory]
    [InlineData("xn--")]
    [InlineData("xn--a-")]
    [InlineData("a‌b")]
    [InlineData("̀a")]
    [InlineData("אa")]
    public void AnInvalidDomainIsAnError(string input) => Assert.Null(Uts46.ToAscii(input));
}
