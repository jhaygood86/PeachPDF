# A footnote's resolved page can go stale if `target-counter(_, page)` reflows afterward

`HtmlContainerInt.PerformLayout` runs the footnote convergence loop (`ResolveFootnotesForThisAttempt`)
before `ReapplyPseudoElementContent`/the `target-counter(_, page)` reflow loop, on the reasoning that
footnotes should settle their own page breaks before anything depending on the *final* page list resolves
against them. But `target-counter(_, page)`'s own loop can itself trigger up to three more
`LayoutDocument` passes afterward (its own comment: "the resolved page-number text's own width can change
line-breaking, which can change pagination"). `FootnoteAreaHeightsBySlot`/the internal per-slot call
grouping are never recomputed after the footnote loop's own last pass, so if a later `target-counter(_,
page)` pass shifts a page break, a footnote's call can end up on a different page than the one it was
numbered and reserved for - `AttachFootnoteAreas` (keyed by the now-stale slot numbers) may then attach a
page's footnote area to the wrong page, or drop it if the stale slot no longer has any content in the
final tree.

Only reachable by a document combining `float: footnote` with `target-counter(_, page)`/`leader()` (e.g. a
table of contents with page-number cross-references) *and* where resolving those cross-references actually
shifts a page break near a footnote. Properly closing this means re-running the footnote convergence loop
after the target-counter loop too (or merging the two into one combined fixpoint), which risks its own new
interaction bugs between two already-nontrivial convergence loops - judged out of scope for this change.

Filed as [issue #757](https://github.com/jhaygood86/PeachPDF/issues/757).
