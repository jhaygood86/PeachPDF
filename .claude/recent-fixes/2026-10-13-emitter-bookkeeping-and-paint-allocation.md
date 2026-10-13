# What the emitter keeps per box, and two paint-time allocations

`invoices-l` (1,000 pages of striped, bordered tables), warm, one render, in process: allocation 1,525 -> 1,331 MB, live heap at the end
~765 -> ~575 MB. Wall time moved within the noise of the machine, which is the honest summary: the GC pause is ~1 s of ~3.5 s and the
rest is spread over parse, cascade, layout and paint with no frame above ~15%. Output is unchanged (benchmark documents, fuzz corpus and all 220
showcases identical to main under PyMuPDF words and drawings).

- **`Draft` holds arrays, built from scratch lists.** `BuildDraftCore` gathered each box's line rectangles, words and child drafts in three
  `List`s that were then kept (after the first change in this area, adopted rather than copied), so every draft carried three lists grown by
  doubling and half empty. The lists are now rented from per-emitter stacks (the method is recursive: a box's children are built while its own
  lists are still being filled), and `Draft.Adopt` copies them into arrays of exactly their length. A path that does not give a list back loses only
  the saving.
- **One dictionary per box instead of two.** `_fragmentRange` and `_fragmentsOf` had the same key; the second was read only when a box had two or
  more fragments, and held a one-element list for every other box. `_boxFragments` holds the range and the single fragment in its entry and creates the
  list when a second arrives (`CollectionsMarshal.GetValueRefOrAddDefault`, so the entry is updated in place).
- **The memoized extent and local rectangle live on the draft**, stamped with an epoch (`_cacheEpoch`, bumped where the dictionaries were
  cleared), instead of in two `Dictionary<Draft, Rect>` with reference-equality comparers.
- **`DerivedStyle.BorderColors`.** A table cell reads its four border colours and nothing else of `PaintExtras` (outline, radii, transform,
  column rule: ~500 bytes). They have their own cache now.
- **Outline rectangles are collected only for a box whose outline can draw** (`outline-style` neither `none` nor `hidden`). Every rectangle of every box
  was appended to a list with an array behind it, and thrown away unless `OutlineDrawHandler.Paints` said yes.
- **A plain background colour is one fill.** `PaintBackground` takes the box straight to `PaintClippedBrush` when it has no image layers, is not
  rounded and clips to the border box. `PaintBackgroundCore` has a lambda capturing its parameters, so it allocated a closure object on every
  call, which every striped row paid.

## Not done

The per-line structures (`CssLineBox.Rectangles`, the `SliceGeometry` dictionary built per draft with lines, `_rectangles` per box) are the next
allocation, ~4%: for a box with a single draft, which is nearly every box, the rectangles dictionary is the draft's own lines and the slices could be
an array aligned with them. The sort in `SliceGeometriesOf` is unstable, so an array version has to sort the same sequence with the same comparison
to give the same result for lines that tie.
