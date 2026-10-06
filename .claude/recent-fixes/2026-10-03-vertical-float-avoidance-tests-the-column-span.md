# Vertical float avoidance tests the column's span; #1048's table symptom was a test artifact

Issue #1048 (after the reverted #1044 attempt).

- `ComputeColumnInlineSpan` used to ask only whether a column's *leading edge* lay in a float's block range.
  It now passes the prospective thickness (max of the strut line-height and the first word's
  `LineBoxExtentOf`) to `DomUtils.VerticalFloatCoversBlockPoint`, which with an extent does a
  positive-width overlap (0.01 tolerance). With extent 0 it keeps the old half-open point rule.
- The "table continued in balanced multicol emits words nowhere" symptom did **not** reproduce as lost
  words. `MulticolTableFontMetricTests` sweeps line-heights/offsets across two bundled fonts and every
  word is emitted exactly once with its centre inside the band. What looked like loss was
  `PaintedWords.LayOutAndCollectVisibleAsync` requiring the whole word rectangle inside the band: with
  `line-height` below the font's natural height the first line's word rect pokes ~2pt above the band
  (negative leading). Don't "fix" that in layout; the line box owns the word. A speculative change to
  `ResumedRowTop` had no effect and was dropped.
- `PinSansSerifAsync` also maps `Arial`, so `font: ... Arial` fixtures no longer resolve to a host fallback.
