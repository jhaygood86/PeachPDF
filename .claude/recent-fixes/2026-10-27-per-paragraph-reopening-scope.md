# A reopened page no longer retires the pruning marks of the whole document

**Symptom.** Same text, only the page height differs: 1000 paragraphs took 395 ms / 172 MB at 842pt and 72 ms / 21 MB at 820pt; 2000 paragraphs
at 842pt took 1.5 s / 631 MB against 158 ms / 40 MB at 820pt. Reported by the tier B spike (`2026-10-27-tier-b-relayout-spike.md`); it affects every
document under the default `orphans: 2; widows: 2`, i.e. ordinary PDF generation.

**Cause.** `FragmentEmitter.ScopeOwnerOf` scoped the "emitted nothing" marks and their `InvalidationHistory` to a box's ancestor that is a
direct child of the document root - `<html>` for every HTML document, so **one scope for everything**. At some page heights the default
orphans/widows move a paragraph after its first lines were emitted into the previous, frozen page (109 reopenings at 842pt, 2 at 820pt). A
reopening at slot r retires every mark in its scope naming a slot >= r, and marks are always written naming the frontier slot, so each page's
reopening retired all the marks just written. The next emission walks (`CatchUpStaleSlotsBehind`, then every `EmitPass`) re-entered the
boxes of every finished page: 116k + 112k full box entries against 8k at 820pt. The per-section scoping added for issue #917 never applied to
a plain HTML document.

**Fix (smallest that works).** `ScopeOwnerOf` now returns the box just below the *highest ancestor that branches* (has two or more
children that produce anything), so a flat list of paragraphs gets one scope each and a reopening only retires its own paragraph's marks.
The soundness argument is the one the code already made for per-section scopes: every box a relocation actually affects fires its own
reposition and discards its own and its ancestors' marks. Plus the one place that assumed a child shares its parent's scope:
`OrdinaryChildrenStart` now derives each child's own scope (see `.claude/invariants/fragmentation-an-emitted-nothing-mark-is-read-against-its-own-boxs-scope.md`).

**Not done: batching the stale slots into one walk** (the first candidate). Measured before building it: disabling the eager catch-up entirely
only moved the cost into `Finish`'s replay (110 full walks, 220k box entries) and left `EmitPass` degraded (116k entries) - half of the cost was
the lost marks, which batching cannot fix. Scoping removed both.

**Results** (Release, net8.0, best of 3, before -> after; allocation in MB):

| document | before | after |
| --- | --- | --- |
| 1000 paragraphs @842pt | 395 ms, 172 MB | 94 ms, 29 MB |
| 1000 paragraphs @800pt | 436 ms, 182 MB | 101 ms, 32 MB |
| 1000 paragraphs @1500pt | 263 ms, 107 MB | 92 ms, 31 MB |
| 2000 paragraphs @842pt | 1523 ms, 631 MB | 229 ms, 57 MB |
| other heights (700, 820, 860, 900, 1000) | 71-126 ms | 69-92 ms (unchanged) |

`BuildDraft` visits fell at every size (1200 paragraphs: 145,697 -> 125,059; 2000 paragraphs @842pt: 1.34M -> 458k).

**Verification.** Engine suite 16,523/16,523 (plain). With `PEACHPDF_VERIFY_FRAGMENT_PRUNING=1` the engine suite has the *same 13 failures with and
without the change* (pre-existing oracle-mode failures, all unrelated tables/columns/cost-count tests) and zero `PruningDiverged` reports;
`PeachBrackets.Tests` 68/68 and `PeachPDF.Tests` 1490 passed / 0 failed, plain and with the oracle.

**Traps found.**
- The first cut changed only `ScopeOwnerOf`; `StaleSlotReopeningCostTests` failed (box visits doubled) because the child-start cursor compared children's marks
  with the *parent's* scope. Fixed, and recorded as an invariant.
- `StaleSlotReopeningCostTests`' 8x ratio became 8.5x because the small case got cheaper (4,491 visits against 7,545) while the large one did too
  (38,101 against 50,423). The ratio check is now 12x plus an absolute ceiling at the old figure, and a new test compares the same document at 842pt and 820pt.
- A scripted file restore keeps the old timestamp and MSBuild then reuses the old binary: touch the file before benchmarking a "before" and "after" in one session.

**Still superlinear.** Visiting a finished paragraph to read its mark is still O(paragraphs) per page when the child cursor cannot advance
(the mark is per-paragraph and gets voided only for the reopened paragraph, but the cursor still stops at it). Linear emission would need the
cursor to skip past a reopened paragraph's neighbours, not just to a prefix.
