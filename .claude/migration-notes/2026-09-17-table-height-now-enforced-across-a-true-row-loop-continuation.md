# A table's explicit height/min-height is now enforced even when its row loop must continue into a later top-level page

## What changed

**A `<table>`/`<tr>`'s explicit `height`/`min-height` (or, for a vertical table, `width`/`min-width`)
previously received no enforcement at all when a single cell's own content was large enough that the
table's row loop itself had to stop and resume on a separate, later top-level pass** — a narrow case,
distinct from an ordinary multi-page table (which takes its per-row page breaks *inside* one pass and
was already fully handled). The measure-then-redo mechanism this enforcement relies on could only
compare the table's declared size against the rows it had measured within a single pass; once the row
loop itself needed to hand off to a later pass, that comparison silently never ran, and the table
rendered at its natural (unenforced) size regardless of any `height`/`min-height` declared on it.

This was reachable only by a table whose one cell's own content was itself large enough to force that
kind of continuation — an uncommon, large-document scenario, not an everyday multi-page table.

Now, the table's total natural row-axis extent is tracked across every pass such a continuation needs,
and the final pass that actually finishes the table applies the same proportional-surplus enforcement
this engine already gives an ordinary single-pass table — but only to the rows that final pass itself
places. Rows already committed and drawn on an earlier page by an earlier pass are left completely
untouched, since redrawing them is not possible once their page has already been produced.

Rendered output changes only for the narrow scenario above: a table declaring `height`/`min-height` (or,
under a vertical writing mode, `width`/`min-width`) whose row loop genuinely spans a true top-level
continuation now has that declared size applied, on a best-effort basis, to its final page's remaining
rows, where it previously had no effect at all. This is deliberately not a full guarantee: rows already
committed and drawn on an earlier page cannot be grown retroactively, so the table's overall realized
size can still fall a little short of the declared value in this specific scenario — by however much of
the surplus those earlier rows would otherwise have taken. An ordinary table — including one that simply
spans many pages via per-row page breaks within a single pass — reaches the declared size exactly, as
before, and is otherwise unaffected. A table combining this continuation scenario with non-default
`border-spacing` or a `<caption>` is less precise still, tending to under-distribute the remaining
surplus (see `.claude/accepted-gaps/table-height-continuation-border-spacing-caption-precision.md`).
