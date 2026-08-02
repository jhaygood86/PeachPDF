# `column-span: all` is parsed and stored but has no layout effect

Tracking issue: [#602](https://github.com/jhaygood86/PeachPDF/issues/602).

Per [CSS Multi-column Layout Module Level 1 §3](https://www.w3.org/TR/css-multicol-1/#propdef-column-span),
`column-span: all` should make an element span all columns of its nearest multi-column ancestor,
breaking the column flow into segments before and after it. `column-span` is parsed and stored on the
computed style (`ColumnSpanProperty`/`ColumnSpanConverter`, `ComputedStyleAreas.MultiColumnArea.ColumnSpan`
via `CssBox.ColumnSpan`), but `CssLayoutEngineColumns.cs` never reads it — a `column-span: all` element
lays out as an ordinary in-flow box inside its column, with no spanning behavior at all.

`docs/html-css-support.md`'s `column-span` row already discloses this ("Parsed and accepted but has no
effect"). `css-properties.json`'s `column-span` entry splits `cssDataType` (`["none", "all"]` — permissive,
matches what real dispatch actually stores) from `supportsDataType` (`["none"]` only), so
`@supports (column-span: all)` correctly reports unsupported while `column-span: all` itself is still
accepted and round-tripped by the cascade — the same shape as `break-before`'s `region`/`avoid-region`
split.

**Deliberately out of scope.** Fixing this means implementing spanning-column layout in
`CssLayoutEngineColumns.cs`: detecting a `column-span: all` descendant, splitting the column flow around
it, and laying it out at the full multi-column container's content width — a real, separate layout
feature, not a JSON/`@supports`-accuracy fix.
