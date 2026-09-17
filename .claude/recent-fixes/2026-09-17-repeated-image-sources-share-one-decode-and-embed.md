# Repeated `<img>`/background/`<object>` sources now decode and embed once per render

Every `CssBoxImage`/`CssBoxObject`/`CssImage.Url` created its own `ImageLoadHandler`, and
`ImageLoadHandler` had no cache at all keyed by resolved source. Two `<img>` tags with the identical
`src` decoded the source twice and each got a brand-new `XImage` (`XImage.FromStream` always assigns a
fresh GUID path), so `PdfImageTable`'s existing dedup-by-`XImage`-identity never had a chance to fire —
the same source embedded as two separate PDF Image XObjects. This directly contradicted
`docs/architecture.md`'s own claim that a decoded image is cached for the lifetime of a render.

## The load-bearing idea

`HtmlContainerInt` now owns a `Dictionary<string, (RImage?, SvgDocument?)>` keyed by the resolved
absolute source URI (`RUri.AbsoluteUri` — already exactly the value used to fetch, including `data:`
URIs verbatim). `ImageLoadHandler.SetImageFromUrl` checks it before fetching, and populates it on a
successful decode. Ownership of a cached `RImage` moves to the container; `ImageLoadHandler` no longer
disposes `Image` at all (the old `_releaseImageObject` field is gone). The cache is disposed exactly
once, in `HtmlContainerInt.Dispose(bool)` — deliberately **not** in `Clear()`, since the container-query
convergence loop disposes and rebuilds the box tree wholesale between passes within one render
(`SetHtml`/`Clear()`), and a rebuilt tree's fresh handlers should still hit the cache.

Also exposed `PeachPdfDocument.ConsolidateImages()` (forwarding to the previously-unreachable
`PdfDocument.ConsolidateImages()`, which already existed but had no caller and `PdfDocument` is
internal) as a separate, opt-in tool for what the render-scoped cache can't see: a duplicate spanning
two separate `AddPdfPages`/`AddPages` calls into the same document, or two different sources that
happen to encode identical bytes. Not automatic on `Save()` — hashing every embedded image on every
save isn't a cost every document should pay by default.

## What running it found that reading it wouldn't have

Sharing one `RImage` instance across draw sites surfaced a real, independent latent bug in
`PdfSharpCore/Pdf.Advanced/PdfImageTable.cs`: `ImageSelector` (the dedup key `PdfImageTable.GetImage`
uses) identified an image by `(path, targetWidth, targetHeight)` only, not by `XImage.Interpolate`.
`BackgroundImageDrawHandler`/`BorderImageDrawHandler` toggle `Interpolate` to `false` transiently around
one draw call (forcing nearest-neighbor for a repeating tile or a border-image slice) and restore it
afterward — safe when every consumer had its own private `RImage`, but `PdfImage`/`GetImage` bakes
`/Interpolate` into the embedded object once, at whichever draw call first creates it for a given
selector, and reuses that same `PdfImage` for every later draw at the same selector regardless of what
`Interpolate` is set to on the later call. Once an `<img>` and a `background-image` of the same source
at the same display size can share one `XImage`, whichever paints first permanently decides the
`/Interpolate` value the other one gets too — a repeating tile painted first would silently force a
plain `<img>` of the same source into nearest-neighbor rendering. Fixed by folding `Interpolate` into
`ImageSelector`'s identity, so the two draws simply get their own `PdfImage` instead of contaminating
each other. This bug pre-dated this change (the same failure mode was already reachable in principle
wherever a `CssImage.Url` instance was reused across draw calls), but this change made the common case —
two independent, unrelated elements referencing the same source — trigger it far more often, so it had
to be fixed alongside the caching change rather than left for a future issue.

A second latent issue, also only visible once ownership of the decoded image moved to the container: the
new cache's disposal loop originally sat inside the same `try { } catch { }` as `Root?.Dispose()` in
`HtmlContainerInt.Dispose(bool)`. If disposing the box tree threw, the cached-image disposal loop never
ran and every image decoded during that render leaked silently (the pre-existing catch swallows
everything). Split into its own `try`/`finally` so an exception walking `Root` can't skip releasing the
images the container now owns independently of the box tree.

## What was deliberately not done

A failed image load (a 404, a timeout, a source that fails to parse) is not cached — a document
referencing the same dead URL N times still fetches it N times. This is a pre-existing inefficiency this
change doesn't touch; caching failures changes error-reporting/retry semantics in a way that deserves
its own decision, not a side effect of a caching fix.

## Evidence

`RepeatedImageDeduplicationIntegrationTests` covers: two identical `<img>` data URIs collapsing to one
`/Subtype /Image` object; two different sources staying distinct (regression guard against
over-eager caching); an `<img>` and a `background-image` of the same source sharing one embed; a
network-fetch-count assertion (via a counting `RNetworkLoader`) proving the fetch itself is skipped, not
just deduped after the fact; and `ConsolidateImages()` merging a duplicate that spans two separate
`AddPdfPages` calls. The full existing `ImageDrawingTests`/`BackgroundImageSvgIntegrationTests`/
`BorderImagePaintIntegrationTests` suites still pass unchanged after the `ImageSelector` fix, including
the ones that assert `(path, targetWidth, targetHeight)` dedup behavior directly.
