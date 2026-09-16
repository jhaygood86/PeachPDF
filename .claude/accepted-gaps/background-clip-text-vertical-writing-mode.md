# `background-clip: text` has no glyph-outline union under a vertical writing mode

Tracked as [#1123](https://github.com/jhaygood86/PeachPDF/issues/1123).

`background-clip: text` (issue #1117) clips a background layer to the union of a box's own laid-out
glyph outlines, built by `FragmentPainter.Decorations.cs`'s `BuildTextClipPath` walking the box's
fragment subtree and calling `RGraphics.GetTextOutline` per run. That walk only collects words from a
box in `writing-mode: horizontal-tb` (`IsHorizontalWritingMode`); a `vertical-rl`/`vertical-lr` box
falls back to a plain `border-box` clip instead - the same fallback a font with no decodable outline
at all (a bitmap font, or one this reader could not parse) gets.

**Why it's out of scope for now**: `RGraphics.GetTextOutline` has no rotation input, and the vertical
glyph-paint paths (`FragmentPainter.Text.cs`'s `PaintUprightVerticalRun` for upright runs, and the
sideways-rotation `PushTransform`+`DrawString` branch for rotated runs) have no outline-shaped
equivalent to build a clip path from - a caller wanting a rotated/stacked glyph outline union would need
to `Transform()` each run's returned path itself, an untested combination this repo's much larger
vertical-writing-mode effort (see [no-vertical-writing-mode-layout.md](no-vertical-writing-mode-layout.md),
tracked as #547) hasn't built yet. A narrower instance of that same general gap, not a new category of
limitation.
