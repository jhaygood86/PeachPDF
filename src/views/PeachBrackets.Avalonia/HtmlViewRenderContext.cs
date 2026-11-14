using PeachBrackets;
using PeachDrawing.Core;
using PeachDrawing.Skia;
using PeachDrawing.Text;

namespace PeachBrackets.Avalonia;

/// <summary>The Skia context of a view, which also loads what the document refers to: images, style sheets and fonts by <c>data:</c> URI and file, and by web address when the host allows it.</summary>
internal sealed class HtmlViewRenderContext(FontSet fontSet) : SkiaRenderContext(fontSet: fontSet), IResourcePrefetcher
{
    // A load reads these from a pool thread, where a styled property of the control cannot be read: the control copies them here, on the UI thread, when it starts a load.
    private volatile bool _allowNetwork;
    private volatile Uri? _baseUri;

    /// <summary>Takes in what the control's properties say, for the load that is about to start.</summary>
    public void Configure(bool allowNetwork, Uri? baseUri)
    {
        _allowNetwork = allowNetwork;
        _baseUri = baseUri;
    }

    public override Uri? BaseUri => _baseUri;

    public override ValueTask<RNetworkResponse?> GetResourceStream(Uri uri) => HostResourceLoader.LoadAsync(uri, _allowNetwork);

    public ValueTask PrefetchAsync(IReadOnlyList<Uri> addresses) => HostResourceLoader.PrefetchAsync(addresses, _allowNetwork);
}
