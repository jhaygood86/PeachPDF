# An absolute box breaks in passes of its own and is drawn where it is placed (#1349)

Found while re-verifying #1334 (auto-height scroll containers fragment), by a content-preservation fuzz,
and split out of it. It was what turned that change into a net loss of content: documents that used to
draw this content on page 1 lost it once the wrapper before it stopped being monolithic.

## Symptom

After earlier content pushed layout past page 1, an absolutely positioned box with no positioned
ancestor was drawn on no page. When it was the first child of its block, the rest of that block was lost
too, and the next block moved up into its space. A box taller than a page broke between its lines, but
every in-flow box after it in the same block was lost when it was not the block's first child.
`position: relative` on the parent, or everything fitting on one page, hid it. Reproduced on `main` with
plain `<div>`s and no `overflow` anywhere.

## Three causes

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
  and its overflowing content reach (`throughSlot`), not everything after them. Forward layout (every
  ordinary placement) returns after a fragment lookup and one slot lookup.
- **A break inside the box ended its parent's pass.** The next pass resumed inside the box on the following
  page while the in-flow boxes after it, which it does not displace (CSS 2.1 §9.3.1), were placed back on the
  page the break left, already emitted. The box now runs as its own fragmentainer passes, resumed page by
  page by its own break token, the loop `CssBox.LayoutBlockChild` already ran for a block float
  ([the invariant](../invariants/fragmentation-a-box-laid-out-apart-from-the-in-flow-chain-must-not-end-the-pass.md)).
  It starts in the slot its offsets place its top in (`AbsoluteTopBeforePlacement`).

`position: fixed` is not touched: the emitter places a fixed box on every page itself
(`ComputeFixedPageOffset`).

## What a browser does, and the design that was tried first

A 40-line absolute box on a document with two short paragraphs, on a 300×200pt page with 20pt margins,
printed through Chrome: 4 pages, every line drawn once, none cut, 12, 13, 13 and 2 lines to a page.
`main` draws the same, and so does this change (`TallAbsoluteBox_AddsPagesAndBreaksBetweenItsLines`).

The first version of this change laid the box out in one piece instead (`LayoutBlockChildUnbroken`, the
fragmentainer detached and word breaks suppressed), and let each page show the slice of it that fell there.
That kept the in-flow content, but a line at a page edge was cut, nothing inside the box was relocated, and it
drew 13, 14 and 13 lines to a page. It also kept a box that is or holds a multi-column container on the
breaking path, because the columns engine needs the attached fragmentainer, and then had to keep `main`'s
placement of the content after such a box (three attempts to put it at its §9.3.1 position failed review). Both
exceptions, and the scroll-container one below, are gone with the one-piece layout.

## Measured

592 generated documents of plain text, floats, columns, flex, tables and scroll containers, each with
absolutely positioned boxes (tall ones, first children, inside other wrappers) and no positioned
ancestor, unique words, extracted per page and compared with `main`. The Chrome columns use the first 60 of
them, 28,952 words, printed through Chrome.

| Build | Words lost | Duplicated | Documents `main` draws completely that lose words | Words at Chrome's page and position | Words Chrome draws that are missing |
|---|---|---|---|---|---|
| `main` | 4,737 | 85 | | 7,853 | 444 |
| first version (one piece) | 1,542 | 180 | 57 of 313 | 13,550 | 130 |
| this change | 702 | 181 | 7 of 312 | 17,375 | 61 |

- 225 documents lose fewer words and 10 lose more.
- The same sweep with one part removed from the one-piece version: without the re-opening, 30,850 words lost;
  without the `GetPreviousSibling` change, 4,024. The re-opening is essential and the predicate is where most of
  the gain comes from.
- On 250 generated mixed-feature documents (floats, columns, flex, grid, scroll containers) words lost go from
  34,514 to 34,067 and duplicated from 7,920 to 8,217; the 14 that `main` draws completely are still complete.
  The 250 ordinary documents are unchanged.
- Six of the 7 documents that lose words have floats, columns or flex in them, the engines with gaps of their
  own; the seventh loses one word inside an absolute box. In the one reduced (a float beside text after a
  flex container), replacing the absolute box with a plain 6pt spacer makes `main` lose the same words: the
  content after the box now starts where it should, which exposes the float's loss.
- Timings, fastest of three, on a 217-page ordinary document and a 237-page one with cards, floats and
  `flow-root` boxes: 83.3s to 86.8s and 141.1s to 140.0s. A document with one absolute badge in a
  `position: relative` parent per paragraph, 2,600 paragraphs, takes 129s on `main` (273 pages, 56 too many) and
  189s here (217 pages). Absolute boxes already scale worse than linearly on `main`: 1,200 such paragraphs take
  18 to 20s against 5s without the badges.

## A scroll container that holds an absolute box breaks

[#1521](2026-09-29-auto-height-overflow-containers-paginate.md) kept an auto-height scroll container whole when
it held an absolute box, because the box's break ended the pass and the box was drawn above the page area. That
exception now costs words: kept whole, the container is sliced, and once the paragraphs after the box start at
the container's top (the change above) the slice loses lines at the page edges. A plain `overflow: auto`
wrapper with an absolute first child lost 6 words that `main` draws. Dropping the exception fixed that and
took the absolute-box documents from 21 documents that lose words to 7 (1,037 words lost to 702). The new
`ScrollContainerWithAbsoluteFirstChildIntegrationTests` fails with the exception back.

## Behaviour changes that look like regressions but are not

- A block whose first child is absolute used to start its in-flow content one line below that child. It
  now starts at the block's top, with the absolute box over it, as CSS 2.1 §9.3.1 requires (out-of-flow
  boxes do not affect the layout of their siblings). Under `position: relative` this moves the content up
  by the absolute box's height.
- Content that `main` drew at the top of page 1 through this bug is now placed where it belongs.

## Review of the split PR

- **Placed twice.** A box whose block position depends on its containing block's height (auto margins
  between `top` and `bottom`, CSS 2.1 §10.6.4) is placed provisionally in its own epilogue, and finally by its
  containing block's (`ResolveAbsolutelyPositionedDescendantAutoBlockMargins`). The re-opening ran only for the
  first, and `OffsetTop`'s notification covers only a box already frozen, so a vertically centred stamp at the
  end of a long document was drawn on no page. It now re-opens the pages the final position reaches too
  (`AnAbsoluteBoxCentredByItsContainingBlock_IsDrawnWhereItIsFinallyPlaced`, which also checks it is drawn
  once). `main` lost it as well.
- **Counters.** `DomUtils.ResolveCounterAnchor` used `GetPreviousSibling`, a layout question that steps over
  out-of-flow boxes. With the absolute first child now stepped over too, a `display: contents` element's
  `target-counter()` missed that child's `counter-increment`. Counters follow the document tree whatever the
  positioning, so the anchor now comes from `PreviousSiblingInDocumentOrder`. This also corrects `main`, which
  already missed a non-first absolute or fixed sibling.

## Not done

Static position ([#1303](https://github.com/jhaygood86/PeachPDF/issues/1303)) is still not used: an absolute box
with `auto` offsets sits at its containing block's corner, so one with no positioned ancestor goes to the top of
page 1 rather than to where it appears in the flow. An absolutely positioned multi-column box that is the first
thing in its block still draws W4 and W17 below the page band on the #1376 document, and uses 3 pages where
Chrome uses 2.
