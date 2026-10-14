# Per-line and per-box allocation in the multi-column and line layout paths

Allocation profile of the 1,272-page multi-column document (`AllocAnalyzer`, first PeachPDF frame): after the index-hint change the
largest direct allocators that were not the content itself (words, boxes, fragments) were small per-line and per-box leftovers.
Removed: the float list in `CssLayoutEngineColumns.Layout` was a `SelectMany` that built an array per child (154 MB, now a loop);
`BoxEdgesDrawHandler.DrawBoxEdges` allocated a closure over its parameters on entry to every call for a case that is rare (translucent
dotted/dashed edge groups), now in its own method; `FragmentEmitter.IsOverflowNoColumnClaims` allocated a closure and a list per
candidate (now a reused scratch list, read only until `OpenAtTheRightIfLastInItsRow` returns); `CssLayoutEngine.BubbleRectangles` and
`ApplyVerticalAlignment` built a `List` per box per line through `CssLineBox.WordsOf` (now loops over the line's words);
`ApplyBidiReordering` allocated two arrays per line before finding there was nothing to reorder (now returns first for a line whose
words are all level 0 in a left-to-right block); `PdfGraphics` text calls took a new `PdfStringFormat` per call
(`PdfStringFormats.SharedDefault`, read-only by convention: nothing in `PDF/` or `PdfBackend/` changes a format it is handed).

Allocation: multi-column document 2,226 -> 1,934 MB, prose document 1,592 -> 1,454 MB; wall time is inside the noise of the machine.
Output is unchanged (benchmark documents, 199 of 200 fuzz documents, suite).

**Replacing the painter's `_painted` `HashSet<BoxFragment>` with a stamp on the fragment did nothing** (A/B, interleaved, best of 5:
prose 6.5-6.9 vs 6.8-7.2 s, multi-column 10.1-10.9 vs 9.8-10.9 s) although `RuntimeHelpers.GetHashCode` shows 9-30% under
`PaintStackingParticipant` in sampling profiles. That frame is the safepoint a sampling profiler lands on, the third time it has been
measured this way; do not chase it from a profile again without an A/B.
