# `InheritStyle`'s "everything" branch now actually copies `Bottom`/`Right`

Found incidentally while splitting `CssBoxProperties` into `ComputedStyle`/`DerivedStyle`
(`src/PeachPDF/Html/Core/Dom/ComputedStyle.cs`, `DerivedStyle.cs`, `CssBox.StyleProperties.cs`), not
sought out - the storage-unification exposed a pre-existing dead-field bug rather than introduced one.

## What was true

`CssBoxProperties.InheritStyle`'s `everything: true` branch (used only for structural duplicates of the
same source box - `CssProxyBox`'s repeated `<thead>`/`<tfoot>` clone, `DomParser.CorrectBlockSplitBadBox`'s
inline/block split) copied two private fields, `_bottom`/`_right`. But `Bottom`/`Right`'s real
getters/setters were independent plain auto-properties that never read from or wrote to those fields -
they were dead storage. So a structural duplicate's `Bottom`/`Right` silently never inherited the source
box's value, always keeping the CSS initial `"auto"`, even though CSS 2.1 §9.4.3 says a
relatively/absolutely positioned box's offsets should carry over here, the same way `Left`/`Top`/`Width`/
`Height` already correctly did a few lines above.

## The load-bearing idea

Splitting cascaded storage onto one `ComputedStyle` record meant `Bottom`/`Right` no longer have a
"real" auto-property separate from a "dead field" - there's exactly one place a value can live. Rewriting
`InheritStyle` to copy `_computedStyle.Bottom`/`.Right` from the parent's `ComputedStyle` (the same pattern
used for every other structurally-duplicated property) therefore fixes the bug as a side effect of the
storage move, not as a deliberate, separately-scoped change.

## Deliberately kept, not reverted

A post-change review pass flagged this as an unreviewed, untested behavior change riding inside a
refactor - correct to flag, since nothing in the 6974-test suite (nor the new `ComputedStyleTests.cs`)
asserted `Bottom`/`Right` through `InheritStyle` before this. Rather than reverting to the historical bug,
the fix is kept and pinned with a dedicated test,
`ComputedStyleTests.InheritStyle_Everything_CopiesBottomAndRight`, which fails against the pre-refactor
`CssBoxProperties` and passes now.

## Evidence

Full net8.0 suite: 6975 passed / 0 failed / 9 skipped. Zero-warning `dotnet build PeachPDF.slnx -t:Rebuild`.
Diff coverage 97% (gate is 90%).
