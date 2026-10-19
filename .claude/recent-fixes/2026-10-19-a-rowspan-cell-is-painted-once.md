# A rowspan cell is painted once

[#1834](https://github.com/jhaygood86/PeachPDF/issues/1834), found rendering business-continuity reports with merged cells
against Chrome.

`FragmentEmitter.ChildrenOf` yields a `CssSpacingBox`'s `ExtendedBox`, so a spanning cell is walked through its own row and
again through every placeholder in the rows it covers. Its comment assumed each copy lands "in its own place", but every copy
reads the same live geometry, so within a slot each is the same cell at the same rectangle, and the paint log drew each word
2N−1 times (3 for `rowspan="2"`, 5 for `rowspan="3"`): bolder on the page and extracted 2N−1 times by a reader.

The load-bearing idea mirrors `_liveFloatWordsThisWalk`: `_spanningCellsDrawnThisWalk`, cleared wherever a walk starts, records
a spanning cell once it has a fragment in the slot being walked, and a placeholder yields the cell only if it has none yet.
Recorded after `BuildDraft` returns a draft, not before, so a slot the cell's own row does not reach (the continuation case
`TableRowspanContinuationTests` pins) still gets it from its first placeholder there. A suppressed placeholder is never recorded
as empty for pruning: `ContentStaysInOneRun` already excludes `CssSpacingBox`.

## Evidence

- `TableRowspanCellPaintedOnceTests` (paint-log census, spans 2/3/5, both border models, no-span control) fails on `main` and
  passes here; `TableRowspanContinuationTests` is unchanged apart from its remark on the duplicate fragments.
- Full suite on net8.0: 17,451 tests, the one failure (`AnchorLinkDestinationTests…NearThePageTop`) also fails on `main`.
- Diff coverage of the changed library lines: 9/9.
- The issue's repro through `PeachPDF.Cli` now matches Chrome 149: `SPANNING` drawn once.
