using PeachBrackets.Media.GStreamer;

namespace PeachBrackets.Media.Platform.Tests;

public class PlatformMediaProviderTests
{
    [Fact]
    public void Current_NamesThePlatformTheProgramRunsOn()
    {
        var expected = OperatingSystem.IsWindows() ? PlatformMediaProvider.PlatformKind.Windows
            : OperatingSystem.IsMacOS() ? PlatformMediaProvider.PlatformKind.Apple
            : PlatformMediaProvider.PlatformKind.Other;

        Assert.Equal(expected, PlatformMediaProvider.Current());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EveryPlatform_IsServedByGStreamer_UntilItsOwnProviderExists(int kind)
    {
        var provider = PlatformMediaProvider.CreateFor((PlatformMediaProvider.PlatformKind)kind, null);

        if (GStreamerMediaProvider.IsAvailable)
            Assert.IsType<GStreamerMediaProvider>(provider);
        else
            Assert.Null(provider);
    }

    [Fact]
    public void Create_GivesAProviderThatSaysWhatItCanPlay_OrNothingWhenThereIsNoDecoder()
    {
        var provider = PlatformMediaProvider.Create(new MediaProviderOptions { DeterministicClock = true });

        Assert.SkipUnless(provider is not null, "No media decoder is installed.");
        Assert.Equal("", provider.CanPlayType("text/html"));
        Assert.Equal("maybe", provider.CanPlayType("video/webm"));
    }
}
