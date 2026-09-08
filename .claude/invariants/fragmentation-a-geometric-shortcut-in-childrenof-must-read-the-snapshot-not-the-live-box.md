# A geometric shortcut in `ChildrenOf`/`BuildDraft` must read the snapshot, not the live box

Any check that reasons about *where* a child sits — to decide whether to skip visiting it, commit an
observation about it, or anything else geometry-shaped — must consult the `BoxGeometrySnapshot` parameter
threaded through `ChildrenOf`/`BuildDraft` whenever one is non-null, never the box's own live
`Location`/`Rectangles`/`Words` directly (`CssBox.OwnGeometryTop()` and friends). A non-null `snapshot`
means the child is being read through a `CapturedInstance` — a repeating header's source subtree,
re-snapshotted and repositioned once per page it repeats on, or a multi-column column's own fill — and the
box's *live* position is simply a different page's answer, not this slot's. `RectanglesOf`/`BoundsOf`
already know this (they consult `snapshot` when present); a new geometric check that reads the box directly
instead reintroduces the bug this file exists to name, found via the pruning-parity oracle: a repeating
header's own `<tr>`, reached through the header's own captured snapshot, read its *live* `Location` —
wherever the box last happened to sit — instead of this page's own snapshotted position, corrupting the
tree at every slot but the one where the two happened to coincide.

A box nested inside a captured-instance owner is a related but distinct trap: skipping a normal-flow
ancestor because *its own* settled geometry looks safe to skip can still hide a captured instance recorded
for something nested arbitrarily deep inside it (a relocated `break-inside:avoid` card wrapping a
repeating-header table). Checking only the immediate child against `_capturedInstanceOwners` misses this —
the owner can be several levels further down. Any new mechanism with this shape needs the ancestor-inclusive
set (walk every ancestor of a box the moment it becomes a captured-instance owner), not just the owner set
itself.
