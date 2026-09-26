# The font and text engine must not reference the rest of PeachPDF

`src/PeachPDF/Fonts` and `src/PeachPDF/Text` may reference only `PeachPDF.Fonts` and `PeachPDF.Text` (and the BCL).
Not `PeachPDF.PdfSharpCore`, `PeachPDF.CSS`, `PeachPDF.Html`, `PeachPDF.Svg`, `PeachPDF.Adapters`, `PeachPDF.Raster`,
`PeachPDF.MathML`, and not the root `PeachPDF` namespace's types (`RuneRange`, `PdfGenerator`). The engine is being
extracted into its own assembly (`PeachDrawing.Text`), which PeachPDF references, so it cannot reference PeachPDF back:
every such edge is a compile error at extraction time, and they arrive in dozens at once if they are allowed to build up.

`EngineIndependenceTests` scans both folders' source and fails naming the file and line. When it fires, the fix is
never to relax the test; it is to invert the dependency:

- Something the engine needs from a PDF/CSS/HTML type becomes an **engine-owned type**, and the outer layer converts at
  its boundary. Examples that already exist: `FaceStyle` and `SyntheticStyle` (the engine's view of `XFontStyle`/
  `XStyleSimulations`), `EmojiMode` (of the CSS `font-variant-emoji` enum, mapped by `EmojiModeMapping`), `RuneInterval`
  (of the public `RuneRange`, converted in `PdfGenerator.AddFontFromStream`), `FontLock` (of the PDF layer's lock), `FontMessages`.
- Something that only *exists* because of the outer layer belongs in the outer layer. Examples: the 256-entry WinAnsi
  `/Widths` table (`PdfSimpleFontWidths`), the default PDF font encoding (`GlobalFontSettings`), the descriptor and glyph
  typeface caches that are keyed on `XFont`/`XGlyphTypeface`, and `CssUnicodeBidiMapping` (the CSS half of the bidi
  integration; the engine only exposes the explicit-push types it maps onto).

The measured cost of not holding this line: the first pass over the engine found ~15 files with PDF-writer dependencies,
including a two-way one (`XGlyphTypeface` called back into `FontResolver`, which held a dictionary of `XGlyphTypeface`).
