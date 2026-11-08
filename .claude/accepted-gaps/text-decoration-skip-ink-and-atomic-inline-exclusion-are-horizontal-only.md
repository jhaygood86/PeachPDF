# Text-decoration atomic-inline exclusion (and upright-run skip-ink) are horizontal-only

Issue #1075 fixed `FragmentPainter.PaintDecoration`'s geometry orientation under a true vertical
writing mode (`vertical-rl`/`vertical-lr`): the span is now built along whichever physical axis is the
box's own inline axis (Y under a true vertical mode, X otherwise), and each keyword's cross-axis
position picks the correct physical "over"/"under" side (css-writing-modes-4 §6.3/§6.4) instead of
always reasoning in physical x/y. A decoration on vertical text now runs down the column, on the
correct side of the glyphs, matching browsers — verified by rendering the issue's own repro through the
Release CLI and rasterizing with both PDFium and MuPDF.

What #1075 deliberately left out of scope, tracked separately as
[#1145](https://github.com/jhaygood86/PeachPDF/issues/1145): `text-decoration-skip-ink` (#1064) and the
atomic-inline exclusion (#1066) are *also* x-axis-shaped band reasoning — a margin box measured left to
right, a band swept horizontally across glyph ink via `PdfCanvas.GetInkCrossings`. #1145 closed
the ink-crossing half for a **rotated** (sideways) run: it is one ordinary horizontal glyph run
reoriented as a whole (`DrawWordGlyphs`'s sideways branch), so `FragmentPainter.AddInkExclusions` can
measure it once the band is mapped into that run's own pre-rotation ("natural") frame and the resulting
crossings mapped back to physical Y the same way — see that method's own remarks for the derivation. The
gate is now `(horizontal || isVertical) && SkipsInk(...)` rather than `horizontal && SkipsInk(...)`.

What remains genuinely out of scope, unchanged by #1145:

- **An upright run's ink** (stacked character-by-character down the column, `PaintUprightVerticalRun`)
  has no single natural horizontal layout to reduce to the way a rotated run does — each character is
  its own independent glyph placement, not one reorientable run — so `AddInkExclusions` still skips
  (does not measure) an upright word's ink entirely (`IsUprightWordOrientation` check, early `continue`).
- **The atomic-inline exclusion** (`boxExclusions`, still gated on `horizontal` alone) remains fully
  out of scope for a true vertical writing mode — see "The trap" below, unchanged since #1075.

## The trap

The atomic-inline exclusion's own guard in `PaintDecoration` is **now load-bearing**: since the vertical flow lays an atomic
inline out as an item of its own (`CreateVerticalLineBoxes`, issue #771), `CssLayoutEngine` records a per-column rectangle for it,
so the exclusion walk would find something to exclude - and its band math is x-axis-shaped, so letting it run under a vertical
mode would silently delete vertical decorations instead of merely failing to break them. Do not remove the guard as "dead code"
without first making the exclusion band math axis-aware.

`VerticalWritingMode_DrawsItsDecorationUncut` (asserting the decoration is a true vertical stroke, not
merely "uncut") pins the atomic-inline-exclusion gap. `VerticalWritingMode_UprightRun_StillNeverMeasuresInk`
pins the upright-run ink gap; `VerticalWritingMode_RotatedRun_MeasuresInkInTheNaturalPreRotationFrame_AndBreaksTheLine`
(both in `TextDecorationSkipInkTests.cs`) pins the now-fixed rotated-run case.

See also [no-vertical-writing-mode-layout.md](no-vertical-writing-mode-layout.md), whose
"What's still out of scope" list covers the neighbouring gaps
([#769](https://github.com/jhaygood86/PeachPDF/issues/769)'s nested-inline insets in particular, which
also affect which box a decoration is drawn against).
