using PeachBrackets.Media.Android.Internal;

namespace PeachBrackets.Media.Android;

/// <summary>
/// Plays the audio and video of a document through the platform's own media stack (<c>MediaPlayer</c>, over <c>MediaCodec</c>), so it plays whatever the device has
/// decoders for. A video's pictures come from an <c>ImageReader</c> the player renders into while it plays by its own clock, or, with
/// <see cref="MediaProviderOptions.DeterministicClock"/>, from <c>MediaMetadataRetriever</c> at exactly the position the document's clock has reached. Set it in
/// <see cref="HtmlDocumentOptions.MediaProvider"/>; <c>PeachBrackets.Android</c>'s <c>HtmlView</c> does by default.
/// </summary>
public sealed class AndroidMediaProvider : IMediaProvider
{
    private readonly MediaProviderOptions _options;

    /// <summary>Makes a provider.</summary>
    /// <param name="options">settings, or null for the defaults (playback by the player's own clock)</param>
    public AndroidMediaProvider(MediaProviderOptions? options = null)
    {
        _options = options ?? new MediaProviderOptions();
    }

    /// <summary>Whether the program runs on Android, which is where this provider plays.</summary>
    public static bool IsAvailable => OperatingSystem.IsAndroidVersionAtLeast(24);

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

        return await AndroidMediaSession.OpenAsync(request, _options, cancellationToken).ConfigureAwait(false);
    }
}
