# Presentational `border`/`cellpadding` table attributes now actually reach `<td>` cells, closing issue #636

[Issue #636](https://github.com/jhaygood86/PeachPDF/issues/636) reported that the deprecated
presentational `border` and `cellpadding` HTML attributes on a `<table>` never actually cascaded to
its `<td>` cells - `DomParser.ApplyTableBorder`/`ApplyTablePadding` (both via the shared
`SetForAllCells` cell-finding helper) silently did nothing observable, even though `SetForAllCells`
correctly found and mutated the right `CssBox` objects.

Root cause was not `SetForAllCells`'s traversal (the issue's own suspected cause) - a debug trace
proved it found and set the right cell's `PaddingLeft` every time. The real cause is cascade timing:
`TranslateAttributes` (which reads the table's `border`/`cellpadding` attributes and drove these two
methods) runs mid-cascade, from inside `CascadeApplyStyles` for the **table box itself**, before a
`<td>` descendant's own `CascadeApplyStyles` call has run (the recursion into `box.Boxes` happens
*after* `TranslateAttributes` returns for the current box - see the loop at the end of
`CascadeApplyStyles`). Setting a property on the cell that early makes the cell's `ComputedStyle`
diverge from the shared `ComputedStyle.Default` singleton. `CascadeApplyStyles`'s own "defaulting"
step has a documented fast path that skips re-asserting every property's CSS initial value when a
box's `ComputedStyle` is still that shared `Default` (a guaranteed no-op) - but once diverged, the
cell's own **still-pending** `CascadeApplyStyles` call takes the *other* branch and re-asserts every
property's initial value, silently clobbering the border/padding value just written from the table.

Fixed by splitting the per-cell cascade out of `TranslateAttributes` into its own pass,
`DomParser.ApplyTablePresentationalAttributesToCells`, invoked once right after the top-level
`CascadeApplyStyles(root)` call returns (i.e. after the *entire* tree's cascade has finished) and
before `CorrectAnonymousTables` runs - so the tree still has its as-authored shape, which is what
`SetForAllCells`'s traversal (one level for a bare `<tr>`, two levels through an explicit
`<thead>`/`<tbody>`) already expects. `TranslateBorder` still sets the table's own border
unconditionally, same as before; only the cell-cascading half of the work moved.

## Load-bearing lessons

**A traversal bug and a clobbering bug produce the identical symptom** (the target property reads
back as unset) **and only a debug trace distinguishes them.** The issue's own suspected cause -
`SetForAllCells` not finding the right boxes - was plausible and specific, but wrong; adding a
temporary `File.AppendAllText` trace into `SetForAllCells` and the test (`ACTION on l2 hash=...`
immediately followed by `FOUND cell hash=...` with the *same* hash, but `paddingLeft=0` anyway) proved
the write happened on the right object and then evaporated - which redirected the investigation to
*when* the write happened relative to the rest of the cascade, not *where*.

**A "no-op fast path" gated on reference-equality to a shared default is a trap for any code that
mutates a box before that box's own initialization has run.** `CascadeApplyStyles`'s defaulting-loop
skip (`if (!ReferenceEquals(box.ComputedStyle, ComputedStyle.Default))`) is correct and cheap for the
ordinary case (nothing has touched the box yet), but it means *any* out-of-band property write against
a box that hasn't had its own cascade pass yet will be silently reverted once that pass finally runs -
not just this table/cellpadding path. Worth checking for the same shape (write-before-own-cascade) if
a similar "translate this attribute onto some other box" mechanism is added later.

## Evidence

Five tests in `PresentationalAttributeIntegrationTests.cs`
(`BorderAttribute_OnTable_CascadesASolidBorderToCells`, `BorderAttributeZero_OnTable_DoesNotCascadeABorderToCells`,
`BorderAttribute_OnATableWithATbody_StillCascadesToCells`, `CellpaddingAttribute_CascadesToTableCells`,
`CellpaddingAttribute_OnATableWithATbody_StillCascadesToCells`), written test-first and confirmed to
fail against the pre-fix code with the exact symptom the diagnosis predicted (padding/border reading
back as the CSS initial value), then confirmed to pass post-fix. Full suite green (8875 tests,
net8.0), zero warnings on `dotnet build -t:Rebuild`, and manual inspection of the collected Cobertura
coverage confirms both new branches (border/no-border, cellpadding/no-cellpadding) in the new
`ApplyTablePresentationalAttributesToCells` pass are exercised in both directions.
