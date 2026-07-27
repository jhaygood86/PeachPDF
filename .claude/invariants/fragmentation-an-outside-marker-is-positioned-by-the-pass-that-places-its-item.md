# An outside `::marker` is positioned by the pass whose fragmentainer its item's geometry belongs to

_CSS Fragmentation Level 3 / CSS 2.1 §12.5.1. Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320)._

An *outside* `::marker` (the CSS default) is reached by nothing generic: `CssLayoutEngine.FlowBox`
skips it, and the block-children loop never sees it because it is not a block child. Exactly one call
positions it — `CssBox.LayoutOutsideMarker` — and **which pass makes that call decides which
fragmentainer claims the marker**, because a fragment is claimed by the geometry a box carries at the
moment the slot is frozen.

**The page grid and a column answer "which pass" differently, and one rule cannot serve both.** The
predicate is `CssBox.MarkerBelongsToTheFragmentainerBeingFilled`, and both of its halves cost a
measured defect:

- **On the page grid, the marker belongs to the pass that *places* the item**, because a box that
  does not finish keeps the position that pass gave it. Positioned from `CssBox.PerformLayoutEpilogue`
  — which runs only on the pass that *completes* the item — a straddling item's marker got its
  coordinates after the slot those coordinates fall in had been frozen, and nothing re-opened that
  slot: `HtmlContainerInt.InvalidateEmittedFragmentsFor` is a no-op for a box no frozen fragmentainer
  holds, which a never-emitted marker is. Measured symptom: the bullet or number painted on **no page
  at all** ([#444](https://github.com/jhaygood86/PeachPDF/issues/444)).
- **Inside a column, it belongs to the pass that *completes* the item**, because a box that does not
  finish there is laid out again at the next column's inline position
  (`CssBox.ResumeInTheNextFragmentainer`, gated on the very same
  `FragmentainerContext.HasOwnBand` the predicate asks), so only its last fragment's geometry
  survives. Applying the page-grid answer here left the marker in the column its item had gone from —
  a bullet beside nothing in column 1, none beside the item's own text in column 2, **on a single
  page with no page break in it** — and left that column's `BoxGeometrySnapshot` holding a second
  origin for the same word, so it was claimed twice.

**And it must run last within its pass.** A block opening its inline flow declares that this layout
has placed none of its subtree's words yet (`CssBox.AwaitPlacement`, from
`CssLayoutEngine.CreateLineBoxes`), and that walk reaches the marker even though the flow never visits
it. Positioning the marker before `LayoutContents` therefore has the declaration take it straight
back, and the marker is claimed by nothing again — being positioned is what clears the flag
(`CssRect.Top`'s setter). Anything that moves this call earlier "for clarity" reintroduces the whole
defect with no visible sign in the diff.

**What is safe by construction and what is not.** A mover that relocates the item through
`CssBox.OffsetTop`/`OffsetLeft` takes the marker with it, because both recurse through `Boxes` — that
covers the §4.3 movers, the keep-with-next pulls, and the flex/grid engines translating a placed item.
A *column* does not relocate that way: `CssLayoutEngineColumns` lays the child out again at a new
inline position rather than offsetting it, which is exactly why the second half of the predicate
exists.
