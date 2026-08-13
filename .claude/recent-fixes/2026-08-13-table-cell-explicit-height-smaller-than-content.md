# A `td`/`th` `height` smaller than its content overlapped the next row

`height: 1px` on a cell (a legacy "definite-height containing block" author hack) made the row's
real content overlap the row below it instead of laying out at its content-driven height. Per CSS 2.1
§17.5.3 a cell's specified `height` only ever feeds the row-height calculation (`max(row's own
height, each cell's height, the minimum height the cells' content requires)`) - it must never clip
or shrink the cell below what its content needs.

`CssLayoutEngine.ApplyHeight`, called directly on the cell itself in `PerformLayoutEpilogue`
(`cell.PerformLayout(g)`, strictly *before* `CssLayoutEngineTable.LayoutBodyRow` stretches every cell
to the row's shared height - see `ApplyParentHeight`'s own comment on that ordering) applied §10.6.3's
ordinary-block rule ("a definite height is the used height regardless of content") to the cell,
overwriting the correct content-driven `ActualBottom` that `CreateLineBoxes` had just set with the
cell's too-small specified height.

Because the row loop takes `Math.Max` over every cell's `ActualBottom` to find the row's real height,
a row where *every* cell had an explicit height smaller than its content (the reported repro) ended
up with a `rowMaxBottom` far shorter than any cell's actual content - the next row started right
under it while the still-there, unclipped text (the lines themselves were never moved, only
`ActualBottom` was wrong) drew on top of it.

**Fix**: `ApplyHeight` now exempts a table-cell box (`CssBox.IsTableCell`) from the direct-overwrite
branch, falling back to its existing `Math.Max(box.ActualBottom, ...)` branch instead - exactly "at
least the content's required height", since `ActualBottom` already holds the true content bottom by
the time `ApplyHeight` runs on it.

**A neighboring, unconditional "handle limiting block height when overflow is hidden" clamp in
`CreateLineBoxes` looked at first like a second clamp site, and was initially patched too - it isn't
one.** `ActualBottom`'s getter (`CssBox.StyleProperties.cs`) is `Location.Y + ActualBoxSizingHeight`,
and `ActualHeight` is `ActualBoxSizingHeight` itself, so that branch's guard
(`ActualBottom - Location.Y > ActualHeight`) is always false by construction - dead code for every
box, not just table cells. Confirmed by instrumenting the branch against the repro: it never fired.
The exclusion added there during investigation was reverted since it changed nothing and would have
misdescribed the actual fix to a future reader. Left as-is rather than fixed/removed here since it's
an unrelated, pre-existing dead branch, not part of this defect - flagged separately for its own
follow-up.

**Evidence**: two new regression tests in `CssLayoutEngineTableTests.cs`
(`ExplicitCellHeightSmallerThanContent_DoesNotClipCellOrOverlapNextRow`,
`ExplicitCellHeightSmallerThanContent_StretchesEveryCellInTheRow` - the second cross-checks against an
independently-built reference table containing only the tall cell's content, rather than an arbitrary
threshold, so it actually fails when the tall cell's own `ActualBottom` is wrongly clamped before the
row-max comparison runs, not merely when the row ends up shorter than *some* fixed number); the
reported repro re-rendered and rasterized through both PDFium and MuPDF per this repo's
paint-verification convention, confirming non-overlapping, content-driven row heights on both;
`Table`/`VerticalAlign`/`Flex`/`Grid`/`Multicol`-filtered suite and full `PeachPDF.Tests` suite pass on
net8.0; 100% diff coverage on the changed lines; `dotnet build -t:Rebuild` on the whole solution is
warning-free.
