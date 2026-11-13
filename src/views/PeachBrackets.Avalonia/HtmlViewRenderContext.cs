using PeachBrackets;
using PeachDrawing.Core;
using PeachDrawing.Skia;
using PeachDrawing.Text;

namespace PeachBrackets.Avalonia;

/// <summary>The Skia context of a view, which also loads what the document refers to: images, style sheets and fonts by <c>data:</c> URI and file, and by web address when the host allows it.</summary>
internal sealed class HtmlViewRenderContext(Func<bool> allowNetwork, Func<Uri?> baseUri, FontSet fontSet)
    : SkiaRenderContext(fontSet: fontSet), IResourcePrefetcher
{
    public override Uri? BaseUri => baseUri();

    public override ValueTask<RNetworkResponse?> GetResourceStream(Uri uri) => HostResourceLoader.LoadAsync(uri, allowNetwork());

    public ValueTask PrefetchAsync(IReadOnlyList<Uri> addresses) => HostResourceLoader.PrefetchAsync(addresses, allowNetwork());
}
