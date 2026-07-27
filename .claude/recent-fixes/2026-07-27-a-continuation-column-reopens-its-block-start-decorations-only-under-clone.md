# A continuation column re-opens its block-start decorations only under `clone`

_Landed 2026-07-27._

[CSS Fragmentation 3 §6.2](https://www.w3.org/TR/css-break-3/#break-decoration). Follow-up to
[2026-07-27-a-column-break-can-fall-below-the-containers-own-child.md](2026-07-27-a-column-break-can-fall-below-the-containers-own-child.md),
found by that change's own post-change review.

`CssBox.ColumnTopForTheChildThisFillBeginsAt` placed the child a column's fill begins at using
`ContainingBlock.ClientTop`, which folds in that block's top border and padding **unconditionally**.
The containing block there is always *continuing* — a record naming it is what makes the loop a
resumed one — so those are not real edges in this column at all. Measured with
`padding-top: 10pt; border-top: 6pt`: the first child of every continuation column began **16pt**
below the column top, **identically for `slice` and `clone`**, with no border drawn in the gap. The
painter was right the whole time (`FragmentEmitter` clears the top edge of a fragment that resumes an
earlier one), so the symptom was a strip of blank space with nothing in it.

**The load-bearing point is that this was a block/inline inconsistency inside one feature, not an
unimplemented area.** `CssLayoutEngine.CreateLineBoxes` had the arithmetic right already —
`context.ResumeContentTop + (HasCloneDecorations ? DomUtils.ClonedBlockStart(...) : 0)` — so the fix
is the block path saying the same thing. Summed over the child's **ancestors** rather than including
the child (`ClonedBlockStart(childBox.ParentBox)`, the form `FragmentEmitter.BandCut` already uses):
the child begins here whole, so its own decorations are not being re-opened.

**The `Math.Max` was not deleted, it was narrowed.** It is still needed where `childBox.ContainingBlock`
is not the box whose loop this is — that block did not resume here, so it began in this column and its
content edge *is* a real edge. `ReferenceEquals(childBox.ContainingBlock, this)` is what tells the two
apart; a coordinate test cannot, because a continuation and a box that begins at the column top share
a coordinate.

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

Tests: `MulticolLayoutIntegrationTests` (+1, `slice` flush at the column top against `clone` at
+16pt; and `ABreakTwoLevelsBelowTheContainer_…` **renamed and re-pointed**, since it pinned the
deviation as intended). Full net8.0 suite green (6750 after merging `main`); **100% diff coverage**; zero warnings on
`dotnet build PeachPDF.slnx -t:Rebuild`. **68 of 69 showcases byte-identical**; `multicol` differs by
2 bytes, and pages 1–6 rasterize identically — the whole difference is section 12's `slice` panel now
starting its continuation column flush while the `clone` panel keeps its re-opened inset, which is the
two panels differing at all for the first time. Verified in both PDFium and MuPDF.
