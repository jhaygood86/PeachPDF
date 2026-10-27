# A rowspan cell is drawn once (#1834)

The fragment emitter reaches a spanning cell through its owning row and through the `CssSpacingBox` standing in for it in each later row, and built a draft per visit; all had the same rectangle, so the text was painted 2N-1 times (visibly bolder, and extracted N times).

`BuildDraft` now remembers the `FragmentKey` of each cell flagged `IsRowSpanningCell` (set by the `CssSpacingBox` constructor) per walk and returns null on a repeat visit. The set is cleared with `_liveFloatWordsThisWalk` in both the pruned and the reference walk. Keyed by the full `FragmentKey`, not the box, so a cell split over several columns/instances keeps one draft per instance. A cell continuing onto a later page still gets its draft there: the set is per walk, and a walk is one slot.

Evidence: the issue's repro extracts `SPANNING` once (was 5); engine and PeachPDF suites green. The old comment in `TableRowspanContinuationTests` calling the duplication an accepted limitation is now stale in spirit but its assertions still hold.
