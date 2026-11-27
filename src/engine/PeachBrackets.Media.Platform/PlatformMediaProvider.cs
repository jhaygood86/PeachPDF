using PeachBrackets.Media.GStreamer;
using System;

namespace PeachBrackets.Media.Platform;

/// <summary>
/// Picks the media provider for the operating system the program runs on, so that a host (a view, a test runner) sets
/// <see cref="HtmlDocumentOptions.MediaProvider"/> without knowing which decoder the platform offers.
/// </summary>
public static class PlatformMediaProvider
{
    /// <summary>The kinds of platform a provider is chosen for.</summary>
    internal enum PlatformKind
    {
        Windows,
        Apple,
        Android,
        Other,
    }

    /// <summary>
    /// Makes the provider for this platform, or returns null when it has no decoder (a document then shows its posters, as with no provider).
    /// Today every platform is served by GStreamer, which Linux, Windows and macOS can all run when it is installed; the platform decoders (Media Foundation,
    /// AVFoundation, Android's media stack) take over their platforms as their packages land.
    /// </summary>
    /// <param name="options">settings every provider honors, such as <see cref="MediaProviderOptions.DeterministicClock"/></param>
    /// <returns>the provider, or null when the platform has none installed</returns>
    public static IMediaProvider? Create(MediaProviderOptions? options = null) => CreateFor(Current(), options);

    internal static PlatformKind Current() =>
        OperatingSystem.IsWindows() ? PlatformKind.Windows
        : OperatingSystem.IsMacOS() || OperatingSystem.IsIOS() ? PlatformKind.Apple
        : OperatingSystem.IsAndroid() ? PlatformKind.Android
        : PlatformKind.Other;

    internal static IMediaProvider? CreateFor(PlatformKind kind, MediaProviderOptions? options)
    {
        // Each branch is where that platform's own provider is tried first once it exists; GStreamer is what is left when it is absent.
        switch (kind)
        {
            case PlatformKind.Windows:
            case PlatformKind.Apple:
            case PlatformKind.Android:
            case PlatformKind.Other:
            default:
                return GStreamerMediaProvider.IsAvailable ? new GStreamerMediaProvider(options) : null;
        }
    }
}
