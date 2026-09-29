# SVG `<pattern>` fills are PDF tiling patterns

**Before:** a pattern fill was drawn as a grid of individual image draws under a clip. Two consequences an author could see:
a pattern with `patternTransform` on a shape whose bounds sat away from the transform's origin (a rotated `dots` pattern on
an ellipse) often painted **nothing**, because the grid covered the wrong tiles; and a pattern with more than 10,000 cells
over the shape (a 2 x 2 pattern over a large rectangle) painted nothing at all.

**Now:** the shape is filled with a tile brush. An upright grid is emitted as one real tiling pattern (`/PatternType 1`): the
cell is written once and the viewer repeats it under the shape, with no cell-count limit, and the file no longer grows with
the number of cells. A grid that `patternTransform` (or a transform on the canvas) turns or skews is painted as separate cells
under a clip, as before, but the cells are now chosen from the shape's bounds so the whole shape is covered; a turned grid of
more than ten thousand cells uses a tiling pattern with the rotation in its matrix.

**What did not change:** `patternContentUnits="objectBoundingBox"` content still does not render (it did not before either),
and MuPDF may show faint seams between cells, as it did with the grid.

**A small change to know about:** a viewer may round the size of an upright pattern cell to whole device pixels (Chrome's
viewer does), so a pattern with a cell that is not a whole number of pixels at the viewer's zoom can drift a little from its
exact period across a wide shape; the per-cell drawing it replaces was exact. Verified by rasterizing before and after with
PDFium and MuPDF, and by measuring where marker squares land: identical for cells that are a whole number of pixels.
