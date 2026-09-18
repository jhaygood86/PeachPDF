# Patterned and bevelled outline styles keep a ring per line (issue #1206)

A wrapped inline's `outline` is unioned into one connected shape only for `solid`, `double` and
`auto` (`OutlineDrawHandler.SupportsRegionOutline`). The other six — `dotted`, `dashed`, `groove`,
`ridge`, `inset`, `outset` — fall back to the pre-union geometry of a closed ring per line.

This is a genuine deviation: CSS Basic User Interface 4 §5.2 draws a fragmented outline as if the
fragments were joined, and does not exempt any `outline-style`. It is tracked as #1206, not left as
prose alone.

## Why each of the six needs something the merged region does not define

- **`dotted`/`dashed`.** The dash fit is per straight run against the ring's *outer* edge
  (`BoxEdgesDrawHandler`), which works because a ring has exactly four runs of known length. A merged
  contour has runs of arbitrary length and concave corners between them, so the fit has to be
  re-derived over a closed polyline — a different algorithm, not a parameter change.
- **`groove`/`ridge`/`inset`/`outset`.** These colour each *side* light or dark. A merged region has
  no four sides: at a concave corner two consecutive edges face the same direction, so "which side is
  this" has no answer. A per-edge rule keyed on the edge normal would have to be invented. Chromium
  does not have one either — it picks these bevel colours per rectangle side.

Falling back renders those six exactly as they rendered before the union landed. Rendering them
through the union without solving the above would render them *wrongly*, which is worse than
rendering them as they always have.

## What would close it

Dash fitting over a closed polyline, and a bevel-shade rule defined on edge normal rather than box
side. Both are additive to `OutlineRegionPainter`; `RectilinearRegion` needs no change. The
`PatternedOutlineOnAWrappingInlineElement_FallsBackToARingPerLine` theory in
`OutlineStylePaintIntegrationTests.cs` is the test that would have to be rewritten — it currently
asserts the fallback, so it is also the tripwire that says this gap is still open.

Reader-facing note lives in `docs/html-css-support.md` (without the issue number, per the docs rule).
