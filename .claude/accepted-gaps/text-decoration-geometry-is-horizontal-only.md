# Text-decoration geometry is horizontal-only

`FragmentPainter.PaintDecoration` reasons entirely on the physical x-axis: the span is
`new DecorationInterval(rectangle.X, rectangle.Right)`, each keyword picks a single `y`, and every
segment is stroked with `g.DrawLine(pen, segment.Start, y, segment.End, y)`. Under
`writing-mode: vertical-rl`/`vertical-lr` a box's physical x-range is the column's *thickness*, not
its extent along the line, so the decoration comes out as a short **horizontal** stroke across the
start of the first column instead of running alongside it.

Tracked as [#1075](https://github.com/jhaygood86/PeachPDF/issues/1075). Pre-existing and independent
of the skip-ink work; recorded here because that work is what made the boundary explicit.

## Verified, not assumed

```html
<div style="writing-mode: vertical-rl; height: 300pt; font: 20pt serif;
			text-decoration: underline">Vertical text with an underline</div>
```

Rendered through the Release CLI and rasterized with PDFium: one short horizontal line across the top
of the column, crossing the first glyph. A browser runs it down the column's length instead.

## Why the new subtractions are guarded rather than fixed

`text-decoration-skip-ink` (#1064) and the atomic-inline exclusion (#1066) are *also* x-axis
reasoning — a margin box measured left to right, a band swept horizontally across glyph ink. Applied
under a vertical mode they would not merely be misplaced, they would subtract the column's whole
thickness and **delete** the decoration rather than break it. So `PaintDecoration` computes
`var horizontal = IsHorizontalWritingMode(box)` and gates both `boxExclusions` and `inkWords` on it,
and `WantsInkFrom` repeats the check so a vertical box never even builds the word dictionary.

That keeps the vertical output exactly as wrong as it was before — no better, no worse — which was the
deliberate scope choice. Fixing the orientation is the actual fix, and it belongs with vertical
decoration geometry as a whole, not with either of those two issues.

`VerticalWritingMode_DrawsItsDecorationUncut` pins that the decoration is still drawn with real extent
(i.e. not deleted), and `VerticalWritingMode_NeverMeasuresInk` pins that no ink is measured there.

## The trap, if you fix this

The guard in `PaintDecoration` is **defensive, not currently reachable** for the exclusion half:
`CssLayoutEngine`'s vertical path records no per-line rectangle for an atomic inline at all, so the
walk finds nothing to exclude in the first place (verified by probing `CssBox.Rectangles`). It is kept
because that is a layout fact rather than a paint one — a future vertical-layout improvement that
starts recording those rectangles (see
[#771](https://github.com/jhaygood86/PeachPDF/issues/771)) would otherwise silently begin deleting
vertical decorations. Do not remove the guard as "dead code" without first making the geometry
axis-aware.

See also [no-vertical-writing-mode-layout.md](no-vertical-writing-mode-layout.md), whose
"What's still out of scope" list covers the neighbouring gaps
([#769](https://github.com/jhaygood86/PeachPDF/issues/769)'s nested-inline insets in particular, which
also affect which box a decoration is drawn against).
