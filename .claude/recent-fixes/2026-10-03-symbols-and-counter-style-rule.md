# `symbols()` and `@counter-style` (issue #685)

`list-style-type` and `counter()`/`counters()` now take an inline `symbols()` value or the name of an
author `@counter-style` (cyclic/numeric/alphabetic/symbolic/fixed/additive/extends, plus
negative/prefix/suffix/range/pad/fallback).

- **One grammar, two layers.** `CounterStyleGrammar` (CSS layer) validates `symbols()` in the converter
  and is the same code `CounterStyle` (Layer B, `Html/Core/CounterStyles/`) uses to build the style, per the
  "no two parsers for one grammar" rule. `@counter-style` descriptors are stored raw (`UnknownProperty`,
  like `@font-palette-values`) and parsed in `CounterStyle.FromRule`.
- **Formatting is document-aware only through `CssCounterEngine.FormatCounterValue(int, string, CssBox?)`.**
  The old two-argument overload still formats predefined styles only and is what the formatter calls for
  `extends <predefined>` and predefined fallbacks. The registry hangs off `HtmlContainerInt.CounterStyles`
  (null when the document has no rules, so the common case pays nothing). Margin-box `counter(page)` and the
  footnote call/marker keep the two-argument overload: they have no `@counter-style` access yet.
- **`ListStyleConverter` now accepts any custom-ident** (minus CSS-wide keywords and `inside`/`outside`, so
  `list-style: inside foo` still parses); `list-style-type: number` used to be rejected and is now valid and
  falls back to `decimal` at layout, as the spec says. The test that used `number` as an "illegal" value
  now uses `5`.
- A symbolic/alphabetic style with an explicit `range` admitting 0 must still return "unrepresentable"
  (fallback); the guard is in `Represent`.
- Default suffix for custom styles is the spec's `". "`, unlike the built-in markers' bare `"."`.
- Not done: `<image>` symbols, `speak-as`, `@counter-style` in margin-box/footnote counters.
