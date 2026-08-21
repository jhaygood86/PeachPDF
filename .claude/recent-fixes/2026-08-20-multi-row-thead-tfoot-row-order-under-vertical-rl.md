## A multi-row `<thead>`/`<tfoot>` reverses its own internal row order under `vertical-rl` (#784)

Closed the one gap #762 deliberately left open: `CssLayoutEngineTable.ReflectRowAxisForVerticalRl`
mirrors a `vertical-rl` table's rows once its final row-axis bounds are known, converting the engine's
"grow forward as if `vertical-lr`" placement into true right-to-left growth. Individual `<tbody>` rows
are each passed in separately and get their own mirror delta, correctly reversing their relative order.
A `<thead>`/`<tfoot>` group was passed in as ONE entry (`_headerBox`/`_footerBox`), so it got ONE shared
delta — correct for a single-row group (the row's own delta and the group's own delta are the same
number, the only shape #762 tested), but a multi-row group's own rows kept their forward-grown relative
order instead of reversing.

**Load-bearing idea**: generalize the file's own pre-existing "residual on top of a coarser shift"
composition trick — already used to correct a rowspan cell's own footprint on top of its opening row's
uniform shift — one level up. Each internal row of a multi-row group now also gets a residual
(`rowDelta - groupDelta`) applied via `OffsetLeft` on top of the uniform delta it already received by
cascading down from its group's own `OffsetLeft`; since `OffsetLeft` is purely additive, the row's total
received shift becomes `groupDelta + residual == rowDelta`, the same delta an independently-reflected row
gets. The `rowspanFixups` cell scan was widened the same way, since it previously only ever saw the
row-GROUP as one entry (`row.Boxes` returning `<tr>`s, not cells) and so could never find a rowspan cell
nested inside a multi-row group at all, regardless of row count.

This alone only fixes the *detached* row objects — what `GetGridLineY`/`GetGridLineX`/
`EmitCollapsedBorderSegments` read directly off `TableGrid.RowAt`/`CellAt`. A repeating group's actually
*painted* content instead comes from its own `CssProxyBox.SourceGeometry`, a frozen `BoxGeometrySnapshot`
captured in `CssProxyBox.PerformLayoutImp` necessarily before the table's final row-axis bounds (and this
reflection) are known — the only existing touchpoint between the reflection sweep and an already-captured
snapshot was `CssProxyBox.OnTranslated`/`BoxGeometrySnapshot.Translate`, which only supports one uniform
shift for the whole snapshot, with no way to give one internal row a different shift than another's. Fixed
with a new scoped method, `BoxGeometrySnapshot.ReflectSubtree(CssBox root, double dx)`, that applies the
same per-row (and per-rowspan-cell) residual directly into the snapshot, for whichever proxy(s) repeat that
row's own group (`ReferenceEquals(proxy.SourceBox, group)`).

**Found only by working through it, not by reading it**: the residual is safe to apply once, from
whichever state the shared detached `_headerBox`/`_footerBox` happens to be in, to *every* proxy's own
snapshot — even though a vertical table cannot currently repeat its header/footer more than once (a
vertical table is monolithic, `_headerRepeats`/`_footerRepeats` require `!_isVertical`), so this is really
a soundness proof for the general case rather than something a fixture can exercise end-to-end today. The
residual `rowDelta - groupDelta` algebraically reduces to `(groupRight0 + groupLoc0) - (rowRight0 +
rowLoc0)` — the table's own `min`/`max` bounds cancel entirely — which is invariant under adding any
shared constant to all four inputs, i.e. under any uniform translation of the whole group. Since
`CssProxyBox.PerformLayoutImp` always moves every row of a group by one shared delta per page, two
different pages' own capture of the same relative row layout differ by exactly such a constant, so the one
residual computed from the shared detached rows is correct for every page's snapshot regardless of which
page it represents.

A first cut of the snapshot-sync only propagated each row's own residual, missing that a rowspan cell
nested in that row needs its own *additional* residual applied to the snapshot too (composing on top of
the row's, exactly like it composes on top of the row's live `OffsetLeft`) — caught immediately by the new
rowspan-inside-multi-row-`<thead>` test (`span.Location.X` in the live tree vs. the snapshot disagreeing
by exactly the missing residual, 34pt in the failing run) before this landed, not discovered later.

**Deliberately not done**: a rowspan cell opening in a *single*-row header/footer group and spanning down
into `<tbody>` is still never found by the `rowspanFixups` scan (single-row groups are excluded from the
widened scan, since a rowspan "entirely contained" in a 1-row group is impossible by construction) — this
predates #784 (any header/footer-scoped rowspan cell was already unreachable by the pre-existing scan,
regardless of row count) and is a different, narrower scenario (a cross-group span) than what #784's own
scope covers ("entirely contained within a multi-row group"), so left as a separate, still-open gap rather
than folded into this fix. `BoxGeometrySnapshot.ReflectSubtree` also doesn't skip a descendant that
escapes its ancestor's translation (`CssBox.EscapesTranslationOf`, the guard `CssBox.OffsetLeft`'s own
cascade already has) — pre-existing in `Translate` too, not newly introduced, but this fix's per-row
residual can now give an absolutely/fixed-positioned descendant inside a header/footer cell a different
wrong shift depending on which row it sits in, rather than one at least uniformly-wrong shift.

**Evidence**: four new `CssBox`-property tests (`TableWritingModeIntegrationTests.cs`) asserting reversed
row order in both the live (detached) tree and the painted proxy snapshot, for a 2-row `<thead>`, its
`vertical-lr` sibling (confirming no regression — forward order is genuinely correct there), a 2-row
`<tfoot>`, and a rowspan cell entirely inside a 2-row `<thead>`; the existing single-row
`VerticalTable_WithTheadAndTfoot_ProxiesFlankTheBodyAlongTheRowAxis` test passing unchanged (1-row groups
take the same code path as before); full `dotnet test --framework net8.0` suite green (9047/9056, 9 
pre-existing platform-specific skips); zero `dotnet build PeachPDF.slnx -t:Rebuild` warnings; 100% diff
coverage; PDFium+MuPDF rasterization of a new `writing_mode` showcase section (7c) — a combined multi-row
`<thead>`+`<tfoot>` table and a separate rowspan-inside-multi-row-`<thead>` table — agreeing pixel-for-pixel
between both renderers.
