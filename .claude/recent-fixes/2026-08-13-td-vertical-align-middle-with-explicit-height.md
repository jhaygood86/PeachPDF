# `vertical-align: middle`/`bottom` no-op on a `<td>` with an explicit `height`

`CssLayoutEngine.ApplyCellVerticalAlignment` computed the cell's content-bottom via
`CssBox.GetMaximumBottom(cell, 0f)` — passing the **cell itself** as `startBox`. `GetMaximumBottom`'s
own-height fallback (`CssBox.cs`, `if (startBox.Height is not Keywords.Auto) currentMaxBottom =
Math.Max(currentMaxBottom, startBox.ActualBottom);`) exists so a childless box with an explicit height
still reports a sensible bottom. But by the time `ApplyCellVerticalAlignment` runs, the table row loop
(`CssLayoutEngineTable.cs`) has already set `cell.ActualBottom = rowMaxBottom` — so for a `<td>` that
also carries a CSS `height`, that fallback clamped the measured "content bottom" back to the exact same
row-equalized `ActualBottom` used as `cellBottom`. `(cellBottom - bottom) / 2` (middle) and
`cellBottom - bottom` (bottom) both collapsed to zero regardless of how much shorter the actual content
was — `vertical-align: middle`/`bottom` silently did nothing whenever the cell also had an explicit
`height`, which is exactly the combination most authors reach for (a fixed-height row with centered
text).

**Fix**: measure over the cell's *children* instead of the cell itself — seed at `cell.ClientTop` and
call `GetMaximumBottom` per child in `cell.Boxes`, never passing the cell itself as `startBox`. This
keeps the fallback's original intent (an explicit height still counts for a box with no children
reaching that far) while no longer letting the cell's own row-stretched `ActualBottom` masquerade as its
content's bottom.

**Existing coverage had actually documented the gap without closing it**:
`VerticalAlignIntegrationTests.Bottom_OnATableCell_PushesShortContentLowerThanTopAligned` had a comment
explicitly avoiding an explicit cell `height` "because it would make `GetMaximumBottom` clamp to the
cell's own already-row-equalized bottom" — i.e. the test was written *around* this bug rather than
catching it. New test `Middle_OnATableCellWithExplicitHeight_CentersShortContent` covers the case that
was actually broken (explicit `height` + `vertical-align: middle`); the stale workaround comment on the
older test was removed since the underlying limitation it described no longer exists.

`CssLayoutEngineTable.cs`'s own two `GetMaximumBottom(cell, ...)` call sites (line ~2791, and
`SpanningCellBandGeometry` at ~3142) were left untouched — at those call sites `cell.ActualBottom` has
not yet been stretched to the row's final height when the call happens, so the same clamp isn't
observed to misfire there; only `ApplyCellVerticalAlignment`, which runs strictly after the row loop
sets the final `ActualBottom`, was affected.

**Evidence**: new regression test added; `VerticalAlign`/`Table`/`Rowspan`-filtered suite (530 tests)
and full `PeachPDF.Tests` suite (6616 tests) pass on net8.0; `dotnet build -t:Rebuild` on the whole
solution is warning-free.
