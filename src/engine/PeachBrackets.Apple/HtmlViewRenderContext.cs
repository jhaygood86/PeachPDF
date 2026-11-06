using PeachDrawing.CoreGraphics;
using PeachDrawing.Core;

namespace PeachBrackets.Apple;

/// <summary>The CoreGraphics context of a view, which also loads what the document refers to: images, style sheets and fonts by <c>data:</c> URI and file, and by web address when the host allows it.</summary>
internal sealed class HtmlViewRenderContext(Func<bool> allowNetwork, Func<Uri?> baseUri, bool useGpu)
    : CoreGraphicsRenderContext(imageCodec: CoreGraphicsImageCodec.Auto, useGpu: useGpu)
{
    // What a style sheet the parser meets resolves against, since the document's own <base> is not known until the parse is over.
    public override RUri? BaseUri => baseUri() is { } address ? new RUri(address) : null;

    public override ValueTask<RNetworkResponse?> GetResourceStream(RUri uri) => HostResourceLoader.LoadAsync(uri, allowNetwork());
}
