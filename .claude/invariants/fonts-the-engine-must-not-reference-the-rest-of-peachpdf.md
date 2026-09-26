# The font and text engine must not reference the rest of PeachPDF

The engine is its own assembly, `src/PeachDrawing.Text` (NuGet package `PeachDrawing.Text`), which PeachPDF references.
It therefore cannot reference PeachPDF back: not `PeachPDF.PdfSharpCore`, `PeachPDF.CSS`, `PeachPDF.Html`, `PeachPDF.Svg`,
`PeachPDF.Adapters`, `PeachPDF.Raster`, `PeachPDF.MathML`, and not the root namespace's types (`PdfGenerator`, `RuneRange`).
The project reference direction enforces it at compile time; there is no separate guard test. (Before the assembly existed,
a source-scan test held the same line for the `Fonts/` and `Text/` folders, so the move was a rename and not a rewrite.)

When the engine needs something that only exists in the outer layers, the fix is to invert the dependency, never to add a
reference:

- Something the engine needs from a PDF/CSS/HTML type becomes an **engine-owned type**, and the outer layer converts at its
  boundary. Examples that exist: `FaceStyle` and `SyntheticStyle` (the engine's view of `XFontStyle`/`XStyleSimulations`),
  `EmojiMode` (of the CSS `font-variant-emoji` enum, mapped by `EmojiModeMapping`), `RuneInterval` (of the public
  `RuneRange`, converted in `PdfGenerator.AddFontFromStream`), `FontLock`, `FontMessages`.
- Something that only *exists* because of the outer layer belongs in the outer layer. Examples: the 256-entry WinAnsi
  `/Widths` table (`PdfSimpleFontWidths`), the default PDF font encoding (`GlobalFontSettings`), `XFont` (a `Typeface` plus an
  em size, PDF options and a skew), and `CssUnicodeBidiMapping` (the CSS half of the bidi integration; the engine only exposes
  the explicit-push types it maps onto).

The engine grants `InternalsVisibleTo` to one assembly only, its own test project `PeachDrawing.Text.Tests` (see the comment in
`PeachDrawing.Text.csproj`). PeachPDF and `PeachPDF.Tests` read the public API only, the compiler enforces it, and
`PublicSurfaceTests` fails if an entry for any other assembly comes back. A test that needs an engine internal goes to the
engine's test project; one that needs PeachPDF as well is written against the public API, and if the API lacks what it needs
the API gains a small documented member (and a row in the register of
`text-public-api-must-not-mirror-a-competitor.md`) instead of the bridge coming back.

The measured cost of not holding this line before the move: the first pass over the engine found ~15 files with PDF-writer
dependencies, including a two-way one (`XGlyphTypeface` called back into `FontResolver`, which held a dictionary of
`XGlyphTypeface`).
