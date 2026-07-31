# `dir`/`bdo` now isolate their bidi resolution instead of embedding it

**Landed:** 2026-07-31 — the UA stylesheet's `[dir]` rule actually isolates instead of embedding
**Doc section:** none dedicated; the affected rules are the `dir` global attribute and `<bdo>`
compatibility-matrix entries in [docs/html-css-support.md](../../docs/html-css-support.md).
**Verified against v0.9.7:** `git show v0.9.7:src/PeachPDF/Html/Core/CssDefaults.cs` shows the UA
stylesheet's `*[DIR="ltr"]`/`*[DIR="rtl"]` rules using `unicode-bidi: embed` and `BDO[DIR="ltr"]`/
`BDO[DIR="rtl"]` using `unicode-bidi: bidi-override` at that tag — the same behavior this note
describes as "before."

An element carrying a plain `dir="ltr"`/`dir="rtl"`/`dir="auto"` attribute (any element other than
`<bdo>` or `<bdi>`, which already isolated/overrode correctly) previously resolved with
`unicode-bidi: embed`: its own directional levels could leak into how the surrounding paragraph's
neutral characters (spaces, punctuation) and European/Arabic numbers around it resolved. `<bdo dir>`
previously resolved with plain `bidi-override` (directional override, no isolation) rather than
`isolate-override`. Both now match the current HTML Standard's rendering rules — `isolate` for `dir`,
`isolate-override` for `bdo[dir]` — the same values `<bdi>` already used.

The observable difference is narrow but real: a `dir`-bearing inline element embedded in an
opposite-direction paragraph is now opaque to that paragraph's own neutral/number resolution, the
same way `<bdi>` already was. For example, `<p>1 <span dir="rtl">עברית</span> 2</p>` previously
rendered the trailing `2` immediately after `1` (the RTL span's levels influencing how the digits
resolved around it); it now keeps the paragraph's own document order (`1`, the isolated RTL run,
`2`), matching real browsers. A document relying on the old (non-isolating) digit/neutral placement
around a `dir`-bearing element will see that placement change.
