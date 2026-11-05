using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Graphics.Canvas;
using PeachBrackets;
using PeachDrawing.Core;
using PeachDrawing.Text;
using PeachDrawing.Win2D;

namespace PeachBrackets.WinUI;

/// <summary>The Win2D context of a view, which also loads what the document refers to: images, style sheets and fonts by <c>data:</c> URI and file, and by web address when the host allows it.</summary>
internal sealed class HtmlViewRenderContext(Func<bool> allowNetwork, Func<Uri?> baseUri, CanvasDevice device, FontSet fontSet) : Win2dRenderContext(device, fontSet: fontSet), IResourcePrefetcher
{
    public override RUri? BaseUri => baseUri() is { } address ? new RUri(address) : null;

    public override ValueTask<RNetworkResponse?> GetResourceStream(RUri uri) => HostResourceLoader.LoadAsync(uri, allowNetwork());

    public ValueTask PrefetchAsync(IReadOnlyList<RUri> addresses) => HostResourceLoader.PrefetchAsync(addresses, allowNetwork());
}
