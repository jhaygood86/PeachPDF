# Hinting follows the font's `gasp` table

Before: with `PdfGenerateConfig.TextHinting` set to `Standard` or `Monochrome` (or `GridFitting` asked of `PeachDrawing.Text` directly), a font was
fitted to the pixel grid at every size, including sizes at which the font's own `gasp` table says it does not want grid-fitting (many fonts turn
hinting off at the smallest sizes, and some at large ones).

Now: at a size whose `gasp` range does not have the grid-fitting flag, the text is the scaled design, as for a font that cannot be hinted. Fonts with no
`gasp` table, and sizes no range reaches, are fitted as before. The PDF's vector text is not affected (it is never hinted), and neither is anything when
`TextHinting` is `None`, its default.
