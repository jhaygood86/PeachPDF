# writing-mode: sideways-rl / sideways-lr are vertical writing modes

They used to render as `horizontal-tb`. Now every "is this vertical" check goes through `WritingMode.IsVertical()` (any mode but `horizontal-tb`) instead of the ~30 hand-written `is VerticalRl or VerticalLr` tests, so `WritingModeFrame`, the vertical line flow, table/columns/flex/fragmentation guards and the paint code agree. SVG text keeps its own check and still treats them as horizontal.

- Glyphs: `CssBox.EffectiveTextOrientation` is `sideways` under the two modes whatever `text-orientation` says; layout (`CssLayoutEngine` word sizing), paint and the ink-skip code read it.
- `sideways-lr` needed almost nothing beyond that: `LogicalPropertyResolver.InlineStart` already said the inline start is the bottom (it was written for vertical + `direction: rtl`), so `WritingModeFrame.InlineStartIsBottom` and the existing bottom-start alignment and bidi paths did the rest. Only the glyph turn is new: `FragmentPainter.SidewaysRotationFor` rotates counter-clockwise (natural (x, y) lands at (rect.X + y, rect.Bottom - x)), and `AddInkExclusions` maps crossings through the mirrored transform.
- Verified by rasterizing boxes in all four modes (plus `text-align: end`, centred, underlined) in PDFium and MuPDF, which agree; the geometry tests compare `sideways-rl` word-for-word to `vertical-rl` + `text-orientation: sideways`.

Found by running it: baseline alignment of different font sizes inside a column is by the line-over edge, not a shared baseline - already true of `vertical-rl`/`vertical-lr` and left alone (see the accepted gap).
