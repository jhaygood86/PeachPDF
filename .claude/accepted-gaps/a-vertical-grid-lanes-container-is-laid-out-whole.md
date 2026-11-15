# A vertical grid-lanes container is laid out whole on one page

A grid-lanes container in a vertical writing mode (`CssLayoutEngineGrid.Lanes.cs` with `_vertical`) is not fragmented: it is an orthogonal flow
in a horizontal page, whose lanes run across the page's block axis and whose stack runs along its inline axis, so a page boundary would cut every
lane at the same line rather than fall between items. `MonolithicContent.IsUnresumableVerticalGrid` keeps its parent from cutting it, exactly as
for a vertical grid (see `.claude/recent-fixes/` for that decision). css-break-3 does not forbid fragmenting it along its lanes, and CSS Writing
Modes 4 7.3.4 limits breaks in an orthogonal flow to between line boxes, so treating it as monolithic is within what a user agent may do; a faithful
implementation would be a general fragmentation of vertical grids, which this engine does not have.

Everything else the lanes pass does (placement, dense packing, reversal, alignment, subgrid, baseline, rtl, horizontal fragmentation) is
implemented; see docs/html-css-support.md.
