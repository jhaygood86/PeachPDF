using PeachBrackets.Media.MediaFoundation.Internal;

namespace PeachBrackets.Media.MediaFoundation;

/// <summary>
/// Plays the audio and video of a document through Windows Media Foundation from any .NET program on Windows, whatever its target framework: pictures come from a
/// source reader, which decodes with the codecs the machine has (H.264, HEVC, AAC, MP3 and WAV on a stock Windows; WebM, VP9, AV1 and Ogg where their extensions are
/// installed) and sound is rendered through WASAPI. Pictures are pixels any canvas can draw; <c>PeachBrackets.Media.Windows</c> is the one to use for a Win2D document,
/// whose pictures it keeps on the GPU. Set it in <see cref="HtmlDocumentOptions.MediaProvider"/>; <c>PeachBrackets.Media.Platform</c> picks it on Windows.
/// </summary>
public sealed class MediaFoundationMediaProvider : IMediaProvider
{
    private readonly MediaProviderOptions _options;

    /// <summary>Makes a provider.</summary>
    /// <param name="options">settings, or null for the defaults (playback by the player's own clock)</param>
    public MediaFoundationMediaProvider(MediaProviderOptions? options = null)
    {
        _options = options ?? new MediaProviderOptions();
    }

    /// <summary>Whether the program runs on Windows 10 or later, where this provider plays.</summary>
    public static bool IsAvailable => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763);

    /// <inheritdoc />
    public string CanPlayType(string mimeType)
    {
        ArgumentNullException.ThrowIfNull(mimeType);
        return IsAvailable ? MediaTypeSupport.CanPlay(mimeType) : "";
    }

    /// <inheritdoc />
    public async ValueTask<IMediaSession?> OpenAsync(MediaRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsAvailable)
            return null;

        return await MfSession.OpenAsync(request, _options, cancellationToken).ConfigureAwait(false);
    }
}
