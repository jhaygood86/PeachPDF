# An absolute box placed on an already-emitted page is drawn there (#1349)

Found while re-verifying #1334 (auto-height scroll containers fragment), by a content-preservation fuzz.
Fixed in the same branch because it was what turned that change into a net loss of content: documents
that used to draw this content on page 1 lost it once the wrapper before it stopped being monolithic.

## Symptom

After earlier content pushed layout past page 1, an absolutely positioned box with no positioned
ancestor was drawn on no page. When it was the first child of its block, the rest of that block was lost
too, and the next block moved up into its space. `position: relative` on the parent, or everything
fitting on one page, hid it. Reproduced on `main` with plain `<div>`s and no `overflow` anywhere.

## Two causes

- **`DomUtils.GetPreviousSibling` returned an absolutely positioned first child.** Its walk stepped over
  `position: absolute`, but the separate check applied when the walk ran out of siblings listed every
  other skipped kind and not `Absolute`. That omission is in the first commit of the file. So the paragraph
  after an absolute first child was placed below the absolute box, whose containing block (the initial
  one) put it on page 1: the paragraph went to page-1 coordinates too. With a monolithic box before it,
  page 1 was still open and the content drew there, over what was already on the page (visibly wrong but
  present). With ordinary flow before it, page 1 was already emitted, and the content was on no page.
  The walk and its end now share one predicate, `IsSteppedOverAsPreviousSibling`, so they cannot drift
  apart again.
- **Nothing re-opened an emitted fragmentainer for a box placed into it for the first time.**
  `InvalidateEmittedFragmentsFor` only fires for a box that already holds fragments and moves
  (`HoldsFragmentsFor`). An absolute box is reached in the tree on the pass for page 3 but placed by its
  offsets on page 1. `CssBox.PerformLayoutEpilogue` now calls
  `HtmlContainerInt.InvalidateEmittedFragmentainersReceiving` for an absolutely positioned box once its
  position and height are final, which calls `FragmentEmitter.InvalidateFrom` for the slots its border box
  reaches (`throughSlot`), not everything after them. That returns at once when the slot is not frozen yet, so forward layout
  (every ordinary placement) pays one comparison. When it does re-open a slot, the stale slot is re-emitted
  by `CatchUpStaleSlotsBehind` or `Finish`, the same path a §4.3 mover's re-opening takes.

`position: fixed` is not touched: the emitter places a fixed box on every page itself
(`ComputeFixedPageOffset`).

## Behaviour changes that look like regressions but are not

- A block whose first child is absolute used to start its in-flow content one line below that child. It
  now starts at the block's top, with the absolute box over it, as CSS 2.1 §9.3.1 requires (out-of-flow
  boxes do not affect the layout of their siblings). Under `position: relative` this moves the content up
  by the absolute box's height.
- Content that `main` drew at the top of page 1 through this bug is now placed where it belongs. Inside a
  multi-column container that also holds the absolutely positioned box, fragment pruning can then drop
  the content after a stretch of blank lines (#1358; fuzz 1_56, 1_64). `main` loses the same content
  whenever the absolutely positioned box is not the container's first child, so that is #1358, not this
  change: layout places the content identically with and without the box, and
  `VerifyFragmentPruningOverride` reports the pruned walk diverging from the full one.

## Evidence

`DomUtilsTests.GetPreviousSibling_OnlyAnOutOfFlowOrUndisplayedBoxBefore_ReturnsNull`,
`AbsolutePositioningIntegrationTests.AbsoluteFirstChild_LaidOutAfterPageOne_TakesNoPartInPlacingTheBoxAfterIt`
and `AbsoluteBoxPlacedOnAnAlreadyEmittedPage_IsDrawnThere` fail without the change. The full net8.0 suite
passed with it before any of the new tests were written, so no existing test depended on the old
placement.

## Not done

Static position (#1303) is still not used: an absolute box with `auto` offsets sits at its containing
block's corner, so one with no positioned ancestor goes to the top of page 1 rather than to where it
appears in the flow.
