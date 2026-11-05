# Two more load-time costs of arstechnica.com: serial style sheets and repeated WOFF2 conversion

Measured with the demo's phase timings (Release, three runs each, consistent): `css.collect` 165 ms -> 66 ms, `fonts` 257 ms -> 143 ms, about 210 ms of a 2.3 s load.

**Style sheets.** `CascadeParseStyles` fetches each `<link rel=stylesheet>` in document order, awaiting each. `DomParser.PrefetchStylesheetsAsync` (before it, like the image and font prefetch) hands the
resolved addresses of every stylesheet link to a render context that implements `IResourcePrefetcher`, so the fetches overlap; the parse and the cascade order are untouched. It resolves
the address with the same `ResolveAgainstDocumentBase` the loader uses, skips `rel` values that are not a `stylesheet` token and empty `href`s, and needs two or more links to bother.

**WOFF/WOFF2 conversion.** The page declares 92 `@font-face` rules over 12 files (one rule per unicode-range and weight of a variable font). `FontSet.AddData` already answers a repeated identical
(bytes, options) pair from what it holds, but the options differ per rule, so each rule converted the same file again (WOFF2 -> OpenType was the bulk of `fonts`). `FontFormatConverter` now keeps the
conversion by content (length plus a 64-bit hash to find it, the bytes themselves to confirm it, so a collision can never return another font), under a 64 MB budget that starts over when passed.
**Trap found by the suite:** two threads converting the same file at once both missed, and the loser returned its own copy, so a test asserting the shared result failed intermittently only in the
full run; the second `lock` now returns the entry already stored.

**Not done.** Fonts whose `unicode-range` covers nothing on the page are still registered (spec asks UAs to load only the faces a page uses, css-fonts-4 section 4.8; deferring them needs an on-demand
path for text that arrives later, as in an editor), and the cascade (~0.55 s) and layout (~0.75 s) are the rest of the load.
