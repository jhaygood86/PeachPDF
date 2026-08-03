# `column-gap`/`row-gap`'s JSON initial value is `0`, not spec's `normal`, and `normal` itself isn't parseable

Tracking issue: [#606](https://github.com/jhaygood86/PeachPDF/issues/606).

Per [CSS Box Alignment Module Level 3](https://www.w3.org/TR/css-align-3/#column-row-gap),
`column-gap`/`row-gap`/`gap`'s initial value is the keyword `normal` (grammar `normal |
<length-percentage [0,∞]>`), which computes to `0` for flex/grid containers and a UA-convention
value (commonly `1em`) for multicol — not a single literal `0` in every context.

`css-properties.json`'s `column-gap`/`row-gap` entries (`html.area: "FlexArea"`, `propertyPath:
"FlexColumnGap"`/`"FlexRowGap"`) declare `initialValue: "0"` and `cssDataType: "length"` with no
keyword clause, so the literal keyword `normal` is accepted by neither real cascade dispatch nor
`@supports`. `docs/html-css-support.md`'s multicol `column-gap` row (line 572) already correctly
documents the real `1em`-for-multicol-vs-`0`-for-flex/grid split at the C# level
(`ColumnGapProperty.cs`/`RowGapProperty.cs`'s own `OrDefault(...)` fallbacks, which this JSON entry
has no bearing on) — this gap is specifically in the JSON's own `initialValue` metadata field (used
for computed-style/revert purposes) and its grammar, not in rendered output.

**Deliberately out of scope.** Fixing this cleanly means widening `cssDataType` to include a `normal`
keyword clause (so `@supports (column-gap: normal)` reports true and `initialValue` can legitimately
read `"normal"`) without changing what real dispatch stores for the common numeric case — plausibly
another `supportsDataType`-shaped split, but the underlying gap value differs by layout mode
(flex/grid vs multicol) in a way a single JSON entry's `initialValue` field can't represent, and this
JSON entry covers only the flex/grid storage path (`FlexColumnGap`/`FlexRowGap`), not multicol's
separate `column-gap` handling — untangling that is a real design question, not a mechanical fix.
