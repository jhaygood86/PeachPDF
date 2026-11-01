# A flowed inline-block with a declared line-height paints its line box height (#1694)

An `inline-block` whose content fits on one line is flowed into the surrounding line, and its painted
rectangle came from the bubbled-up word extents, i.e. the font's content area. CSS 2.1 §10.6.6/§10.8.1:
its height is its line boxes, each at least the inherited `line-height`, so the background, border and
transform pivot must cover the whole line (issue repro: 56px line-height, 14pt font: Chrome 34.3x42pt,
PeachPDF 34x16pt, 13pt too low).

`CssLayoutEngine.ExtendInlineBlockRectanglesToLineHeight` runs after `HeightenAtomicInlineRectangles`
and grows the rectangle by the leading (`line-height - font content area`), half above and half below,
so the glyphs do not move. Deliberately narrow:

- **Only a declared `line-height`** (`DerivedStyle.HasDeclaredLineHeight`). With `normal` the leading is
  the font's line gap, 0.1-0.2pt: correct in principle, but it moved eleven pinned geometry assertions
  (`InlineBlockDeclaredHeightGeometryTests`, `InlineBlockHeightRegressionTests`, one
  `InlineBlockOverflowClipPaintTests`) for no visible gain. Revisit if a `normal` case shows up.
- **Only a one-run box** (rectangle inner height equals the box's own font content area): children in
  other fonts have struts this does not model.
- A declared `height`/`min-height` wins (handled by the Heighten pass). A plain `inline` never grows.

Evidence: `InlineBlockLineBoxHeightTests`; engine (17014) and PeachPDF.Tests suites green on net8.0.
