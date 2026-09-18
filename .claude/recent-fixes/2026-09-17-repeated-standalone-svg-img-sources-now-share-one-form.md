# Repeated standalone SVG `<img>` sources now share one Form XObject

Two independent `<img src="...svg">`/`background-image`/`border-image` elements referencing the exact
same standalone SVG resource each got their own `ImageLoadHandler`, and each handler parsed its own
`SvgDocument` via `SvgTreeBuilder.Build`. `SvgRenderer`'s Form XObject cache
(`src/PeachPDF/Svg/SvgRenderer.cs:38-39`) is keyed on `(SvgDocument document, double width, double
height, double pixelsPerPoint)`, comparing `SvgDocument` by reference identity — so two elements with
an identical resolved source and identical rendered size still missed the cache and each got their own
PDF Form XObject, even though the drawn content was byte-identical. `PdfDocument.ConsolidateImages()`
didn't help: it only walks `/Subtype /Image` objects, never `/Subtype /Form`. This was flagged as a
known, deliberate limitation when the per-instance Form cache was first built —
[color-glyph-artwork-drawn-once-into-a-form-xobject](2026-09-10-color-glyph-artwork-drawn-once-into-a-form-xobject.md)
says outright that "the resource URL is what would additionally share one logo across separate `<img>`
elements" — and the gap was still open as of
[svg-form-reuse-showcase-and-how-its-saving-was-measured](2026-09-14-svg-form-reuse-showcase-and-how-its-saving-was-measured.md),
which only covers one element repainted across pages, not separate elements sharing one source.

## The load-bearing idea

No change was needed in `SvgRenderer.cs` at all. The general resolved-source cache added by
[repeated-image-sources-share-one-decode-and-embed](2026-09-17-repeated-image-sources-share-one-decode-and-embed.md)
(`HtmlContainerInt`'s `Dictionary<string, (RImage?, SvgDocument?)>`, keyed by resolved absolute source
URI and shared by every `ImageLoadHandler` the render creates) already makes two elements resolving to
the same source share one `SvgDocument` *instance* — and a shared instance is exactly what
`SvgRenderer`'s existing instance-keyed Form cache needed to start hitting across separate elements, not
only across repeated pages for one element. The two issues (#1172 for raster dedup, #1173 for this SVG
Form angle) were filed and worked in parallel by separate sessions; coordinating early meant #1173
needed no source changes of its own once #1172's cache landed, only regression coverage proving the
consequence for the Form-XObject case specifically (the raster fix's own tests only assert
`/Subtype /Image` counts, never `/Subtype /Form`).

## What was verified, and how

New tests in `SvgFormReuseTests.cs`: two `<img>` elements with the identical SVG `data:` source on one
page collapsing to one `/Subtype /Form` with two placements; an `<img>` and a `background-image` of the
identical source doing the same (proving the fix is general at the `ImageLoadHandler` choke point, not
`<img>`-specific); and a kept negative case, two `<img>` elements with *different* sources staying as
two distinct Forms (regression guard against over-eager sharing). All three were run red against `main`
(both positive cases failed with 2 Forms instead of 1) before #1172 landed, then green against its
branch, confirming the mechanism actually produces the effect rather than merely not regressing it.

## What was deliberately not done

Inline `<svg>` (`CssBoxSvg`) was not touched and needed no changes — it never goes through
`ImageLoadHandler`/`LoadSvgFromStream`, so it stays identity/context-sensitive exactly as
`SeparateInlineSvgCurrentColors_KeepDistinctForms` already proved before this change. A cache keyed by
SVG content hash (sharing a Form across two *different* resolved sources that happen to render
identical output) was explicitly out of scope, per the issue's own reasoning — resolved-source identity
is enough to fix the reported gap, and content hashing raises correctness questions (byte-identical SVG
loaded from two different base URIs can still resolve nested `<image href>`/`@import` differently) this
change doesn't need to answer.

## Evidence

`SvgFormReuseTests.cs`: 9/9 passing, including the 3 new/extended cases above. Full targeted suite
(`--framework net8.0`) green. The `svg_form_reuse` TestHarness showcase was extended with a same-page
repeated-`<img>` icon row and re-rasterized to confirm the new Form-sharing case renders correctly, not
just that the object count dropped.
