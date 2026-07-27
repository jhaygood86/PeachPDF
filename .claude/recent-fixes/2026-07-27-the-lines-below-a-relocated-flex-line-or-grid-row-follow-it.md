# The lines below a relocated flex line or grid row follow it

_Landed 2026-07-27._

[CSS Fragmentation Level 3 §3.1](https://www.w3.org/TR/css-break-3/#break-between).

A flex container's break points are between its **lines** and a grid container's between its **rows**,
and #315 made a line that may not be cut move to the next fragmentainer. It moved *only that line*.
Everything below it stayed exactly where it was, so the relocated line was drawn **on top of** the one
after it — measured on a three-line wrapping flex container as line 2 landing at 380–440 while line 3
stayed at 400–460, a 40pt overlap. Both engines had the defect, in the same shape.

**The load-bearing idea is that the displacement accumulates and the geometry has to accumulate with
it.** Both engines already tracked a running `shift` and already used it to *ask* the question — a
later line's straddle test reads `bounds.Top + shift`, i.e. where the line will be once everything
above it has moved. What neither did is *apply* it: each relocated line was offset by its own `delta`
rather than by the running total, and a line that needed no relocation of its own was never offset at
all. So the model was right and only the write-back was wrong, which is why no test caught it: every
existing fixture relocates exactly one line, where `delta == shift` and there is nothing below it.

`LineRelocation.DeltaFor` now states the decision once for both engines — they were asking the same
question of the same inputs in two copies, differing only in local variable names — and it deliberately
*returns* the displacement rather than applying it, because applying it is precisely the part that
cannot be done per line.

**The second half was found by reading the container's own height after fixing the first.** With the
lines right, an auto-height container still reported a bottom a whole displacement short of the content
it held (460 against content reaching 500), because both engines sized the container from their
**lines/tracks** — `lines.Max(l => l.CrossOffset + l.CrossSize)`, `rows.Sum(t => t.Size)` — in a phase
that runs *after* the relocation and overwrites the `+= shift` it had just applied. The relocation pass
now runs after that sizing in both engines. Getting this order wrong is silent: the boxes are in the
right places and only the container's reported box is short, so nothing overlaps and nothing looks
wrong until something measures against the container.

**All 67 showcases were byte-identical before a fixture was added for it**, which is the whole reason
this survived: no showcase had two lines relocating. `paged_media_monolithic_content` gained a
wrapping flex container whose *first* line straddles the boundary, so all three travel — and **on the
unfixed build lines 2 and 3 stay put and line 1 is drawn underneath them**, with only the top edge of
its cards visible above the line that overdrew it.

Tests: `FlexGridFragmentationIntegrationTests` (+2 theories × 2 engines — the line after a relocated
one not overlapping it, and the container reporting a height that holds its content). Verified
load-bearing by restoring the per-line `delta`: the overlap test fails. Full net8.0 suite green (6568),
0 warnings.
