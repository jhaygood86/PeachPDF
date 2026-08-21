## A rowspan cell spanning from `<thead>` into `<tbody>` now produces well-defined geometry (#788)

Closed the gap recorded in `.claude/accepted-gaps/rowspan-crossing-a-thead-tfoot-tbody-boundary-produces-unspecified-geometry.md`:
`TableGrid`/column-placement (built over the whole-grid `_allRows`) already treated a `<thead>`-opened
`rowspan` reaching past the header's own last row as reaching into `<tbody>`, reserving a column there -
but the header's own closing bookkeeping (`DetachAndMeasureRepeatedRowGroups`'s group-local
`headerSpanningCellsEndingOnRow`) closed the cell one row short, leaving a phantom, unfilled gap: no cell
painted there and no `CssSpacingBox` placeholder stood in for it either.

**Load-bearing idea**: reuse `TableRowCursor.RowSpannedBoxes` - the same map `TableRowCursor.Continuing`
already pre-seeds from a `TableBreakToken` when a resumed pass re-enters a row an earlier page left a
rowspan open in - for a cell an earlier *row-group* (not an earlier *pass*) left open instead.
`SeedCrossBoundaryRowSpans` registers each crossing cell into the body cursor's own `RowSpannedBoxes`, at
the real body-local row its span ends on (`ComputeHeaderRowSpansCrossingIntoBody`, using a new
`_allRowsOriginalIndices` - the header's own real per-row numbering continued, unbroken, into the body's -
to apply the exact same `visibility: collapse` remapping issue #665 already gives an ordinary body-opened
span), before the body row loop places its first row. From that point on, `CloseSpanningCell`,
straddle-correction, and pagination continuation (`TableRowCursor.Continuation`/`Continuing`) all treat the
cell as an indistinguishable, ordinary already-open rowspan cell - **zero changes needed to any of that
machinery**, confirmed directly by a real pagination test (the crossing cell's own body end-row landing on
a later page than the header, with the header repeating there too).

**Found only by running it, not by reading it** - two further, real bugs, caught by a post-change review
pass before this landed:

1. **`GetLastRowInGrid`** (`TableGrid`/column-placement's own "where does this span end" answer,
   independent of `ComputeHeaderRowSpansCrossingIntoBody`'s) still used raw, unclamped
   `gridRow + rowSpan - 1` arithmetic for a header row - correct only when the header has no
   `visibility: collapse` row of its own. A header rowspan crossing a collapsed header row landed
   `TableGrid`'s own column reservation one row off from where `InsertSpacingBoxesForSpan` actually placed
   the continuation placeholder, silently mis-shifting whichever real body row the two disagreed about.
   Fixed by routing a header-originated `gridRow` through the same collapse-aware
   `GetEffectiveEndRowIndex(gridRow, rowSpan, _allRowsOriginalIndices, ...)` `ComputeHeaderRowSpansCrossingIntoBody`
   already uses, rather than two independent, silently-divergent answers to the same question.
2. **A repeating header's own painted snapshot** (`CssProxyBox.SourceGeometry`, what `FragmentEmitter`
   actually builds the header's fragments from) is captured *before* the body row loop runs
   (`LayoutBodyRows` creates the header proxy ahead of its own row loop) - so a crossing cell's snapshot
   entry showed its natural, not-yet-closed height, even once the *live* box was correctly stretched by the
   body loop later. A `CssBox`-property test reading the live box alone could not catch this - exactly the
   layout/paint divergence this repo's own testing conventions warn about. Fixed with a new
   `BoxGeometrySnapshot.Resync`, called (`CssLayoutEngineTable.ResyncHeaderProxiesFor`) on every header
   proxy that already exists the moment a crossing cell actually closes; a proxy created afterward (a later
   page's repeat) needs no resync of its own, since it captures the already-closed live geometry directly.

Also found, by inspection while verifying reachability rather than by a failing test: `_allRowsOriginalIndices`'s
first-cut implementation rebased `_bodyRowOriginalIndices`' own values by a single constant (reasoning that
only each body row's index *relative to the first body row* mattered). That reasoning holds only when a
`<tfoot>` sits before every body row - legal HTML table markup can place one *between* two `<tbody>`
groups instead, where the footer's own one-unit slot perturbs only the body rows *after* it, not by a
shared constant. Replaced with a fresh, independent walk (mirroring `AssignBoxKinds`'s own row-counting
exactly, but never incrementing for the footer) before this landed, rather than shipping a rebase that
looked sufficient against every fixture actually written.

**Deliberately not done**: the mirror-image case - a `<tbody>`-opened rowspan reaching *into* `<tfoot>` -
hits the identical `GetLastRowInGrid`/`_bodyRowOriginalIndices` clamp from the other side (under-reserving
the footer's own column instead of over-reserving a phantom body one). Different code path, opposite
symptom, found while verifying `<tfoot>`-into-`<tbody>` is structurally unreachable (confirmed: `_allRows`
always places footer rows last, so a footer-opened span has nothing after it to reach into) - recorded as
its own accepted gap ([`.claude/accepted-gaps/rowspan-crossing-from-tbody-into-tfoot-under-reserves-the-footers-column.md`](../accepted-gaps/rowspan-crossing-from-tbody-into-tfoot-under-reserves-the-footers-column.md),
tracked as [#792](https://github.com/jhaygood86/PeachPDF/issues/792)).

**Evidence**: new regression tests in `CssLayoutEngineTableTests.cs` (the minimal thead-into-tbody repro,
a `visibility: collapse` header-row sibling, a `<tfoot>`-between-two-`<tbody>`-groups sibling, and the
header-proxy-snapshot-resync assertion), `TableRowspanContinuationTests.cs` (the real-pagination case, with
the header also repeating onto the later page), and `TableWritingModeIntegrationTests.cs` (a `vertical-lr`
sibling, passing unchanged on the first attempt - the mechanism operates purely on grid row/column indices,
never physical coordinates, so it needed no axis-specific handling of its own); full
`dotnet test --framework net8.0` suite green; 100% diff coverage; zero `dotnet build PeachPDF.slnx -t:Rebuild`
warnings.
