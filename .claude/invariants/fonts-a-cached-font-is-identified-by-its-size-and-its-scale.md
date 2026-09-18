# A cached font's identity is its size **and** its `PixelsPerPoint`

`RAdapter.GetFont(family, size, ...)` takes `size` in layout units (points × `PixelsPerPoint`); the font behind it is
built at `size / PixelsPerPoint` points. Two requests with the same `size` under different scales are different fonts,
so **every font cache must carry `RAdapter.FontSizeScale` in its key** — `FontsHandler._fontsCache` (its size level),
`_codepointFontsCache` and `_systemFallbackFontsCache`. A new font cache keyed by size alone reintroduces the bug.

The measured symptom is a reused `PdfGenerator` rendering 10pt text at 9.819pt `Tf` after an earlier `ShrinkToFit`
render, which shrinks or re-wraps whatever is laid out next and makes a document's output depend on what was rendered
before it (see the recent-fixes entry `2026-09-18-font-caches-are-keyed-by-pixels-per-point.md`).

Do not fix a scale-dependent cache by clearing it on a scale change: that discards the whole cache at the start of every
render of a reused generator. Key it. The one `ClearFontCache()` at the `ShrinkToFit` rescale in `PdfGenerator` stays
because it bounds the cache, not because correctness needs it.
