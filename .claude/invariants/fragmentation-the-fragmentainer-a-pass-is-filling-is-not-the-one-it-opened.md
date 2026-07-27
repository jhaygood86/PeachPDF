# The fragmentainer a pass is filling is not the one it opened with

_CSS Fragmentation Level 3 §3.1. Tracker: [#400](https://github.com/jhaygood86/PeachPDF/issues/400)._

**A forced break is realized by *placement*** — the box is put at the content top of the slot the break names and the pass carries straight on from there, with no resumption record in between — so a pass that opened on slot `k` can be flowing content into `k + n`. A directional value makes `n` two, since [§3.1](https://www.w3.org/TR/css-break-3/#break-between) forces "one or two page breaks" and the stepped-over page stays deliberately blank.

`FragmentainerContext.SlotIndex` is therefore a **cursor** (`StepOverTo`), not the slot the driver handed the pass, and `FragmentEmitter.EmitPass`'s `throughSlot` exists for the same reason. Anything asking about "the fragmentainer being filled" — its band, whether anything precedes a box inside it — must read the cursor, never the slot the pass began with.

The measured symptom of reading the stale one: `CssBox.HasRoomAboveInThisFragmentainer` gates §5.4's orphans mover on "is there anything above this box in this fragmentainer?". A box placed *by* a forced break is at the very top of one, but against the pass's opening band it looked like a box with a whole page above it — so the mover fired and pushed it one page further, **leaving the page the forced break named blank** (`Location.Y` 340 instead of 180; emitted slots `[0, 2, 3, 4, 5]` instead of `[0, 1, 2, 3, 4]`).

The cursor moves **forward only** — a pass fills fragmentainers in document order, and a pass that has to reconsider an earlier one is re-entered by the driver with a context of its own — and is a **no-op for a nested fragmentainer**, because a column's band is not a slot of the page grid at all: `SlotIndex` there names only the page the column sits on, and a forced page break inside a column escapes to the page driver rather than re-banding the column.
