# A non-inherited property added to a whole-adopted style area needs its own `InheritStyle` restore entry

`CssBox.InheritStyle` (`CssBox.StyleProperties.cs`) adopts several style areas - `Font`, `Text`,
`Table`, `List`, `Pagination` - from parent to child *by reference*, in one shot, because every
property those areas hold is spec `Inherited: true` **except** a small, explicitly-named minority
(`Text`'s `unicode-bidi`, `vertical-align`, `text-overflow`, and now `line-clamp`). For that minority,
`InheritStyle` captures the child's own pre-inherit value before the whole-area adoption, then (when
`everything: false`) writes it back into the freshly-adopted area afterward - the only mechanism that
stops those properties from silently inheriting anyway.

**Adding a new `Inherited: false` property to one of these already-100%-inherited areas in
`css-properties.json` does nothing on its own to keep it non-inherited.** The property registry
generator has no notion of `InheritStyle`'s restore list and cannot cross-check it - a JSON entry's
`"inherited": false` only governs cascade-time defaulting (an unset declaration falls back to the
initial value, not the parent's computed value), which is a completely different code path from
`InheritStyle`'s whole-area-by-reference adoption. `line-clamp` shipped with `"inherited": false` in
`css-properties.json` but was not added to `InheritStyle`'s restore list, and silently inherited
through `TextArea`'s whole adoption as a result - measured concretely: `<div style="line-clamp:2">`
containing two `<p>` children clamped **each child paragraph** to 2 lines (4 lines of visible content
total), instead of clamping only the div's own direct line boxes the way Chrome does.

**When adding a property to `Font`/`Text`/`Table`/`List`/`Pagination` with `"inherited": false`**, add
it to `InheritStyle`'s existing capture-before/restore-after block for that area (see the `Text` area's
`unicode-bidi`/`vertical-align`/`text-overflow`/`line-clamp` block for the pattern) in the same change.
There is no build-time or test-time check that catches a missed entry - `CssPropertyRegistryEquivalenceTests`-style
tests prove the generator's `TrySet`/`Get` dispatch matches direct property application, not that
inheritance behaves per the property's own declared `inherited` flag, so this has to be caught by
hand until such a check exists.
