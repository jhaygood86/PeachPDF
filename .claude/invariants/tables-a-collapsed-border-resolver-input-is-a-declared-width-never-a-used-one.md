# A collapsed-border resolver input is a declared width, never a used one

`CollapsedBorderModel`'s candidate collection must read `CssBox.NaturalBorder*Width` — the declared
`border-*-width` — and never `ActualBorder*Width`, for every candidate, on every path, including
`Resolve`'s own.

**Why the cached property is the wrong one, even though it looks equivalent.**
`CssLayoutEngineTable.ApplyCollapsedUsedBorderWidths` calls
`DerivedStyle.SetCollapsedUsedBorderWidths`, which overwrites the `ActualBorder*Width` cache with the
box-model *used* half-width — half the resolved grid line — so that `ClientLeft`/`ClientTop`, content
insets, `GetAvailableCellWidth` and `GetWidthSum` all see the used value with no call-site change.
That override is deliberately sticky: `_hasCollapsedUsedBorderWidths` makes the four
`InvalidateBorder*Width` calls no-ops, and `ClearCollapsedUsedBorderWidths` only runs for a box that
has *stopped* participating in a collapsed table. So it outlives the pass that set it.

A §17.6.2 resolution is that override's **input**. Reading the cache therefore feeds the previous
resolution's output straight back into the next one.

**The measured symptom.** A declared `border: 12pt` collapsed grid line resolves to 12pt on the
table's first layout pass, **6pt on the second, 3pt on the third**, with the cells shrinking to match
— the table renders visibly smaller than its `border-collapse: separate` twin. It is silent: no
exception, no assert, and a rendered PDF that simply looks a bit tight.

**Why it will not show up in a small test.** One pass is the common case, and every minimal repro
renders correctly — the table alone, the table after a tall spacer, the table on page 2 of a two-page
document. Re-entry comes from elsewhere in the document (`ShrinkToFit`, a CSS Fragmentation 3 §4.3
relocation, a per-page-width reflow), so the trigger is non-monotonic in the markup: bisecting a real
document found one section prefix that halved and a *longer* prefix that did not. A change here is
checked with `LayoutHarness.LayoutRepeatedlyAsync`, which lays the same tree out N times and lets the
test assert the result is pass-independent — not by constructing markup that "should" re-enter.

The same reasoning applies to any future consumer that resolves against a box's border width while
that box is a collapse participant. If the value is an input to the used-width override, it reads the
declared width.

See [../recent-fixes/2026-09-22-a-collapsed-grid-line-resolves-from-its-declared-width.md](../recent-fixes/2026-09-22-a-collapsed-grid-line-resolves-from-its-declared-width.md)
(will age out) and `CollapsedBorderModelIntegrationTests.RelaidOutCollapsedTable_ResolvesTheSameLineWidthsOnEveryPass`,
which is the pin.
