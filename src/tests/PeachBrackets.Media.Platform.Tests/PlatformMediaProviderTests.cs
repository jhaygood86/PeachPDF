using PeachBrackets.Media.GStreamer;
using PeachBrackets.Media.MediaFoundation;

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
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AnyPlatformWithoutItsOwnProvider_IsServedByGStreamer(int kind)
    {
        var provider = PlatformMediaProvider.CreateFor((PlatformMediaProvider.PlatformKind)kind, null);

        if (GStreamerMediaProvider.IsAvailable)
            Assert.IsType<GStreamerMediaProvider>(provider);
        else
            Assert.Null(provider);
    }

    [Fact]
    public void Windows_IsServedByMediaFoundation_ThenGStreamer()
    {
        var provider = PlatformMediaProvider.CreateFor(PlatformMediaProvider.PlatformKind.Windows, null);

        if (MediaFoundationMediaProvider.IsAvailable)
        {
#if WINDOWS
            // A program that targets Windows gets the router (WinRT for a Win2D document, Media Foundation for the rest).
            Assert.NotNull(provider);
#else
            Assert.IsType<MediaFoundationMediaProvider>(provider);
#endif
        }
        else if (GStreamerMediaProvider.IsAvailable)
        {
            Assert.IsType<GStreamerMediaProvider>(provider);
        }
        else
        {
            Assert.Null(provider);
        }
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
