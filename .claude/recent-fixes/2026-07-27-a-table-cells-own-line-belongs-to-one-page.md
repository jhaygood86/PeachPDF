# A table cell's own line belongs to one page

_Landed 2026-07-27._

[CSS Fragmentation Level 3 §4.1](https://www.w3.org/TR/css-break-3/#possible-breaks) — a line box is a
monolithic break unit.

A table cell with more text than a page holds painted the line on the boundary **twice**: its top half
at the foot of one page and its bottom half at the head of the next, the same line drawn under two
different page clips. Measured on a 300-word cell as 300 words laid out and **312 emitted**, with 12 of
them (one whole line) claimed by both fragmentainers.

**The load-bearing observation is that the identical text in the identical cell was already correct one
wrapper away.** `CssLayoutEngine.FlowBox`'s per-word boundary check was guarded
`box is { IsFixed: false, IsTableCell: false }`, so it skipped a word whose owner box *is* the cell —
but a word inside `<td><p>…</p></td>` belongs to the `<p>`, which is not a cell, and took the check
normally. A sweep of 200–320 words over three shapes says it outright: bare `<td>` duplicates at 244
words, `<p>`-in-`<td>` and a plain `<div>` never do. That asymmetry is what makes the exemption a
defect rather than a rule — there is no reading of §4.1 under which the same text breaks differently
because of a wrapper.

With nothing to stop the line straddling, the emitter did what it is supposed to do: `FragmentRegion`
claims a rectangle that overlaps a band by more than the epsilon, and a straddling line overlaps
**both**. So the double-paint is downstream of the layout defect, not a second bug.

**`IsFixed` stays**, and for a real reason: a fixed box repeats at the same page-box position on every
page (CSS 2.1 §13.3.1), so a page boundary means nothing to its words.

**What this does not do** is make a table cell fragment its content through the break-token model. The
cell's words still relocate one at a time via `CssRect.BreakPage`, because `CssBox.LayoutContents`
runs the table under `LayoutMonolithicContent` and the engine reads no resumption record — that is
[#390](https://github.com/jhaygood86/PeachPDF/issues/390) stage 4, and this fix is what the shape of it
looks like from outside until then. The `else` arm in `FlowBox` that this restores a caller to is
exactly what [#400](https://github.com/jhaygood86/PeachPDF/issues/400)'s remaining half is blocked on.

**All 67 pre-existing showcases are byte-identical**, which is why this survived: none of them has a
bare-`<td>` line landing on a page boundary. New showcase `paged_media_table_cell_lines` does, and on
the unfixed build `word150 word151 word152 word153 word154` is visibly cut through the middle on both
pages. Verified in both PDFium and MuPDF.

Tests: `TableCellLineFragmentationTests` (5 — the no-duplication sweep over all three shapes, no line
spanning the boundary, and the cell still paginating every word). Verified load-bearing by restoring
the exemption: two fail, the `<p>`-in-`<td>` and `<div>` controls pass either way. Full net8.0 suite
green (6573), CLI green (96), 0 warnings, 100% diff coverage.
