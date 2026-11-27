using PeachBrackets.Media.GStreamer.Internal;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace PeachBrackets.Media.GStreamer;

/// <summary>
/// Plays the audio and video of a document through the GStreamer installed on the machine (<c>libgstreamer-1.0</c> on Linux, the GStreamer runtime on
/// Windows and macOS). Nothing is bundled: where GStreamer or a codec is missing, <see cref="CanPlayType"/> says no for what needs it and the media element
/// fails as it does with no provider. Set it in <see cref="HtmlDocumentOptions.MediaProvider"/>.
/// </summary>
public sealed class GStreamerMediaProvider : IMediaProvider
{
    private readonly MediaProviderOptions _options;

    /// <summary>Makes a provider.</summary>
    /// <param name="options">settings, or null for the defaults (real-time playback)</param>
    public GStreamerMediaProvider(MediaProviderOptions? options = null)
    {
        _options = options ?? new MediaProviderOptions();
    }

    /// <summary>Whether GStreamer is installed and starts.</summary>
    public static bool IsAvailable => GstNative.EnsureInitialized();

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

        return await GstSession.OpenAsync(request, _options, cancellationToken).ConfigureAwait(false);
    }
}
