using PeachBrackets.Media.Windows.Internal;
using PeachDrawing.Win2D;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace PeachBrackets.Media.Windows;

/// <summary>
/// Plays the audio and video of a document through Windows Media Foundation (<c>Windows.Media.Playback.MediaPlayer</c>): whatever codecs the machine has,
/// which on a stock Windows is H.264, HEVC, AAC, MP3 and WAV, and WebM, VP9, AV1 and Ogg where their extensions are installed. A video's pictures are served
/// by the player's frame server into Win2D bitmaps on the device the document draws with, so drawing one reads nothing back. Set it in
/// <see cref="HtmlDocumentOptions.MediaProvider"/> of a document whose render context is a <see cref="Win2dRenderContext"/>; with any other context each picture is read back from a shared device into pixels any canvas can draw.
/// </summary>
public sealed class WindowsMediaProvider : IMediaProvider
{
    private readonly MediaProviderOptions _options;

    /// <summary>Makes a provider.</summary>
    /// <param name="options">settings, or null for the defaults (playback by the player's own clock)</param>
    public WindowsMediaProvider(MediaProviderOptions? options = null)
    {
        _options = options ?? new MediaProviderOptions();
    }

    /// <summary>Whether the machine can play media through the Windows player (it has the Windows 10 media stack; every supported Windows does).</summary>
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

        // The player's events arrive on its own threads whatever thread opens it, but opening it off the caller's keeps a document's UI thread free of the wait.
        return await Task.Run(() => WindowsMediaSession.OpenAsync(request, _options, cancellationToken), cancellationToken).ConfigureAwait(false);
    }
}
