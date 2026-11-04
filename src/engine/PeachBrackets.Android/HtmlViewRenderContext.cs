using System;
using System.Threading.Tasks;
using PeachBrackets;
using PeachDrawing.Core;
using PeachDrawing.Skia;

namespace PeachBrackets.Android;

/// <summary>The Skia context of a view, which also loads what the document refers to: images, style sheets and fonts by <c>data:</c> URI and file, and by web address when the host allows it.</summary>
internal sealed class HtmlViewRenderContext(Func<bool> allowNetwork) : SkiaRenderContext
{
    public override ValueTask<RNetworkResponse?> GetResourceStream(RUri uri) => HostResourceLoader.LoadAsync(uri, allowNetwork());
}
