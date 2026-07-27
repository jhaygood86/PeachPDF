# `wrap-reverse` stacks its lines in the other direction

_Landed 2026-07-27. [Issue #458](https://github.com/jhaygood86/PeachPDF/issues/458)._

`flex-wrap: wrap-reverse` was implemented by reversing the *list of cross offsets* after
`align-content` had assigned them. That is the same thing as reversing the stack **only when every
line has the same cross size**, which every fixture in the repo happened to have. Where the sizes
differ, a line lands at an offset that was computed for a line of another size, so the lines overlap
and the container is sized from the wrong end of the stack.

**The load-bearing idea is that the reversal is about the *direction the lines are stacked in*, not
about the numbers that came out of it.** §5.3 swaps the cross-start and cross-end directions, so each
line still occupies its own cross size, in sequence, with whatever `align-content` put between them —
read the other way. Reflecting each line's placed strip about the container's cross axis
(`CrossOffset = crossExtent - (CrossOffset + CrossSize)`) *is* that stack: order reversed, every size
and every gap preserved, spacing correct in the new direction for `space-between`/`space-around`/
`space-evenly` without recomputing any of it. The permutation is what needed the special cases.

`crossExtent` is the container's own cross size where it has a definite one, and the content extent
where it is indefinite and the lines are what decide it. Both halves are load-bearing, and the
definite half is what makes an overflowing container spill off the **cross-end** edge — the top of a
row container, since `wrap-reverse` put cross-start at the bottom. Definiteness is passed in as its
own flag rather than inferred from `containerCrossSize > 0`: a definite cross size of *zero* is a real
size, and the two readings overflow in opposite directions.

## What was found by running it rather than by reading it

**The defect reached a second showcase that nobody had connected to it.** `paged_media_monolithic_content`'s
`wrap-reverse` section — added by
[the #448 fix](2026-07-27-a-flex-container-is-read-down-the-page-not-down-the-source.md) — has cards
of unequal height, and its two lines sat 21pt further apart than the `gap` asks for, with the
container's dashed border ending short of them. That is the same permutation defect wearing different
clothes: the extra space is exactly the difference between the two lines' cross sizes. It was not
visible as an *overlap* there only because the taller line happened to be the lower one.

**§3.1 avoidance between two such lines had nothing to answer, rather than answering wrongly.** The
issue predicted `LineRelocation.Walk.AvoidanceRun` could answer the "does a boundary fall here?"
question wrongly. Measured, the failure is quieter: with the lines *nested* (B at 120–180 holding A at
140–160), no fragmentainer boundary falls between them at all, so the avoid is never consulted and
nothing moves. Stacked properly (B 120–180, A 180–200) the boundary is exactly the break point above
A, and A's `break-after: avoid` brings B onto the next page with it. The regression test is written at
those coordinates.

**A run of the harness in a worktree another agent is also building in will hand you the other
agent's `PeachPDF.dll`.** A fragmentation fixture that had just passed its geometry guard failed it a
minute later with the *pre-fix* numbers, from a concurrently rebuilt binary. Re-run before believing a
result that contradicts one you already have.

## What the post-change review found

**Reflecting about a *larger* extent than before exposed a column container sizing itself from its
lines.** Phase 10 gates its cross-axis write on `!hasDefiniteHeight`, which in a column direction is a
question about the **main** axis and says nothing about the width — so a column container with an
explicit `width` re-derived that width from wherever its lines happened to end. On `main` this
discarded the width whenever the lines were packed at cross-start (`width: 200pt` measured as 50pt);
the reflection moved the damage to `align-content: flex-end` instead. Neither is right: the width is
not the lines' to decide, and that branch now runs only where the width is auto. Found by the review
rather than by this change's own tests — the fourth consecutive review of this code to find something
the author's tests missed.

**A definite cross size of zero took the indefinite path.** `containerCrossSize > 0` conflates "the
container has no definite cross size" with "it has one, and it is zero" — and they overflow in
opposite directions (`height: 1pt` overflowed the top correctly, `height: 0pt` overflowed the bottom).
A `box-sizing: border-box` box whose padding exceeds its height reaches the same discontinuity.
Definiteness is now its own parameter.

## What was deliberately not done

`ComputeCrossOffsets` still aligns each item *within* its line against the unswapped edges, so
`align-items`/`align-self: flex-start` puts an item at the top of its line where `wrap-reverse` has
made cross-start the bottom — including through the `stretch`-that-cannot-stretch fallback. That is a
different method, a different unit of work, and it changes the rendering of far more documents than
this fix does. Filed as #459 and
[recorded as an accepted gap](../accepted-gaps/flex-wrap-reverse-does-not-swap-align-items-within-a-line.md).

Two more the review measured and this change leaves alone: `align-content: normal` packs where it
should stretch (#461,
[gap note](../accepted-gaps/align-content-normal-packs-instead-of-stretching.md) — the reason the new
column fixture states `align-content: flex-start` rather than leaving it unset), and a wrapping
`inline-flex` container places its lines against a cross origin it no longer has and reports the
height of its tallest line (#462 — measured identical before and after this change, and identical for
`wrap` and `wrap-reverse`, so this fix never reaches it).

## Evidence

Tests: `FlexboxIntegrationTests` +10 cases counting theory rows, `FlexGridFragmentationIntegrationTests`
+1. The five that state the stacking (unequal sizes with an explicit no-overlap assertion,
`align-content: flex-end` and `space-between` against the reversed edges, an overflowing container, and
a `flex-direction: column` container whose lines pack against the right edge) all fail on the unfixed
build. Verified load-bearing by neutralizing each half in turn: restoring the permutation → **5 fail**;
keeping the reflection but dropping the container's definite cross size for the content extent → **2**
(the overflow case and the column one, where the free space has to be there for the lines to be packed
against the far edge). The two review fixes are pinned by one case each, each failing alone when its own
fix is neutralized.

Full net8.0 suite green (**6749** passed), CLI green (**96**), **100% diff coverage**, zero-warning
`dotnet build PeachPDF.slnx -t:Rebuild`.

**67 of 69 showcases identical.** `flexbox` gains a `wrap-reverse, unequal line heights` row in
section 6 — on the unfixed build its 24pt green card is drawn over the top of the 40pt blue one below
it, obscuring the label; verified in PDFium and MuPDF. `paged_media_monolithic_content` changes by the
21pt described above.

The comparison rasterized every page of all 69 at 72 dpi and diffed the pixels, rather than hashing
normalized bytes. Two reasons, both met here: it needs no list of volatile fields to keep up to date
(see [the two-timestamps invariant](../invariants/testing-a-pdf-carries-two-timestamps-not-one-when-showcases-are-compared.md)),
and it says *which page* differs, which is what sends you to the right crop. Regenerate the baseline
from the same `main` the branch has been merged with: an earlier run against a stale baseline reported
`transform` and `acid2` as differing, which was the baseline's age and not the change.
