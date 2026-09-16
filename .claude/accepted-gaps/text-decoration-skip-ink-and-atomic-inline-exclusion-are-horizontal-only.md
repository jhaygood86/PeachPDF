# Text-decoration skip-ink and atomic-inline exclusion are horizontal-only

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
right, a band swept horizontally across glyph ink via `GraphicsAdapter.GetInkCrossings`. Neither
understands a vertical inline axis, so both stay gated off under a true vertical writing mode exactly
as before #1075: `PaintDecoration` computes `var horizontal = IsHorizontalWritingMode(box)` and gates
both `boxExclusions` and `inkWords` on it. A vertical decoration is now drawn with its full, correctly
oriented extent (per #1075), but is never broken around an atomic inline or a glyph's ink there.

## The trap, if you fix #1145

The guard in `PaintDecoration` is **defensive, not currently reachable** for the exclusion half:
`CssLayoutEngine`'s vertical path records no per-line rectangle for an atomic inline at all, so the
walk finds nothing to exclude in the first place (verified by probing `CssBox.Rectangles`). It is kept
because that is a layout fact rather than a paint one — a future vertical-layout improvement that
starts recording those rectangles (see
[#771](https://github.com/jhaygood86/PeachPDF/issues/771)) would otherwise silently begin deleting
vertical decorations. Do not remove the guard as "dead code" without first making the exclusion band
math axis-aware.

`VerticalWritingMode_DrawsItsDecorationUncut` (now also asserting the decoration is a true vertical
stroke, not merely "uncut") and `VerticalWritingMode_NeverMeasuresInk` pin the current (post-#1075,
pre-#1145) behavior.

See also [no-vertical-writing-mode-layout.md](no-vertical-writing-mode-layout.md), whose
"What's still out of scope" list covers the neighbouring gaps
([#769](https://github.com/jhaygood86/PeachPDF/issues/769)'s nested-inline insets in particular, which
also affect which box a decoration is drawn against).
