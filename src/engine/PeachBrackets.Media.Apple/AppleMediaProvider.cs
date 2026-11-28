using AVFoundation;
using Foundation;
using PeachBrackets.Media.Apple.Internal;

namespace PeachBrackets.Media.Apple;

/// <summary>
/// Plays the audio and video of a document through AVFoundation, so it plays what macOS or iOS can. A video's pictures come from an
/// <c>AVPlayerItemVideoOutput</c> while the player plays by its own clock, or, with <see cref="MediaProviderOptions.DeterministicClock"/>, from an
/// <c>AVAssetImageGenerator</c> at exactly the position the document's clock has reached. Set it in <see cref="HtmlDocumentOptions.MediaProvider"/>;
/// <c>PeachBrackets.Apple</c>'s <c>HtmlView</c> does by default.
/// </summary>
public sealed class AppleMediaProvider : IMediaProvider
{
    private static readonly string[] s_codecs = ["avc1", "avc3", "hvc1", "hev1", "mp4a", "mp3", "flac", "alac", "ac-3", "ec-3"];

    private readonly MediaProviderOptions _options;

    /// <summary>Makes a provider.</summary>
    /// <param name="options">settings, or null for the defaults (playback by the player's own clock)</param>
    public AppleMediaProvider(MediaProviderOptions? options = null)
    {
        _options = options ?? new MediaProviderOptions();
    }

    /// <summary>Whether the program runs on macOS or iOS, which is where this provider plays.</summary>
    public static bool IsAvailable => OperatingSystem.IsMacOS() || OperatingSystem.IsIOS();

    /// <inheritdoc />
    public string CanPlayType(string mimeType)
    {
        ArgumentNullException.ThrowIfNull(mimeType);
        if (!IsAvailable)
            return "";

        var parts = mimeType.Split(';');
        var container = parts[0].Trim();

        // AVFoundation lists the containers it opens; the codecs it plays are the ones every Apple platform has (a hardware-dependent one, VP9 and AV1, is left out).
        if (!Array.Exists(AVUrlAsset.AudiovisualMimeTypes ?? [], t => string.Equals(t, container, StringComparison.OrdinalIgnoreCase)))
            return "";

        var codecs = parts.Skip(1)
            .Select(p => (Index: p.IndexOf('='), Text: p))
            .Where(p => p.Index > 0 && string.Equals(p.Text[..p.Index].Trim(), "codecs", StringComparison.OrdinalIgnoreCase))
            .SelectMany(p => p.Text[(p.Index + 1)..].Trim().Trim('"', '\'').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToArray();
        if (codecs.Length == 0)
            return "maybe";

        return codecs.All(c => s_codecs.Any(prefix => c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) ? "probably" : "";
    }

    /// <inheritdoc />
    public async ValueTask<IMediaSession?> OpenAsync(MediaRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsAvailable)
            return null;

        return await AppleMediaSession.OpenAsync(request, _options, cancellationToken).ConfigureAwait(false);
    }
}
