# A table's declared column widths can now shrink to fit its own explicit `width`/`height` instead of always overflowing it

**Landed:** 2026-08-25 — Fix `CanReduceWidth`'s inverted bounds check and the resulting dead shrink pass (#819)
**Doc section:** none — internal table auto-layout mechanics, not a separately itemized doc row
**Verified against v0.9.13:** `CssLayoutEngineTable.CanReduceWidth(int)` at that tag has the same inverted
bounds check (`_columnWidths!.Length >= columnIndex`), so `ShrinkColumnsToFitAvailableWidth` was
unconditionally dead code at that release too — confirmed genuine behavior change since that release.

A table whose declared/content-driven column widths summed to more than the table's own explicit `width`
(or, under a vertical writing mode, explicit `height`) used to always render wider than that explicit
value — the table simply grew to fit its columns, with no shrink ever applied, because the internal method
responsible for shrinking columns toward their content-minimum width (`ShrinkColumnsToFitAvailableWidth`)
had a bug that made it unconditionally a no-op. It now shrinks columns — never below a column's own
content-minimum width, an explicit `min-width`/`min-height` on any of its cells, or (specific to a vertical
table) a cell's own explicit `height`, which CSS 2.1 §17.5.3 makes an unshrinkable per-cell floor — to make
the table fit its own explicit size when there is room to do so without clipping content. A document that
relied on the old always-wider-than-specified behavior (for example, to guarantee a fixed layout regardless
of `<col>`-declared widths) will now see such a table narrower, matching the specified `width`/`height`
more closely. A table with `max-width` set was and remains unaffected in the cases that actually clip
(`ClipColumnsToMaxWidth` was already live) — this only changes tables that previously overflowed a `width`/
`height` without also having `max-width` force a clip.

A vertical table (`writing-mode: vertical-rl`/`vertical-lr`) with no explicit `height` of its own, and no
definite height anywhere up its containing-block chain either, is unaffected — there is no genuine
column-axis constraint to shrink toward in that case (an auto-height container's `Size.Height` is not yet
resolved at the point a vertical table's columns are sized, unlike `Width`, which block layout always
resolves top-down before a child lays out), so the table's columns continue to size purely from content as
before.
