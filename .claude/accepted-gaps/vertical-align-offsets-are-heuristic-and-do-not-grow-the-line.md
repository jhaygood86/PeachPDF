# `vertical-align` sub/super/middle are heuristic, and a raised or lowered box does not grow its line

Two separate deviations from CSS 2.1 §10.8.1 that surfaced while an empty inline-block's baseline and an
empty positioned inline's `vertical-align` were fixed. Both predate that work and both are shared by every
box, not only empty ones.

- **`sub`/`super`/`middle` arithmetic** (#2074): `CssLayoutEngine.VerticalAlignDeltaOf` lowers by 0.5 x the
  box's own height and raises by 0.2 x it, and `middle` centres in the line box, where the spec (and
  Chromium/WebKit's `font-size / 5 + 1` and `/ 3 + 1`, Gecko's font metrics) use the parent's font.
- **The line does not grow for an offset box** (#2075): a length/percentage/`sub`/`super` offset moves the box
  after the line was sized, so a raised box can stick out of the line box that Chrome would have made taller.
  An empty inline behaves like text here, as in Chrome.
