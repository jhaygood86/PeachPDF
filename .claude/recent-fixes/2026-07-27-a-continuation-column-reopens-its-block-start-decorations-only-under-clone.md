# A continuation column re-opens its block-start decorations only under `clone`

_Landed 2026-07-27._

[CSS Fragmentation 3 §6.2](https://www.w3.org/TR/css-break-3/#break-decoration). Follow-up to
[2026-07-27-a-column-break-can-fall-below-the-containers-own-child.md](2026-07-27-a-column-break-can-fall-below-the-containers-own-child.md),
found by that change's own post-change review. Supersedes
[PR #469](https://github.com/jhaygood86/PeachPDF/pull/469), which corrected one of the two sites and was
closed unmerged in favour of this.

**Two** sites placed content at the head of a column using
`max(ResumeContentTop, ContainingBlock.ClientTop)`, which folds in that block's top border and padding
**unconditionally**. The containing block there is continuing, so those are not real edges in this column
at all. Measured with `padding-top: 10pt; border-top: 6pt`: the first child of every continuation column
began **16pt** below the column top, **identically for `slice` and `clone`**, with no border drawn in the
gap — the painter was right the whole time (`FragmentEmitter.ResumesAnEarlierFragment` clears the top edge
of a fragment that resumes an earlier one), so the symptom was a strip of blank space with nothing in it.

**The load-bearing point is that this was a block/inline inconsistency inside one feature, not an
unimplemented area.** `CssLayoutEngine.CreateLineBoxes` already had the arithmetic written —
`context.ResumeContentTop + (HasCloneDecorations ? DomUtils.ClonedBlockStart(...) : 0)` — so the fix is
the block path saying the same thing, out of one helper (`CssBox.ContentTopOfTheContainingBlockIn`) that
both block sites call.

**Do not read that as "the inline path was already correct" — it is not, at a column boundary**, and the
post-change review is what forced the distinction. Measured on the fixed build with
`box-decoration-break: clone` and `padding-top: 9pt; border-top: 5pt` on a paragraph split between two
columns: the continuation begins flush at the column's content top, identically to `slice`, so the
paragraph's own decorations are never re-opened there even though `ClonedBlockStart` returns the right
14pt for it. The inline path supplied the *shape* of the answer, not a working precedent. That gap, and
the paint-side one beside it, are tracked — see the accepted-gap file
[clone-decorations-at-a-multicol-boundary-outside-the-block-path.md](../accepted-gaps/clone-decorations-at-a-multicol-boundary-outside-the-block-path.md).

**Both sites, because they answer the same question about the same containing block.**
`ColumnTopForTheChildThisFillBeginsAt` places the child laid out *afresh* at the head of the column;
`ResumeInTheNextFragmentainer` places the box that *continues* into it. A chain two or more levels deep
goes through both — the continuing block through the second, its children through the first. Fixing only
the first is not a smaller version of the fix but a **different and worse defect**: with the decorations on
`wrap` and the rows one level further down in `inner`, `inner`'s second-column fragment came out at
y=22.00 against a column content top of 6.00 while its rows sat at 6.00, so its background and border
painted 16pt *below* the content they contain. Before either fix both were 22.00 — wrong, but at least
self-consistent. That is why the test that matters here asserts on the emitted `BoxFragment` and not on
the box: **every box-level assertion about the rows passes on the half-fixed build.**

**What separates a re-opened box from one that is not is the fragmentation context, and it is stated
once — as the walk's bound.** `DomUtils.ClonedBlockStart` gained a `stopAt`, and the column sites pass
`column.ContextRoot`. That says exactly "sum the boxes this boundary falls inside", which is what §6.2
turns on, and it is what makes the multi-column container contribute nothing: a container is **not**
fragmented by its own columns, its border and padding wrap all of them at once, and `ResumeContentTop` is
already inside them. Left unbounded the walk runs past the container to the document root, so a container
that itself sets `clone` added its own block-start spacing to content inside it — **measured at 14pt of
spurious indent** with `padding-top: 9pt; border-top: 5pt` on the container, and on `slice` content too,
since `HasCloneDecorations` is a document-wide flag.

**The `Math.Max` is gone rather than narrowed, and that was measured rather than assumed.** Once the walk
is bounded, the maximum against `ContainingBlock.ClientTop` can never be the greater term: that coordinate
is the container's position on the page it *began*, so it is at or above the fragmentainer's content edge
in every case. Removing the branch entirely changes no result across the suite. Keeping it would have left
a branch no document can distinguish, and one the diff-coverage gate would have had to carry.

**The near-miss worth recording**, because it is the shape a future change reaches for first: asking
`ReferenceEquals(childBox.ContainingBlock, this)` instead — is the containing block the box whose child
loop is running? Because a child's `ParentBox` always *is* that box, that test is false only when the
loop's box is not a block container by `CssBox.ContainingBlock`'s walk (which skips `table-row`,
`table-row-group`, `grid`, `inline-block`) — and the containing block is then an ancestor sitting *higher
in the same continuing chain*, so "it did not resume here" does not follow from it. It names the caller,
not the fragmentation context.

**`CssLayoutEngineColumns`'s non-block arm is reachable, and must not throw.** The review that found this
defect also believed `RestatedInTheNextColumn`'s `token is not BlockBreakToken` arm was dead. It is not:
it is reached by the **recursion**, never by the outermost call, because a chain's innermost link is an
`InlineBreakToken` whenever the box that stopped is a block whose content is text — the ordinary "a
paragraph continues into the next column" case. Measured by throwing there: unconditionally, **29 tests
fail**; restricted to the outermost call, the whole suite passes. The arm now says so in its doc comment
rather than looking like coverage nobody explains.

**Also recorded here because it would otherwise be lost: the unguarded site fixes an
[#430](https://github.com/jhaygood86/PeachPDF/issues/430)-shaped nesting, and the obvious guard breaks
it.** The review measured whether `ColumnTopForTheChildThisFillBeginsAt` needs an `IsFragmenting`
check — the usual caution, given that several sites ask `HasOwnBand` without asking whether breaking
is live. It does not, and the evidence runs the other way. Same fixture, words authored vs claimed:

| | before the parent fix | with it | with it plus an `IsFragmenting` guard |
|---|---|---|---|
| top level | 2 pages, 51/49 (2 double-claimed) | 1 page, 49/49 | 1 page, 49/49 |
| inside `<td>` | 45/41, **8 missing**, y `[-375.2, 84.8]` | 49/49, **0 missing**, y `[7.5, 367.5]` | 45/41 again, negative y back |
| inside `display:flex` | 45/41, **8 missing** | 49/49, 0 missing | 41/41, **8 missing** |

So the guard would re-break a case the unguarded site fixes. (A separate 40-paragraph fixture — #430's
own — measures **identically** on both builds, so the two results are about different fixtures and
neither contradicts the other: #430 is not closed.)

Three nits from the same review: `BoxGeometrySnapshot.CaptureBox`'s `excluded` parameter is required
rather than defaulted (a private recursive helper that silently loses the exclusion if a call forgets it);
`Capture` states — and now `Debug.Assert`s — why a root is never itself tested against `excluded`, which
is a fact about the caller's two abutting ranges rather than about that method; and `BeyondThisColumn` no
longer allocates a set for the no-record case that ends every fill, along with `ContinuingPast` beside it,
which had the same shape.

**Load-bearing verified by neutralizing each part separately** (multicol suite, 89 tests): site 1 alone →
4 failures; site 2 alone → **1 failure, and only the fragment-level test catches it**; the whole helper →
4; the `clone` term → 3; the `stopAt` bound → 1. No part is redundant with another.

Tests: `MulticolLayoutIntegrationTests` (+4 — the `slice`/`clone` difference at the head of a continuation
column, the nested cloning sum, the container's own decorations not re-opening, and the fragment-level
`AContinuingBlocksFragment_BeginsWhereItsContentBegins` as a `[Theory]` over both values;
`ABreakTwoLevelsBelowTheContainer_…` **renamed and re-pointed**, since it pinned the deviation as
intended, with a comment saying so). Showcase section 12 is now two panels differing only in
`box-decoration-break`, with the entries nested one level inside the decorated wrapper so the two-site
disagreement is visible there and not only in the unit tests.
