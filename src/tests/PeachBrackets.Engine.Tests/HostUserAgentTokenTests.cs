using System.Runtime.InteropServices;

namespace PeachBrackets.Engine.Tests;

/// <summary>The platform and CPU values of the user-agent for each kind of system, as Chromium reports them.</summary>
public class HostUserAgentTokenTests
{
    [Theory]
    [InlineData("Windows", Architecture.X64, true, true, "Windows NT 10.0; Win64; x64")]
    [InlineData("Windows", Architecture.Arm64, true, true, "Windows NT 10.0; Win64; x64")]
    [InlineData("Windows", Architecture.X86, false, true, "Windows NT 10.0; WOW64")]
    [InlineData("Windows", Architecture.X86, false, false, "Windows NT 10.0")]
    [InlineData("MacOs", Architecture.Arm64, true, true, "Macintosh; Intel Mac OS X 10_15_7")]
    [InlineData("MacOs", Architecture.X64, true, true, "Macintosh; Intel Mac OS X 10_15_7")]
    [InlineData("Android", Architecture.Arm64, true, true, "Linux; Android 10; K")]
    [InlineData("Linux", Architecture.X64, true, true, "X11; Linux x86_64")]
    [InlineData("Linux", Architecture.Arm64, true, true, "X11; Linux aarch64")]
    [InlineData("Linux", Architecture.Arm, false, false, "X11; Linux armv8l")]
    [InlineData("Linux", Architecture.X86, false, false, "X11; Linux i686")]
    public void ThePlatformToken_IsWhatChromiumSendsThere(string platform, Architecture architecture, bool is64BitProcess, bool is64BitOperatingSystem, string expected)
    {
        Assert.Equal(expected, HostUserAgent.PlatformToken(Enum.Parse<HostPlatform>(platform), architecture, is64BitProcess, is64BitOperatingSystem));
    }

    [Fact]
    public void TheWholeString_IsInTheRequestedShape()
    {
        Assert.Equal(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/156.0.0.0 Safari/537.36 PeachBrackets/0.9.26-preview2",
            HostUserAgent.Compose("Windows NT 10.0; Win64; x64", "0.9.26-preview2"));
    }
}
