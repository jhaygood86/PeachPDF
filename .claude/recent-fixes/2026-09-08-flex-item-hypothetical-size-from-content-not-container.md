# A flex item's hypothetical main size comes from its content, not from its laid-out width

`MeasureItem`'s main-axis-physical-X branch derived an item's max-content width from its
*laid-out* width (`naturalMain`) whenever the item was not inlines-only. That is correct for a
replaced leaf, and wrong for a box with block children: an auto-width block child fills its
containing block during layout, so `naturalMain` is the flex CONTAINER's width, not the item's
content. Every item then measured the container, the free space `justify-content` distributes came
out as zero, and the result read as an even split.

The load-bearing idea is that the item has a real intrinsic measurement available already —
`CssLayoutEngine.GetMinContentWidth`/`GetMaxContentWidth`, the same pair the float sizing a few
lines above relies on, both returning outer widths, which is what `hypothetical` is.

## What running it turned up

- **The clamp order is not interchangeable.** The first version wrote
  `min(max(max-content, min-content), available)`, which — given the invariant max-content ≥
  min-content — collapses to `min(max-content, available)` and has no min-content floor at all.
  CSS 2.1 §10.3.5 is `min(max(min-content, available), max-content)`: min-content is the LOWER
  bound. The two agree everywhere except the case that matters, an item whose own min-content
  exceeds the line, where the correct answer is to keep min-content and overflow. Pinned by
  `FlexboxIntegrationTests.Item_WhoseMinContentExceedsTheRow_KeepsItsMinContent`, which needs
  `flex-shrink: 0` — with shrink left on, the shrink pass clamps the item back to the line either
  way and the fixture says nothing about the clamp.
- **`GetFitContentWidth` is not the helper to reach for here**, despite the name. It is
  `min(GetLargestChildWidth(max-content), available)` — no min-content floor either — so it has the
  same gap. An earlier comment in this method cited it while the code called something else; both
  now say the same thing.
- **A hanging space is worth 3.07pt at the default font**, and it was being counted. `LineContentWidth`
  trims it per css-text-3 §4.1.2. The observable symptom is an item box wider than anything drawn
  inside it: 0.01pt of slack (the deliberate anti-rounding epsilon) with the trim, 3.07pt without.
  That difference is what `WrappedItem_DoesNotCountTrailingWhiteSpaceInItsMeasure` asserts — an
  earlier version of that test put the text in a block child, which takes the other measurement
  branch entirely, and passed with the trim reverted.

## Evidence

Three fixtures in `FlexboxIntegrationTests.cs`, each mutation-checked: reverting
`CssLayoutEngineFlex.cs` to `main` fails two of them; reverting only the clamp order fails the
min-content one; reverting only `LineContentWidth`'s trim fails the trailing-space one. Full suite
10,201 passed / 0 failed / 9 skipped on net8.0; diff coverage 100% on the changed lines.

## Deliberately not done

The grid-container fallback (`box.DerivedStyle.ActualDisplay is Keywords.Grid or InlineGrid` →
`naturalMain`) is left as it was. A nested grid places its children on tracks the intrinsic walk
knows nothing about, so measuring it this way would report a number with no relationship to how it
lays out; that needs the grid engine's own intrinsic sizing, which is a separate piece of work.
