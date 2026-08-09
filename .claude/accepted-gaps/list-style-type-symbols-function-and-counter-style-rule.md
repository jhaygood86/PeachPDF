# `list-style-type`: `symbols()` function and author-defined `@counter-style` not supported

The `symbols()` functional notation (`<counter-style> = <counter-style-name> | <symbols()>`) and the
`@counter-style` at-rule itself are both unimplemented. `@counter-style` is recognized only as a bare
`RuleType.CounterStyle` token with no model/behavior - `ParserExtensions.cs` documents it falling
through to the null default alongside `Unknown`/`RegionStyle`/`FontFeatureValues`. A `list-style-type`
referencing `symbols(...)` or an author-defined `@counter-style` name falls back to plain `decimal`
numbering (the standard "unknown style" fallback, CSS Counter Styles Level 3 §2).

`symbols()` is effectively inline `@counter-style` infrastructure - implementing it well means
implementing the shared `cyclic`/`numeric`/`alphabetic`/`symbolic`/`fixed` system dispatch that
`@counter-style` itself needs (plus `range`, `pad`, `fallback`, `negative`, `prefix`/`suffix`
descriptors), which is a separate, larger feature from filling out the *predefined* counter-style
keyword list (see [docs/html-css-support.md](../../docs/html-css-support.md#lists) for what is
supported). Tracked as [issue #685](https://github.com/jhaygood86/PeachPDF/issues/685).
