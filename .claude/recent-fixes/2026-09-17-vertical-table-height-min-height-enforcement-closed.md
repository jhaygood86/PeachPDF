# `width`/`min-width` on a `writing-mode: vertical-*` `<table>`/`<tr>` now establish a row-axis minimum

[Issue #1116](https://github.com/jhaygood86/PeachPDF/issues/1116) made `height`/`min-height` on a
`<table>`/`<tr>` establish a real CSS 2.1 §17.5.3 minimum for `writing-mode: horizontal-tb` tables, via a
measure-then-redo mechanism in `CssLayoutEngineTable.PerformLayout`, but deliberately left vertical
tables out of scope (`.claude/accepted-gaps/vertical-writing-mode-table-row-height.md`, now deleted) —
the row axis for a vertical table is physical width, and `height`/`min-height` are physical properties
that instead size a vertical table's *columns*.
[Issue #1131](https://github.com/jhaygood86/PeachPDF/issues/1131) closes that gap.

## The row-axis extent tracking was already axis-generic; only the decision was gated off

`TryComputeRowHeightRedistribution`/`RowHeightFloor` both hard-gated with an early
`if (_isVertical) return false/0;`, but the geometry they read (`_naturalGridFarEdge`,
`row.ActualBottom - row.Location.Y`, `cursor.MaxBottom`/`startY`) turned out to already be axis-generic
throughout `CssLayoutEngineTable` — physical X for a vertical table's row axis, physical Y otherwise —
a consequence of this engine's existing writing-mode work
(`.claude/accepted-gaps/no-vertical-writing-mode-layout.md`). Only the *decision* to enforce a floor was
hard-gated off, not the extent-tracking itself, confirmed by reading `LayoutBodyRows`' own Step 7 before
writing any code: `_naturalGridFarEdge = gridBorderBoxBottom`, and `gridBorderBoxBottom` is computed from
`contentBottom` (which already tracks physical X for a vertical table per its own inline comments) and
`TableRowAxisBorderEnd` (already `_isVertical`-aware).

The fix is a narrow axis swap, not a parallel mechanism: `CssLayoutEngine.GetBoxWidth(CssBox)` — a new,
deliberately narrow overload mirroring `GetBoxHeight`'s explicit-length/min-clamp/max-clamp behavior but
**without** its initial-containing-block pinning or absolutely-positioned top/bottom-fill branches
(neither is reachable for a table/row box, which CSS 2.1's table box model keeps always in-flow or
floated) — gives `TryComputeRowHeightRedistribution`/`RowHeightFloor` a width-based resolver to pair with
the existing height-based one, selected by `_isVertical` the same way `CellInlineSize` and its siblings
already select between `cell.Height`/`cell.Width` elsewhere in this same file.

The min-width trap that bit #1116's original height version (`min-width`'s initial value is the literal
string `"0"`, not `"auto"`, so `IsValidLength` alone is true for every row) has an identical width-side
guard (`row.MinWidth != "0"`) — but unlike `GetBoxHeight`, the new `GetBoxWidth` has no unsafe
`ActualBoxSizingHeight`-style fallback baseline to fall through to in the first place (it returns `null`
rather than reading any stale/reused field when neither `width` nor `min-width` is set), so that guard is
purely for symmetry and to skip an unneeded call here, not a correctness requirement the way it was on
the height side.

## What was found by running it, not by reading it

Getting `GetBoxWidth`'s own `max-width`/`min-width` clamp ordering wrong (applying `max-width` *after*
folding `min-width` in, rather than before) silently made `max-width` win over `min-width` on conflict —
backwards from CSS 2.1 §10.4. Caught by a direct unit test asserting the conflict case explicitly
(`GetBoxWidthTests.MinWidthWinsOverMaxWidthOnConflict`), not by reading the code, since the non-conflict
cases all passed regardless of the ordering bug.

## Deliberately not done

`GetBoxWidth(CssBox)` is intentionally scoped to exactly what this table-engine consumer needs
(definite-length parsing plus max/min clamping) rather than a full mirror of `GetBoxHeight`'s
generality — a future caller needing the ICB-pinning or absolute-position-fill branches has to add them
deliberately, not inherit them silently from this narrower purpose.

## Evidence

Seven new unit tests for `CssLayoutEngine.GetBoxWidth(CssBox)` (`GetBoxWidthTests.cs`, mirroring
`HeightDefinitenessTests`' direct-call, post-layout assertion pattern: definite length, percentage
against the containing block, no-explicit-size-returns-null, min-width-alone, min-width-clamp,
max-width-clamp, min-wins-over-max-on-conflict) and six new vertical-table integration tests in
`TableWritingModeIntegrationTests.cs` (`<tr>`/`<table>` `width`/`min-width` enforcement growing and
not-shrinking, two-row proportional surplus distribution, a rowspan cell crossing a redistribution-grown
row — mirroring the horizontal `#1116` suite's own structure and naming convention on the other axis).
Full `PeachPDF.Tests` suite green on net8.0. Diff coverage 100% via `diff-cover` against `origin/main`.
`dotnet build -t:Rebuild` on the whole solution is warning-free.
