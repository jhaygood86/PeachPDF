# `hyphens: auto` now follows the nearest ancestor `lang`, not only `<html lang>`

**Landed:** 2026-09-04 — OpenType shaping completion (GPOS, GDEF, GSUB 2/5-8, per-element language)
**Doc section:** docs/html-css-support.md § [`hyphens: auto` language coverage](../../docs/html-css-support.md#hyphens-auto-language-coverage)

`hyphens: auto` used to resolve its hyphenation-pattern language once for the whole document, from
`<html lang="...">` (or `PdfGenerateConfig.DefaultLanguage` as a fallback) — an element with its own
`lang` attribute, or one nested inside a `lang`-bearing ancestor other than `<html>`, was hyphenated
using the *document's* language regardless. Language resolution now follows the HTML Living
Standard's "language of a node" algorithm (`CssBox.Language`): an element's own `lang` attribute if
set, else its nearest ancestor's, else `<html lang>`, else the same config fallback as before -
matching real browsers' per-element hyphenation-dictionary selection. A document that mixes
languages via `lang` attributes on nested elements (e.g. a French quotation inside an English
article) now hyphenates each part with its own language's patterns instead of the whole document's.
A document with a single `lang` (the common case) is unaffected.
