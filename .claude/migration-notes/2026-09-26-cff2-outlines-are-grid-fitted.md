# Variable fonts with CFF2 outlines are grid-fitted

Before: with `PdfGenerateConfig.TextHinting` set to `Standard` or `Monochrome` (or `GridFitting` asked of `PeachDrawing.Text` directly), a variable font
whose outlines are CFF2 (a `CFF2` table) was not fitted: the text it drew into pixels was the scaled design outline at the font's location, as for a font
that cannot be hinted.

Now: such a font is fitted by the stem hints and blue zones of its charstrings and Private DICTs, blended for the location the text uses (`font-weight`,
`font-stretch`, `font-variation-settings`), the way a font with CFF outlines is, so stems and flat edges land on whole pixels at small sizes. `TextStemDarkening`
applies to it too. The PDF's vector text is not affected (it is never hinted), and neither is anything when `TextHinting` is `None`, its default.
