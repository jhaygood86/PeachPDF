# A GSUB contextual/chaining-context lookup can now target another contextual/chaining-context lookup

**Landed:** 2026-09-05 — Add Bengali/Gujarati/Tamil Universal Shaping Engine support
**Doc section:** docs/html-css-support.md § [Text shaping](../../docs/html-css-support.md#text-shaping)

A GSUB Lookup Type 5/6 (contextual/chaining-context substitution) rule's own `SequenceLookupRecord`
can name any lookup type as its target, per the OpenType spec — including another contextual or
chaining-context lookup. PeachPDF previously applied only lookup types 1/2/3/4 (single/multiple/
alternate/ligature substitution) as a nested target and silently skipped a nested type 5/6, leaving
the matched glyph at its default, un-substituted form. Nested contextual lookups now recurse (up to
the same depth guard that already existed against a pathological font), so a font whose own feature
resolves a glyph's final presentation form through a *chain* of independently-classed contextual
lookups — a real pattern, found in a real Noto Sans Gujarati font's `abvs` feature, that narrows a
pre-base matra's glyph variant by successively more specific context — now renders the font's own
intended glyph instead of a generic fallback. This affects any document using a font authored with
this pattern, regardless of script; it was found and fixed while extending Universal Shaping Engine
support to Bengali/Gujarati/Tamil, but the fix itself is script-agnostic GSUB engine behavior.
