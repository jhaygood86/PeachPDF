# Table height/min-height enforcement across a true row-loop continuation is imprecise with border-spacing/caption

[Issue #1132](https://github.com/jhaygood86/PeachPDF/issues/1132) made a table's explicit
`height`/`min-height` (or, on a vertical table, `width`/`min-width`) enforced on a best-effort basis even
when the table's own row loop must continue into a separate, later top-level pass — previously that
scenario received no enforcement at all. `CssLayoutEngineTable.ThisPassNaturalRowAxisContentLength` sums
each pass's own row-axis content length across the chain, folding in the table's leading row-axis
border/top-caption/border-spacing on the chain's first pass only, and netting out the single trailing
row-axis gap `LayoutBodyRows`' Step 7 adds per pass so it isn't double-counted.

That accounting is verified correct — algebraically, and by test — for the common case (no caption, no
non-default `border-spacing`). It is **not** precise when a genuinely continued table also declares
non-default `border-spacing` or a `<caption>`: the natural row-axis extent a **stopped** pass's own row
(the one whose cell reported it could not finish) contributes to `_naturalGridFarEdge` does not correspond
in an easily-modeled way to that row's eventual natural height in an undisturbed layout, and how the row
loop's own bookkeeping (`TableRowCursor`, `RowHeightFloor`'s "raise, never shrink" carve-out) settles that
row's contribution mid-stop was not fully reverse-engineered while building #1132's own test coverage.

Narrow in practice: it only matters for a table that both (a) genuinely needs a top-level row-loop
continuation (a single cell's content large enough to need a third fragmentainer — already an uncommon,
large-document scenario) and (b) also declares non-default `border-spacing` or a `<caption>`. The observed
failure mode is under-counting the surplus (erring toward *not* redistributing, via the existing
`surplus <= 0` no-op guard) rather than corrupting geometry or growing rows unboundedly. Filed as
[issue #1195](https://github.com/jhaygood86/PeachPDF/issues/1195).
