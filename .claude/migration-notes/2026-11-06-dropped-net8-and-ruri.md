# .NET 8 support dropped; `RUri` replaced by `System.Uri`

**Before:** every package targeted `net8.0` and `net10.0` (plus `net11.0` under the .NET 11 SDK). Resource loading used `PeachDrawing.Core.RUri`, a wrapper over `System.Uri` that held `data:` URIs as raw strings on .NET 8 because `System.Uri` rejected strings over 65,519 characters there.

**Now:** only `net10.0` (and `net11.0`) are built and supported; the 0.9.x series remains the one that supports .NET 8. `RUri` is gone: `RNetworkLoader.GetResourceStream(Uri)`, `RNetworkLoader.BaseUri`, `RenderContext.GetResourceStream(Uri)`/`BaseUri`/`AddFontFamilyFromUrl(..., Uri baseUri)`, `HostResourceLoader.LoadAsync(Uri, ...)`/`PrefetchAsync(IEnumerable<Uri>, ...)`, `IResourcePrefetcher.PrefetchAsync(IReadOnlyList<Uri>)` and `RasterRenderContext.ResourceLoader` take and return `System.Uri`. A custom loader changes the parameter/property types and drops any `.Uri` unwrap (`uri.Uri.LocalPath` becomes `uri.LocalPath`); `new RUri(x)` becomes `new Uri(x)`.

**Why:** .NET 10's `System.Uri` has no length ceiling and round-trips `data:` URIs, so the wrapper no longer earns its keep (issue #74). One difference: `Uri.AbsoluteUri` percent-escapes a non-base64 `data:` payload (a space becomes `%20`); read `OriginalString` for the payload as written.
