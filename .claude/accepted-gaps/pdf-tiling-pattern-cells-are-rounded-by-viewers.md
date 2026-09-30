# PDF viewers round the cells of an upright tiling pattern to whole device pixels

An upright `TileBrush` is written as a PDF tiling pattern (`/XStep`, `/YStep`, `/BBox`), and PDFium (Chrome/Edge's viewer)
renders the cell into a bitmap whose size is a whole number of device pixels and repeats *that*. Measured on a 14-unit
(10.5 pt) cell viewed at 5 device pixels per point: the cell repeats every 10.6 pt, not 10.5 (52.5 pixels rounded to 53), so
the pattern's phase drifts by about a tenth of a point per cell across a wide shape. `/TilingType` (1 constant spacing, 2 no
distortion) makes no difference in PDFium. When the cell is a whole number of pixels (a 10.5 pt cell at 4 px/pt) the
positions are identical to exact per-cell drawing.

Why it is accepted: it is a property of how the viewer draws every tiling pattern, not of what PeachPDF writes, and the
alternative (drawing every cell as an image) is exactly what makes a large pattern cost a file entry per cell and stops
working past ten thousand cells. Rotated and skewed grids, where viewers also show seams, are already drawn cell by cell, so the
drift only affects upright grids. A pattern whose phase must match other content to the pixel at every zoom should be drawn
as cells; `GraphicsAdapter.TryPaintTurnedTiles` is the place to add such a rule.
