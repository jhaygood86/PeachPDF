# `float: footnote` inside a table cell, flex item, or grid item is untested and unsupported

`DomParser.DetachFootnoteBodies` runs once, over the whole tree, before any layout engine (table, flex,
grid) starts - so a `float: footnote` box nested inside a table cell/flex item/grid item is walked and
detached the same as anywhere else, and the synthesized call takes its place as ordinary content before
`CssLayoutEngineTable`/`CssLayoutEngineFlex`/`CssLayoutEngineGrid` ever see that subtree. That is enough
for the common case to *probably* work, but this codebase's per-engine layout algorithms (table row/
column bookkeeping and repeated-header proxying in particular) have enough of their own assumptions about
child content that this combination has not been exercised or tested, and is not a supported combination
for this version - notably, a footnote *inside a repeated `<thead>`/`<tfoot>`* would need its call
re-numbered identically on every page the header repeats onto, which nothing here accounts for.

Mirrors the caution already recorded in
[running-element-inside-table-cells.md](running-element-inside-table-cells.md) - a different mechanism,
but the same "an engine's own per-cell/per-item bookkeeping is more invasive to audit safely than the
generic tree walk" caution. Filed as
[issue #750](https://github.com/jhaygood86/PeachPDF/issues/750).
