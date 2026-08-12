# A table's caption is painted inside the table's own border/background box, not a separate wrapper box

[CSS 2.1 §17.4](https://www.w3.org/TR/CSS21/tables.html#caption-position) models a `<table>` and its
`<caption>` as living inside an anonymous "table wrapper box": the table's own `border`/`background`/
`padding`/`margin` apply to the *grid* only, and the caption sits outside that box (above or below it,
per `caption-side`), inside the wrapper instead.

PeachPDF doesn't synthesize a separate wrapper box. `CssLayoutEngineTable` (`LayoutCaptionGroup`,
called from `LayoutCells` for a top caption and from `LayoutBodyRows`' Step 7 for a bottom one) positions
each caption inside the same `CssBox` that represents the `<table>` element itself — the one whose
border/background/`ActualBottom` painting already covers the grid. A table with a visible `border` or
`background-color` therefore paints that border/background behind the caption's own area too, rather
than around the grid only. Verified visually (PDFium and MuPDF rasterization, agreeing) as part of the
`caption-side` implementation: a bordered table's border encloses both the caption and the row grid as
one rectangle.

Tracked as [#721](https://github.com/jhaygood86/PeachPDF/issues/721).

**Deliberately out of scope** of the `caption-side` positioning fix (top/bottom stacking) this gap was
found alongside. Closing it properly means giving a `<table>` with a caption an anonymous wrapper
ancestor that owns the margin box and paints nothing itself, while the existing `CssBox` continues to
own the grid's border/background/padding exactly as it does today — a structural change (a new anonymous
box kind, plus updating every place that currently treats "the table's `CssBox`" and "the table's outer
box" as the same box) well beyond what stacking the caption above or below the grid needed. Most
real-world tables put borders on cells rather than on `<table>` itself, which is why this reads as
cosmetic in practice rather than as a correctness bug most documents would hit.
