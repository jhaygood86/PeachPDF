# Text-geometry API on `HtmlDocument` (`RecordTextRuns`)

A consumer that paints offscreen (PeachChat via `Win2dComposer`) needs selection, copy and find, which needs to know where characters are.
`HtmlDocument.TextRuns`/`HitTest`/`GetCaretPosition`/`GetSelectionRects`/`GetText`/`FindText`/`GetWordAt`/`GetBlockAt` are the answer, behind
`HtmlDocumentOptions.RecordTextRuns`.

**Load-bearing idea.** The runs are built from the fragment tree after layout (`Html/Core/Text/TextRunCollector.cs`), the way
`DocumentAnnotationCollector` builds links - not recorded as a side effect of painting. A consumer can query geometry before or without
painting, `PaintPageAsync` is untouched, and nothing is computed unless a getter is read, so "no layout change" holds by construction (a test
compares layout with the flag on and off). Page coordinates are `ContentLeftPt + (x - MarginLeft) / ppp` and
`ContentTopPt + (localY - MarginTop) / ppp`, the same mapping the link collector uses.

**Things found by running it, not reading it.**
- A word's rectangle does not include the separator space before it. A run's `Bounds` must cover the gap its separator spans, or a point over
  that space lands on a neighbouring run (the character-centre hit-test caught this).
- `IsRtl` cannot come from a word's `BidiLevel`: Latin words in a `dir=rtl` paragraph are level 2 and stay in order at the right edge. It
  comes from the line owner's `Direction`.
- Hebrew in `dir=rtl` is stored visually (reversed, mirrored); the logical text is `CssRectWord.PreMirrorText`. Offsets are computed on the
  visual shaping and mapped back by index (`logical j` is `visual length - j`).
- A page whose divs hold no ink is not materialized, so a multi-page test needs backgrounds on its spacers.
- `PeachBrackets.Css` already has `TextPosition`/`TextRange`; `HtmlDocument.cs` aliases the public ones.

**Not done, deliberately.** Vertical writing modes, margin boxes and footnote bodies are skipped. A transformed run is reported at its layout
position. Letter-spacing is folded into the advances and then normalised to the rectangle layout gave the word, so uneven spacing is not modelled.

**Evidence.** `PeachBrackets.Tests` (40 tests, Debug and Release net8.0) including the wrapped `Lunch at <strong>12</strong>? See <a href=x>the
menu</a> 😀` paragraph; an overlay of selection rectangles and character edges on a rasterized page looked right.
