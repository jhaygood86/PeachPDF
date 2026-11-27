using PeachBrackets.Media.GStreamer;
using PeachBrackets.Media.MediaFoundation;
using System;
using System.Threading;
using System.Threading.Tasks;

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
#if WINDOWS
            case PlatformKind.Windows:
                return WindowsRouted.Create(options);
#else
            case PlatformKind.Windows:
                // A program that does not target Windows cannot see the WinRT provider; the Media Foundation one has no such limit.
                return MediaFoundationMediaProvider.IsAvailable ? new MediaFoundationMediaProvider(options) : GStreamerMediaProvider.IsAvailable ? new GStreamerMediaProvider(options) : null;
#endif
            case PlatformKind.Apple:
#if ANDROID
            case PlatformKind.Android:
                return PeachBrackets.Media.Android.AndroidMediaProvider.IsAvailable ? new PeachBrackets.Media.Android.AndroidMediaProvider(options) : null;
#else
            case PlatformKind.Android:
#endif
            case PlatformKind.Other:
            default:
                return GStreamerMediaProvider.IsAvailable ? new GStreamerMediaProvider(options) : null;
        }
    }
}

#if WINDOWS
/// <summary>
/// For a program that targets Windows: the WinRT provider for a document drawn with Win2D (its pictures stay on the GPU), and the Media Foundation provider for a
/// document drawn any other way (which needs pixels, and gets them without the WinRT provider's read back). GStreamer when Windows has neither.
/// </summary>
internal sealed class WindowsRouted : IMediaProvider
{
    private readonly IMediaProvider _gpu;
    private readonly IMediaProvider _pixels;

    private WindowsRouted(IMediaProvider gpu, IMediaProvider pixels)
    {
        _gpu = gpu;
        _pixels = pixels;
    }

    public static IMediaProvider? Create(MediaProviderOptions? options)
    {
        if (!Windows.WindowsMediaProvider.IsAvailable || !MediaFoundationMediaProvider.IsAvailable)
            return GStreamerMediaProvider.IsAvailable ? new GStreamerMediaProvider(options) : null;

        return new WindowsRouted(new Windows.WindowsMediaProvider(options), new MediaFoundationMediaProvider(options));
    }

    public string CanPlayType(string mimeType) => _pixels.CanPlayType(mimeType);

    public ValueTask<IMediaSession?> OpenAsync(MediaRequest request, CancellationToken cancellationToken = default) =>
        (request.RenderContext is PeachDrawing.Win2D.Win2dRenderContext ? _gpu : _pixels).OpenAsync(request, cancellationToken);
}
#endif
