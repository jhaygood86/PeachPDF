# The remaining CSS-OM longhand classes are generated too

Follow-up to [the first generation pass](2026-10-31-cssom-longhands-generated-from-the-json.md): the 92 classes it left
hand-written (plus 8 properties that had no JSON entry at all) now come from `cssom` blocks. No `StyleProperties` file
declares a named longhand any more.

**What changed in the generator.** Every generated class exposes `ValueGrammar` (the grammar before the CSS-wide
keywords), so the 25 `cssom-grammar` validators keep resolving `X.ValueGrammar.Convert` unchanged. `cssom.globals`
(`"default"` | `"none"`) says whether `Converter` is `ValueGrammar.OrDefault()` or the grammar as it stands (flex-*,
align-*, justify-*, tab-size, opacity).

**Found by running it, not reading it.**
- `OrGlobalValue()` and `OrDefault()` are the same thing, so fill/stroke/stroke-* need no third `globals` value; the
  typed `OrDefault(x)` argument is dead data (`IdentifierValueConverter<T>` discards it), so plain `OrDefault()` is right
  wherever the hand code passed a typed initial. `CssomLonghandBaseline.txt` came out byte-identical.
- `x`, `y`, `cx`, `cy`, `r`, `rx`, `ry` are `SvgOnlyProperty` instances and `DeclarationCondition` tells those apart by
  type (`_property is ... SvgOnlyProperty`), so generating a class per name would silently change `@supports` handling.
  They are on the drift test's exclusion list with that reason; so are the two vendor aliases (never given a class).
- Eight properties (cursor, size, text-anchor, text-justify, transition-delay/-duration/-property/-timing-function) had
  no JSON entry. They now have one with `"cssDataType": "unsupported"` and no html/svg binding, so the render registry
  is unchanged and the entry carries only the CSS-OM shape and inheritance.
- A static field in `Converters` would be order-dependent (partial static initialisers across files), so the hoisted
  grammars live in a separate static class, `CssomGrammars`.
- `BorderImage*Property.TheConverter` became `ValueGrammar`; the shorthand and `BorderImageLayerResolver` use that.

**Evidence.** `CssomLonghandBaselineTests` unchanged, engine suite, `PeachPDF.Tests` and generator tests green.
