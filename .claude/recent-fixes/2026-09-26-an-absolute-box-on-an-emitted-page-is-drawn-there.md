# An absolute box placed on an already-emitted page is drawn there (#1349)

Found while re-verifying #1334 (auto-height scroll containers fragment), by a content-preservation fuzz,
and split out of it. It was what turned that change into a net loss of content: documents that used to
draw this content on page 1 lost it once the wrapper before it stopped being monolithic.

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

## A tall absolutely positioned box is laid out in one piece

A later #1334 review round found the in-flow content after a tall absolutely positioned box lost. A
`position: absolute` block child is now laid out unbroken, through `CssBox.LayoutBlockChildUnbroken`, which
is shared with the page float in a column (formerly `LayoutPageFloatInColumn`).
- **Why it was lost.** The box's break ended the pass, and the in-flow content after it, which it does not
  displace (§9.3.1), was placed back on the page the break left.
- **On `main` too.** `main` lost that content whenever the box was not its parent's first child.
- **After the `GetPreviousSibling` fix.** Once that fix stopped placing the content *below* a first-child
  box, the same loss appeared in that case as well.

Now each page shows the slice of the box that falls in it. The cost is a line cut at each page boundary
inside the box and no §4.3 relocation of its contents, recorded in
[its gap](../accepted-gaps/a-tall-absolutely-positioned-box-is-sliced-not-fragmented.md) (#1372).

Re-opening covers the content as well as the border box. `InvalidateEmittedFragmentainersReceiving` first
re-opened only the pages the border box covers. On a page already emitted, `overflow: visible` text past a
short absolute box's height was then lost (X15–X25 of 25). It now takes the content's extent
(`GetMaximumBottom`).

## Content after an absolute multi-column box keeps main's placement

An absolutely positioned box that is or holds a multi-column container keeps the breaking path
(`CssBox.IsOrHoldsAMultiColumnContainer`, cached per layout generation). The columns engine records each
column for one page slot and needs the attached fragmentainer: laid out unbroken, it lost W18–W20 (#1376 is
the same cause). Its break ends the pass, which three review rounds turned into content loss for what
follows it:

1. With the `GetPreviousSibling` fix, the content after it went to its parent's top on page 1, which the
   pass resuming inside the box on page 2 had already emitted: lost.
2. Placing it below every such box moved it backwards when the box sat on an earlier page (`top: 0` inside an
   `overflow: hidden` wrapper): the wrapper's content vanished with it.
3. Re-opening the emitted page from `PerformLayoutEpilogue` drew a short block, but no pass paginates content
   laid out behind it:
   - a following `columns: 2` block lost its whole first page;
   - a long paragraph run was sliced across the margin;
   - ungated, that check also made 5,000 wrappers take 24.4s instead of 4.4s.

What stands is `main`'s own placement for exactly that case (`DomUtils.IsAPrecedingBreakingAbsoluteBox`).
When such a box is among the stepped-over siblings, the absolutely positioned first child is returned as the
previous sibling. A fourth review found that checking only the first child missed a plain absolute box
followed by the multi-column one. Every shape from the three rounds, eight probes, now draws the same words
at the same positions as `main`. The §9.3.1 position is an
[accepted gap](../accepted-gaps/content-after-an-absolute-multi-column-box-is-placed-below-it.md) (#1377).

## Not done

Static position (#1303) is still not used: an absolute box with `auto` offsets sits at its containing
block's corner, so one with no positioned ancestor goes to the top of page 1 rather than to where it
appears in the flow.
