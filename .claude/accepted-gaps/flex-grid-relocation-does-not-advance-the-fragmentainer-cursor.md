# Relocating a flex line or grid row does not advance the fragmentainer cursor

`CssBox.PlaceBlockChild` steps `FragmentainerContext.SlotIndex` when a forced break places a block child on a
later slot, because a forced break is realized by placement and the pass carries straight on from there — see
[the invariant](../invariants/fragmentation-the-fragmentainer-a-pass-is-filling-is-not-the-one-it-opened.md).
`LineRelocation.DeltaFor` does the same *kind* of thing for a flex line or grid row — moves it to
`PageTopOf(target)`, one or two slots on — and does **not** step the cursor, so the pass lays the container's
following siblings out with the context naming a fragmentainer it has left.

Measured while landing issues #447/#450/#451: adding `CurrentFragmentainer?.StepOverTo(target)` there changes
nothing — the full net8.0 suite is identical with and without it (6683 either way), and so is the 69-showcase
corpus. The staleness appears unreachable rather than merely untested: the reader that made the block-flow
case a live defect (`CssBox.HasRoomAboveInThisFragmentainer`) asks about a box at the very *top* of the band
being filled, and after a line relocation nothing block flow places is ever there — the relocated line is
placed by the engine, and every following sibling is strictly below it. The emitter needs no help either; a
directional break's reserved blank slot materializes without the cursor.

Left out rather than shipped as a line no test can distinguish. Filed as
[issue #453](https://github.com/jhaygood86/PeachPDF/issues/453), which wants either a fixture that tells the
two apart or the written argument that none exists.
