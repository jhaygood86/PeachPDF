# Collapsed table borders now center on the true grid-line, closing issue #744

[Issue #744](https://github.com/jhaygood86/PeachPDF/issues/744) reported a `border-collapse: collapse`
table's `<td>` background bleeding about 1px past a shared `border-bottom` into the row above.
Root cause: `GetGridLineY`/`GetGridLineX` in `CssLayoutEngineTable.cs` returned one *edge* of an
interior grid line's row/column overlap band (`row.ActualBottom`, a cell's `Location.X`/`ActualRight`),
not the band's center - but `EmitCollapsedBorderSegments`/`EmitHeaderFooterBorderSegments` always
built each border segment's rect *centered* on that returned value. For an outer table edge this is
correct (the row's own half-reservation and the table's own separately-applied half sit on
non-overlapping sides of that point), but for an interior line the two neighboring rows/cells
deliberately overlap by the *whole* resolved border width (the #735 fix's own design - see
`2026-08-13-collapsed-table-borders-css-2-1-17-6-2.md`), so centering on one edge only covered half
that overlap, leaving the other half open for the later-painted neighbor's background to show
through. Fixed by making both methods (plus `SnapshotLineY`, a local function duplicating the same
logic against `BoxGeometrySnapshot` for repeated `<thead>`/`<tfoot>` proxies) return/compute the true
center, subtracting or adding half the resolved line width depending on which edge the raw value
named.

## Load-bearing lessons

**A doc comment can be stale evidence of the bug it's describing.** `GetGridLineY`'s old comment
claimed "a boundary's two neighboring rows already agree on it exactly" - flatly contradicted by
`VerticalSpacingAt`/`HorizontalSpacingAt`'s own documented `-width` (not `-width/2`) interior-line
overlap, which the fix's own new doc comment cites. The mismatch between what a comment claims and
what the code next to it actually does was itself the signal that this "row above's `ActualBottom`
stands for the shared line" claim was never true post-#735, not a coincidental phrasing choice.

**Fixing the same defect in one of several structurally-parallel call sites and missing another is
easy, and this diff did exactly that once.** A post-change review agent (cross-file-tracer angle)
found that the header/footer's own internal vertical-divider segments still read
`SnapshotLineY(boundaryLine)` *uncorrected* even after the horizontal boundary segment's own
`boundaryY`/`correctedY` got the fix - both read the same conceptual grid line, but only one path
carried the correction, so a vertical divider spanning a repeated group's full row range would
visibly fail to meet the corrected horizontal boundary at the corner. Fixed by making
`SnapshotLineY` itself apply the correction whenever `line == boundaryLine`, computed once
(`boundaryHalfWidth`) and shared by every consumer - the vertical-divider loop, the per-page
`ResolveRepeatedGroupBoundary` branch, and the no-adjacent-row fallback branch - rather than each
computing (and needing to remember to compute) its own copy.

**A review finding that "looks like" a bug can still be refuted by tracing the actual layout code,
not just the border-segment-emission code.** A second review angle (altitude) flagged that the
per-page `boundaryY` correction reads `model.HorizontalLineWidth[boundaryLine]` (the static,
DOM-order-resolved model) rather than the fresh per-page border width `ResolveRepeatedGroupBoundary`
resolves against the real visually-adjacent row, and argued this could drift on a table with
heterogeneous row border widths across pages. Refuted by reading where a repeated header/footer's
physical page-break position actually comes from: `cursor.CurrentY += headerRoom` where
`headerRoom = _headerHeight + VerticalSpacingAt(HeaderRowCountInGrid)` (and the mirrored footer
case), both keyed off the *static* model uniformly on every page, regardless of which row a
repeated header ends up above. The physical space reserved for the boundary line is fixed once by
the static model; only the *painted* border's style/color/width legitimately varies per page. Using
the static model's width for the position correction is therefore self-consistent with how layout
actually reserved the space - using the fresh per-page width instead would have been the bug.

**Independent-of-the-computation-under-test ground truth is what actually catches a regression.**
A first attempt at a regression test for the vertical-divider consistency bug compared the vertical
segment's endpoint to the horizontal boundary segment's own derived center
(`boundary.Rect.Y + boundary.Rect.Height / 2`) - this passed even with the fix's correction zeroed
out, because both values are built from the *same* shared `boundaryHalfWidth`, so they drift
together and never disagree with each other regardless of whether that shared value is right. The
test that actually failed pre-fix and passed post-fix compares against the midpoint of two values
computed by an entirely separate code path (`headerProxy.ActualBottom` and the first body row's own
`Location.Y`, set by the ordinary row cursor in `LayoutBodyRows`) - genuinely independent of
`EmitHeaderFooterBorderSegments` entirely.

## Evidence

Four new tests in `CollapsedBorderGeometryTests.cs` assert exact `CollapsedBorderSegment.Rect`
geometry (not paint order, which `CollapsedBorderPaintTests.cs` already covered and would not have
caught this class of bug) for: an interior horizontal line, an interior vertical line, a repeated
`<thead>`'s boundary-to-body line, a `<thead>`-immediately-meets-`<tfoot>`-with-no-body-rows corner
case, and the vertical-divider-meets-boundary consistency case - each written test-first, confirmed
to fail against the pre-fix code with the exact half-border-width discrepancy the diagnosis
predicted, then confirmed to pass post-fix. Full suite green (8838 tests, net8.0), 100% diff
coverage, zero warnings on `dotnet build -t:Rebuild`. The reporter's own repro HTML was rendered
through the CLI and rasterized with both PDFium and MuPDF, before and after the fix - both
renderers agree the background bleed is gone.
