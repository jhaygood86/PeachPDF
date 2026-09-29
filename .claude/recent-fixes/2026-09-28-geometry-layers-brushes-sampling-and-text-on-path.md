# Geometry, layers, tile brushes, sampling and text on a path

One change adding the drawing capabilities a gap analysis against a general-purpose drawing library found missing, each with the
PeachPDF code that switched onto it. The load-bearing decisions, and what running it (not reading it) turned up:

- **Everything PeachPDF adopts is public API.** `PeachDrawing.*` no longer grants internals to PeachPDF, so each adoption forced
  the API to be right rather than reachable. `GraphicsPath.GetCurveContours` is the seam: measuring, boolean operations and
  text-on-path all read curves through it, not through flattening.
- **Boolean operations keep the curves.** Cut every curve at every intersection (recursive subdivision; overlapping lines report the
  ends of the overlap), classify each piece by the winding number just to each side of it against *both* shapes, keep the pieces
  where the result differs across the piece, orient them so the result is on the left, and link. Classifying by the two sides -
  rather than by "is this piece inside the other shape" - is what makes a self-intersecting input and coincident edges work. The
  test that earned trust is a seeded randomized one against a sampled reference over all four operations.
- **The tiling-pattern matrix has to be built the way the gradients' coordinates are, and symmetric test tiles hide it being
  wrong.** The renderer writes y-up coordinates, converting each point as it goes (`WorldToView`), and keeps user transforms in
  the XGraphics world transform, not in the content stream's matrix. A first version composed the pattern matrix from the view
  matrix and CTM alone: it looked right with stripes and dots (vertically symmetric) and was mirrored and offset. It was caught
  only by rendering an *asymmetric* tile on the baseline and the branch and comparing pixels. The matrix is now found by mapping
  three brush-space points through the brush matrix, `WorldToView`, and the view matrix; the tile is placed flipped inside the
  pattern because a form is upright in y-up space and the pattern space is the brush's y-down space. Markers measured to land at
  identical positions to the old per-cell drawing.
- **A rotated tiling pattern shows hairline seams in viewers; a grid of separate cells does not.** So a grid the transform turns
  is drawn as cells (with the cell range taken from the shape's bounds mapped into brush space, which is also what fixes the old
  code's coverage bug), and only an upright grid, or a turned one of more than 10,000 cells, is a pattern.
- **PDFium rounds pattern cells to whole device pixels**, which was invisible until a cell was a non-integer number of pixels:
  found by scanning a row for where a bar starts and seeing a 10.6 pt period for a 10.5 pt cell. `/TilingType` did not change
  it. Recorded as an accepted gap rather than worked around.
- **Adoption found a bug.** The old SVG pattern code rendered a `patternTransform`-rotated pattern on an ellipse as nothing (its
  grid covered the wrong tiles) and gave up past 10,000 cells; the tile brush fixes both. See the migration note.
- **A file I generated with Python's default encoding was cp1252**, not UTF-8 (a `§` in a comment), and another edit truncated a file
  by opening it for write before reading. Both were caught by the build/`git checkout`; write files with an explicit encoding.
- **Anti-aliasing stays a ceiling on the raster canvas.** `PushAntiAlias(true)` must not override a render that turned smoothing off
  (`RasterAntiAliasing = false` is for reproducible output); only `false` can force it off.

Deliberately not done (each has an accepted-gap file): repeating backgrounds/`border-image` still draw per tile; the CSS filter
chain and outset shadows keep `BeginRasterSurface`; the wavy decoration keeps its own construction. `PathText` is adopted for
SVG `<textPath>` glyph frames only - the per-glyph `dx`/`dy`/`rotate`/bidi/paint handling stays in SVG.

Evidence: solution rebuild 0 warnings; PeachPDF.Tests 14,444, PeachDrawing.Tests 220, PeachDrawing.Core.Tests 133 (net8.0). SVG
pattern page rasterized before/after in PDFium and MuPDF; the full showcase set compared against a clean `main` build.
