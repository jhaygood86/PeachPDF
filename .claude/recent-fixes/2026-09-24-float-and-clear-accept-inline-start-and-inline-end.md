# `float` and `clear` accept `inline-start` and `inline-end` (#1285)

**What the spec says** ([CSS Logical Properties §2.2](https://www.w3.org/TR/css-logical-1/#float-clear),
[Writing Modes 4 §6.4](https://www.w3.org/TR/css-writing-modes-4/#logical-to-physical)): the mapping "uses the
writing mode of the element's containing block", `inline-start` is line-left when that block's used `direction`
is `ltr` and line-right when `rtl`, and the computed value is the specified keyword.

**Load-bearing choices**

- **Resolved when layout asks, never rewritten.** `CssBox.EffectiveFloatSide` gains two arms and a new
  `CssBox.EffectiveClear` resolves `clear`, both through `ResolveInlineSide`, which reads
  `ContainingBlock.Direction`. The property keeps `InlineStart`/`InlineEnd` (and `float.ToString()` still says
  `inline-start`), because the containing block's direction is a cascaded value that layout reads and `inherit`
  or a changed `direction` on the parent has to change the answer. The same reason `inside`/`outside` are
  already computed lazily.
- **The containing block's direction, not the box's own.** An `ltr` float inside an `rtl` parent goes to the
  parent's inline-start side, the right. Pinned by a test, since reading the box's own `direction` is the easy
  mistake.
- **No writing-mode lookup.** Because line-left/line-right are what `left`/`right` already mean, and inline-start
  is line-left for `ltr` in *every* writing mode, the answer is `direction` alone. `LogicalPropertyResolver`
  answers a physical side (top/bottom in a vertical mode), which is the wrong currency for the float
  algorithms and would have needed a second mapping back.
- **`clear` compares resolved sides.** `ClearBox` used to compare the raw `ClearMode` with
  `EffectiveFloatSide`; it now passes `EffectiveClear`, so `float: inline-end` next to `clear: inline-start`
  line up whatever the direction, and `GetClearance`'s recursion is handed the resolved value too.
- `IsFloated` includes the new values, which is what gates blockification, `IsOutOfFlow`, the inline-run
  handling in `DomParser` and `FloatBox`'s switch.

**Not done:** `text-orientation: upright` forces the *used* direction to `ltr` (Writing Modes 4), which this
engine does not model for floats; `direction` is read as computed. Floats in a vertical writing mode remain
placed by the physical-axis path until [the vertical float work](../accepted-gaps/no-vertical-writing-mode-layout.md).

**Also found:** text beside a right float in an `rtl` block is drawn under the float even for a physical
`float: right`, so it is the same as the justified-text defect (#1343), not something the logical keywords add.

Evidence: 15 new tests (parse for both properties, four direction/keyword placements, computed value kept,
containing block's direction, clear on each side, the float/clear pairing, wrapping identical to the physical
keyword); full net8.0 suite green.
