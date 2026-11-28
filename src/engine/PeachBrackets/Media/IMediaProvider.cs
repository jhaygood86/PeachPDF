using PeachDrawing.Core;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace PeachBrackets.Media;

/// <summary>
/// What a host gives a document so that its <c>audio</c> and <c>video</c> elements can play: a decoder. PeachBrackets decodes no media itself. A host
/// implements this over the platform's player (Media Foundation, AVFoundation, Android's media stack, GStreamer) and sets it in
/// <see cref="HtmlDocumentOptions.MediaProvider"/>; with none set, a <c>video</c> shows its poster and a script's media elements fail as they always have.
/// </summary>
public interface IMediaProvider
{
    /// <summary>What the provider can play, in the words of <c>HTMLMediaElement.canPlayType</c>.</summary>
    /// <param name="mimeType">a media type, with <c>codecs</c> if the caller names them</param>
    /// <returns>an empty string for no, <c>"maybe"</c> or <c>"probably"</c></returns>
    string CanPlayType(string mimeType);

    /// <summary>Opens a resource.</summary>
    /// <param name="request">what to open</param>
    /// <param name="cancellationToken">stops opening</param>
    /// <returns>the session, or null when the resource cannot be played</returns>
    ValueTask<IMediaSession?> OpenAsync(MediaRequest request, CancellationToken cancellationToken = default);
}

/// <summary>What a media element asks a provider to open.</summary>
public sealed class MediaRequest
{
    private readonly Func<CancellationToken, ValueTask<RNetworkResponse?>> _load;

    /// <summary>Makes a request.</summary>
    /// <param name="address">the absolute address of the resource</param>
    /// <param name="kind">the kind of element asking</param>
    /// <param name="renderContext">the document's render context, whose graphics device a provider that makes textures draws on</param>
    /// <param name="load">reads the resource through the document's resource loader</param>
    public MediaRequest(Uri address, MediaKind kind, RenderContext renderContext, Func<CancellationToken, ValueTask<RNetworkResponse?>> load)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(renderContext);
        ArgumentNullException.ThrowIfNull(load);
        Address = address;
        Kind = kind;
        RenderContext = renderContext;
        _load = load;
    }

    /// <summary>The absolute address of the resource, as the document's resource loader takes it (a <c>blob:</c> address is one).</summary>
    public Uri Address { get; }

    /// <summary>The kind of element asking.</summary>
    public MediaKind Kind { get; }

    /// <summary>The document's render context.</summary>
    public RenderContext RenderContext { get; }

    /// <summary>The media type the document declared (a <c>source</c> element's <c>type</c>), or null.</summary>
    public string? ContentType { get; init; }

    /// <summary>The element's <c>crossorigin</c> attribute: null when it has none, else <c>anonymous</c> or <c>use-credentials</c>.</summary>
    public string? CrossOrigin { get; init; }

    /// <summary>Whether the element is muted.</summary>
    public bool Muted { get; init; }

    /// <summary>Whether the element loops.</summary>
    public bool Loop { get; init; }

    /// <summary>The element's <c>preload</c>.</summary>
    public MediaPreload Preload { get; init; } = MediaPreload.Metadata;

    /// <summary>Reads the resource through the document's resource loader (the data, headers and redirects of a <see cref="RenderContext.GetResourceStream"/>).</summary>
    /// <param name="cancellationToken">stops reading</param>
    /// <returns>the response, or null when the loader has none</returns>
    public ValueTask<RNetworkResponse?> LoadAsync(CancellationToken cancellationToken = default) => _load(cancellationToken);
}
