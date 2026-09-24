# A multi-column container's floats are laid out in their own column, and it contains them (#1203)

## What was wrong, found by probing rather than by reading the issue

The issue names one symptom (an all-float container leaves every float at zero geometry). Probing four shapes
found three separate defects behind "floats in multicol are broken":

1. **All-float container** - `CssLayoutEngineColumns.Layout` returned at `children.Count == 0`, before anything
   laid the floats out. Fixed by laying out the container's floats in its first column
   (`LayoutOutOfFlowChildrenOnly`), computing the column width first.
2. **A float in a column was resolved against the whole container** - `LayoutOutOfFlowChildrenAgain` re-lays
   every out-of-flow child at the container's own width "because they resolved against a column", which is
   right for an absolutely positioned child (its containing block is the container) and wrong for a float
   (css-multicol-1 §2: a float belongs to the column box it appears in). A `float: right` landed at the
   container's right edge whichever column it was in. It now re-lays out absolutely positioned children only.
3. **A float in one column shortened the lines beside it in another** - `FindNarrowestRightFloatBox` and
   `GetIntersectingInlineFloat` test only the block axis, so a right float at the top of column 1 gave column
   2's first rows (same Y) a wrap limit left of the column: one word per line. A right float that lies wholly
   left of the line's content edge is now not beside it. Left floats never had this problem (their test is a
   point collision at the cursor's X).

## The container contains its floats, but is not a float-scan boundary

css-multicol-1 §2 makes a multi-column container an independent formatting context, and `ApplyHeight` already
applies CSS 2.1 §10.6.7 to such a box, so the container's auto height covers its floats once
`DomUtils.ContainsItsFloats` (a formatting-context root, or a multi-column container) is what it asks. That is
deliberately a separate predicate from `EstablishesIndependentFormattingContext`. The first attempt added multi-column to
that one, and the review found what it also drives: the float scans stop at it (so text in the columns stopped
wrapping beside a float that precedes the container - nothing here narrows or moves an in-flow multi-column
container beside a float, so the line narrowing is all it has) and `CollapsesBlockEndMarginWithLastChild` reads
it (the last child's bottom margin was then neither inside the container nor in the gap after it). Both are pinned
by tests now (`MulticolContainerAfterAFloat_StillWrapsItsFirstColumnBesideIt`,
`MulticolContainer_KeepsItsLastChildsBottomMarginInTheGapAfterIt`).

## Trap: the dispatch widening from #1038 dropped text

`CssBox.LayoutContents` sends a multi-column box to the columns engine when it holds any float, so #1038's
`ContainsInlinesOnly` widening cannot reroute an all-float container. But bare text beside a float has no
anonymous block in a multi-column parent, and the columns engine takes its children as block-level column
content: the float was laid out and the text vanished. The widening now applies only when nothing else is in
flow; bare text next to a float goes through the inline flow it always took. Bare text in a multi-column
container is not divided into columns at all, with or without a float
([gap](../accepted-gaps/bare-text-in-a-multicol-container-is-not-divided-into-columns.md)).

## Not done

A float that does not fit at the foot of its column stays there
([gap](../accepted-gaps/a-float-that-does-not-fit-in-its-column-overflows-it.md)); `float-reference: column`
for page floats was still page-scoped here; it is column-scoped since ([entry](2026-09-24-column-scoped-page-floats.md)).

Evidence: 10 geometry/paint tests in `MulticolFloatIntegrationTests` (the four defect shapes above, painted
position of a float in a column, page float as the only child, abspos child still against the container);
`FloatOnlyChildren_InMultiColumnContainer_AreLaidOutByTheColumnsEngine` replaces the test that pinned the
all-zero geometry; full net8.0 suite green.
