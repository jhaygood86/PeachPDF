# An outside `::marker` is positioned by the pass that places its item, and positioned last within it

_CSS Fragmentation Level 3 / CSS 2.1 §12.5.1. Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320)._

An *outside* `::marker` (the CSS default) is reached by nothing generic: `CssLayoutEngine.FlowBox`
skips it, and the block-children loop never sees it because it is not a block child. Exactly one call
positions it — `CssBox.LayoutOutsideMarker` — and **where that call sits decides which fragmentainer
claims the marker**, because a fragment is claimed by the geometry a box carries at the moment the
slot is frozen.

**It must run on the pass that *places* the item, not the one that completes it.** Positioned from
`CssBox.PerformLayoutEpilogue`, an item straddling a page boundary got its marker's coordinates one
pass after the slot those coordinates fall in had already been frozen, and nothing re-opened that
slot: `HtmlContainerInt.InvalidateEmittedFragmentsFor` is a no-op for a box no frozen fragmentainer
holds, which a never-emitted marker is. Measured symptom: the bullet or number painted on **no page
at all** — 39 of 40 markers claimed in a 40-item list ([#444](https://github.com/jhaygood86/PeachPDF/issues/444)).
The epilogue's stated reason ("now that `Location` is final") was never the marker's requirement: it
is positioned against the item's own *border box*, so the item's height and content are not inputs,
and `PlaceBlockChild` has written the item's `Location` before its content is laid out at all.

**And it must run last within that pass.** A block opening its inline flow declares that this layout
has placed none of its subtree's words yet (`CssBox.AwaitPlacement`, from
`CssLayoutEngine.CreateLineBoxes`), and that walk reaches the marker even though the flow never visits
it. Positioning the marker before `LayoutContents` therefore has the declaration take it straight
back, and the marker is claimed by nothing again — being positioned is what clears the flag
(`CssRect.Top`'s setter). Anything that moves this call earlier "for clarity" reintroduces the whole
defect with no visible sign in the diff.

Everything that relocates the item afterwards is safe by construction: `CssBox.OffsetTop` recurses
through `Boxes`, so the marker travels with its item.
