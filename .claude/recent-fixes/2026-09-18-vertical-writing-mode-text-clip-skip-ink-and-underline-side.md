# Vertical-writing-mode background-clip:text, rotated-run skip-ink, and text-underline-position: left/right

Closes issues #1123, #1145, #1146 - three gaps all rooted in the same rotation/upright-run geometry
`FragmentPainter.Decorations.cs`/`FragmentPainter.Text.cs` already built for #1075/#1153.

## The load-bearing idea

A **rotated** (sideways) run under a true vertical writing mode is one ordinary horizontal glyph run
reoriented as a whole by `SidewaysRotation`'s fixed 90° matrix (`DrawWordGlyphs`'s sideways branch). That
means any per-glyph geometry work that already understands a horizontal run - `GetTextOutline`,
`GetInkCrossings` - can be reused unchanged as long as it operates in the run's own **natural
(pre-rotation) frame** and the caller maps positions/bands through `SidewaysRotation`'s own transform (or
its algebraic inverse) at the boundary. Concretely, `SidewaysRotation` maps natural `(x, y)` to physical
`(rect.Right - y, rect.Y + x)`:

- **#1123** (`BuildTextClipPath`'s new `CollectRotatedWord`): build the outline once for the whole word at
  natural origin `(0, baselineAdjust + font.Ascent)`, then call `outline.Transform(SidewaysRotation(rect))`
  - the exact same matrix `DrawWordGlyphs` pushes before its `DrawString` call, so the two can never
  disagree on where a rotated run's glyphs land. An **upright** run has no such single natural layout (each
  character is placed independently), so it's built character-by-character instead, translating each
  outline to the exact cell `EnumerateUprightGlyphPlacements` (extracted from `PaintUprightVerticalRun`,
  now shared by both paint and clip-building) already resolves for paint.
- **#1145** (`AddInkExclusions`): a decoration's physical cross position (e.g. physical X for a true
  vertical box) maps to a natural Y via the transform's inverse (`naturalY = rect.Right - physicalX`); a
  band of half-thickness around it is measured in that natural frame via the ordinary `GetInkCrossings`,
  and the resulting natural-X crossings map back to physical Y (`physicalY = rect.Y + naturalX`) for the
  exclusion intervals. An upright run is left unskipped - same reasoning as #1123's upright/rotated split.

## What was found by running it, not by reading it

- The vertical-mode ink-skip band, when centered on the *resolved* (clearance-adjusted) underline
  position rather than a real alphabetic baseline, does not reliably land on a real font's actual
  descender ink - a real "gy" fixture at 20pt measured a band entirely past the real descender's own
  natural-Y range, so `GetInkCrossings` legitimately found nothing. This is a structural property of
  vertical mode's existing rect-relative underline-position approximation (undocumented risk before this
  work), not a bug in the rotation math itself - confirmed by re-testing with **scripted** ink (this
  file's own `Overline_MeasuresItsOwnBand_AndBreaksWhereInkCrossesIt` precedent, for the identical reason:
  a real font can't demonstrate the wiring either way), which reliably exercises the same code path.
- `RecordingGraphicsPath.Transform` (`PeachPDF.Tests.TestSupport`) was a silent no-op before this change -
  a test built on it could not tell a correctly-transformed rotated-run outline from one where
  `Transform` never ran at all. Made it actually apply the matrix to every recorded point, per this
  repo's own "a paint feature needs more than a parser test" convention; the #1123 paint tests
  (`RotatedRun_BuildsOneWholeWordOutline_TransformedIntoItsPhysicalFootprint`) would not have caught a
  no-op `Transform` otherwise.
- A first pass at #1146's pinned-underline offset direction shared the same sign for both the clearance
  and offset terms; the sibling (non-pinned) formula uses *opposite* signs for the two (clearance pulls
  inward, offset pushes further away, per `text-underline-offset`'s own "moves further from the text"
  contract). The bug was invisible whenever `text-underline-offset` was unset (the overwhelming majority
  of the initial test matrix), since it only manifests as a wrong-direction shift once a real offset is
  present - caught only once a directed offset+position:left/right test was added.
- The pre-existing `blockEndInset`/`underSign` block-end padding compensation (`PaintDecoration`, dating
  to #1075/#1153) was written assuming every decoration keyword sits on the same physical edge it always
  did before pinning existed - true for every case before #1146, but not after: a pinned underline or a
  switched overline can now sit on *either* physical edge. Generalized to a per-keyword `isAtOverEdge`
  fact (also now driving `StrokeDecorationSegment`'s `double`-style growth direction, which had the
  identical blind spot - a switched overline's second stroke was growing back through the interior
  instead of continuing outward past its own new edge) and a `blockStartInset`/`blockEndInset` pair,
  picking the correct side's own padding/border. Horizontal-tb's own decoration geometry is unaffected
  (verified against the full existing suite): `isAtOverEdge`'s vertical-only inputs
  (`pinnedUnderlineEdge`/`overlineSwitchesSides`) are already null/false there, so it reduces to exactly
  the pre-#1146 `line == Overline` test for growth direction, and the inset stays gated to `isVertical`
  so horizontal's own single block-end-only inset is untouched.
- The spec's own overline-switch note ("if this causes the underline to be drawn on the over side...")
  is conditioned on an underline actually being drawn - `overlineSwitchesSides` now also requires
  `underline` to be one of the box's declared `text-decoration-line` keywords, so a box with only
  `overline` never gets moved just because `text-underline-position` happens to name the conflicting side.

## What was deliberately not done, and why

- **An upright run's ink is still unskipped** (issue #1145's own accepted, narrowed scope) - it has no
  single natural horizontal layout to reduce to the way a rotated run does; extending skip-ink to it
  would need a genuinely different (per-character) band-scanning approach, not a reuse of the existing
  rotation-based reduction.
- **The atomic-inline exclusion remains fully out of scope under a vertical writing mode** - unrelated to
  the rotation trick above; `CssLayoutEngine`'s vertical path records no per-line atomic-inline rectangle
  at all yet, so there is no layout fact to exclude around regardless of paint-side changes.
- **A real vmtx/VORG-metrics font's upright run falls back to `border-box` for `background-clip: text`**
  (tracked as new issue #1194, its own accepted-gap file) rather than shipping a clip shape wider than
  what is actually painted: `PaintUprightVerticalRun` clips each such character to its own reserved cell
  (a real vmtx advance is routinely narrower than the font's line height), but `RGraphicsPath` has no
  path-intersection primitive to reproduce that per-cell clip in the unioned outline geometry. Building
  one is a real, separate undertaking (a polygon-clipping algorithm), not a small addition alongside this
  PR's own scope.

## Evidence

Full `PeachPDF.Tests` suite (net8.0, 12390 passed) green; 97% diff coverage against `origin/main`
(`diff-cover`, remaining gap is `TextUnderlinePositionCompoundConverter`'s `Construct`/`Original`/
`ExtractFor` interface boilerplate, mirroring the uncovered shape of the existing `RunningFunctionConverter`
precedent). All three features' new/extended showcases
(`vertical_writing_mode_background_clip_text`, `vertical_writing_mode_decoration`) rendered through the
real `PdfGenerator` pipeline and rasterized with both PDFium and MuPDF - both engines agree pixel-for-pixel
on the upright/rotated glyph-outline gradient clip, the rotated-run skip-ink break, and the
pinned-underline/switched-overline physical positions (including the double-style growth-direction fix,
re-verified visually after the growSign correction).
