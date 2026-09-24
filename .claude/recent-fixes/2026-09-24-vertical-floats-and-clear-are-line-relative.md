# `float` and `clear` in a vertical writing mode are line-relative, and now place and wrap (#796)

## The issue's premise was wrong, and so were three docs

#796 (and the #768 work before it) assumed `float: left`/`right` stay *physical* in `vertical-rl`/`vertical-lr`, so
that a float went to the physical left or right and stacked along the same axis the box's block flow runs on - a
"genuinely exotic structural collision" with no precedent. That came from MDN's logical-floating guide, whose
examples only show that `float: left` does not follow `direction`, which line-relative also means. The normative
text is [CSS Writing Modes 4 §7.5](https://www.w3.org/TR/css-writing-modes-4/#text-align): the line-right and
line-left directions, computed against the **containing block's** writing mode, are what `float` and `clear` `left`/
`right` mean; §6.4's table maps line-left to the physical **top** and line-right to the physical **bottom** for
`vertical-rl`, `vertical-lr` (both) and `sideways-rl`, and the reverse for `sideways-lr`, independent of
`direction`. So there is no collision: the float sits at the current block-axis position and slides along the
*inline* axis, and CSS 2.1 §9.5.1 applies with the axes swapped (WPT's `css-writing-modes` `float-vrl-*`/`vlr-*`
tests are the model). The old tests' wording ("physical footprint") and three notes claimed the opposite and are
corrected.

## What was built

- **`CssBox.PlaceVerticalFloat`**, used by both entry points (see the invariant below): lays the float out at a
  provisional block position, then translates it (`OffsetLeft`/`OffsetTop`) to where the collision loop puts it -
  laid out first because an auto-width float shrinks to its content only inside its content layout, and where it
  lands depends on that width. Same-side floats sharing a block-axis range stack along the inline axis while it has
  room (a definite height; an auto-height container never drops) and drop to the block-end edge of the first of
  them to end otherwise.
- **`CssBox.VerticalFloatOccupancy`** (a `VerticalFloatReach`) records, per float, which physical edge it is pinned
  to, the inline range from that edge it covers (floats ahead of it on the side included) **and the placing
  container's own top and bottom edges**. A bottom float cannot know its Y until the container's height is final, so
  it is left at the top and moved in `PerformLayoutEpilogue` (`_pendingBottomFloats`, clamped so a float taller than
  its box overflows the end rather than hanging above it); later text only needs the reach, not the position. See
  the invariant [vertical-a-floats-reach-is-relative-to-the-box-that-placed-it](../invariants/vertical-a-floats-reach-is-relative-to-the-box-that-placed-it.md).
- **CSS 2.1 §9.5.1 with the axes swapped, all of it.** Rule 2 (stack beside the floats on the same side), rule 3
  (a left and a right float may not overlap: the opposite side's reach counts against the fit, so a 70pt and a 70pt
  float do not share a 100pt container), rule 5 (a float's block-start is not before an earlier float's) and rule 6
  (a float reached mid-text is no higher than the column being built). The first version enforced only rule 2.
- **A float reached mid-column waits for the column to close** (`StartNewLine` places it), because a taller word can
  still arrive and make the column thicker; placed against the thickness so far, it would be reached into. A float
  reached while the column is empty is placed at once, since the next word's span must see it. A word split in two
  (hyphenation, overflow-wrap) shifts the index of every float after it (`ShiftFloatsAfter`), or a float following a
  split word is placed beside its last piece.
- **`clear`** on a stacked child: `ClearanceEdge` takes the block-end outer edge of the floats it names (`left` = the
  top ones, `right` = the bottom ones, resolved through `EffectiveClear` so `inline-start`/`inline-end` work), moves
  the child's block-start margin edge there, and spends the open margin group (clearance inhibits collapsing,
  CSS 2.1 §9.5.2).
- **Wrap.** `DomUtils.GetVerticalFloatInsets` replaced `GetVerticalFloatConstraint`, which returned only an extent
  cap and so could only express a float at the far end. It returns how far top and bottom floats reach into the
  column, **translated into the reading box's own edges** (a paragraph beside a float is not the float's container),
  and `ComputeColumnInlineSpan` maps them to a start inset (words begin after it) and an end inset (the wrap
  limit), swapped for `direction: rtl`. A column is beside a float when its leading edge is in the float's block
  range, half-open at the block-end edge (`VerticalFloatCoversBlockPoint`): a column starting exactly where the
  float ends is beyond it. The floats the box's own inline flow placed are its children and so are not
  reached by the preceding-sibling scan; they are passed in.
- **Alignment.** `CssLineBox.VerticalTopInset`/`VerticalBottomInset` carry each column's insets, and
  `FinalizeVerticalLineBoxes` aligns and bidi-reorders within that span. Without this, `text-align: start` flushed
  every column back to the box's top edge, under the float - the words were placed after the float and then moved
  back.

## The trap that cost the most time: a floats-only container is not on the block path

The first version passed every block-path test and failed the simplest one, `<div vertical-rl><div float></div></div>`:
the float sat at the physical **left**. #1038 made `ContainsInlinesOnly` true for a box holding only floats, so such a
container goes to `CreateVerticalLineBoxes` and its float was laid out by `LayoutOutOfFlowDescendants` through the old
horizontal `LayoutBlockChild` path. Floats among inline text are placed there too, at the block-axis position of the
column the word stream has reached (`PlaceFloatsUpTo`), and a float placed at word 0 must be placed *before* the first
column's span is computed or that column ignores it. See the invariant
[vertical-a-float-has-two-entry-points](../invariants/vertical-a-float-has-two-entry-points.md).

## The old tests were vacuous

The #768 tests passed for the wrong reason: `AssertFloatAvoidance` skipped each column's first word, which is exactly
where a column that leaves no room puts its one forced word, and none asserted where the float itself was. They are
replaced by `VerticalFloatIntegrationTests` (placement for every mode/direction/side, block-axis
position, opposite sides, stacking and dropping, `clear` left/right/both, `inline-start`/`inline-end`, auto height,
text starting below a top float and stopping above a bottom one).

## A definite height is not `ClientBottom` while the box is still laying out

The first version took a container's inline extent from `WritingModeFrame.LogicalContentWidth`, which reads
`ClientBottom`. For a box with a definite `height` that is not settled until `ApplyHeight` runs in the epilogue, and
read as 0 mid-layout - so every float's container looked 0pt tall and a paragraph beside a bottom float was narrowed by
the float's whole length. It passed every test where the paragraph and the container shared their bottom edge.
`CssLayoutEngine.DefiniteContentHeight` is the number to use, as the inline path always did.

## Not done (recorded in the accepted-gap file)

A float in a nested block does not take part in `clear` of a later sibling of its parent; orthogonal floats are not
sized by the two-phase rule (#1354); a bottom float in an auto-height box wraps text against the provisional edge;
and an auto-height *nested* vertical block wraps against the page, so a bottom float only cuts its columns when that
block has an explicit height (a pre-existing property of the engine, not of this change).

Evidence: 34 new test methods (40 cases); full net8.0 suite green; the `vertical_floats` showcase rasterized through PDFium and MuPDF
(identical: top float with text starting below it, bottom float with text stopping above it, both sides at once in
`vertical-lr`/`rtl`, and `clear` leaving a visible gap).
