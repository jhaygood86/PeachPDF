# The font engine no longer reaches into the PDF writer for infrastructure

Groundwork for extracting `Fonts/` and `Text/` into their own library (the `PeachDrawing.Text` plan): the engine
must not reference `PeachPDF.PdfSharpCore` or `PeachPDF.CSS`, so every edge that was only *infrastructure* was cut
in place, with no behaviour change. Byte-identical PDFs and the full suite were the acceptance bar.

**What moved, and where it lives now**
- `XFontSource` -> `Fonts/FontFileData.cs` (`FontFileData`). It is the bytes of one font file plus its memoised
  checksum and process-wide cache key, and it owns `CalcChecksum` (the Adler32-plus-length that used to sit on
  `PdfSharpCore.FontHelper`). Named `FontFileData`, not `FontData`, because `OpenTypeFontTable.FontData` is already a
  property (of type `OpenTypeFontface`) and a type of that name would shadow it inside every table class.
- `Lock.EnterFontFactory`/`ExitFontFactory` -> `Fonts/FontLock`. **One monitor still serialises every font cache**,
  including the PDF layer's `FontFamilyCache`/`FontFamilyInternal`: `PdfSharpCore.Internal.Lock` now forwards to
  `FontLock`, so the two layers keep excluding each other. Do not give either side its own lock object.
- `PSSR.ErrorReadingFontData` -> `Fonts/FontMessages`.
- `OpenTypeDescriptor.Widths` (the 256-entry WinAnsi `/Widths` table) is PDF-font-dictionary data, not font data: it
  moved to `PdfSharpCore/Pdf.Advanced/PdfSimpleFontWidths`, computed only when a simple TrueType font is actually
  embedded. It used to be computed for **every** descriptor at construction (256 cmap lookups each), whether or not
  a simple font was ever written, which is also why `OpenTypeDescriptor` needed `PdfEncoders`.
- `GlobalFontSettings` (the default `PdfFontEncoding`) -> `PdfSharpCore/Drawing`: it is PDF encoding policy read only
  by `XFont`'s constructors.
- `FontVariantEmojiMode` (a CSS enum) inside `Text/` -> `Text/EmojiMode`, mapped at the boundary by
  `Html/Core/Utils/EmojiModeMapping.ToEmojiMode`. `TextShapingFeatures.EmojiMode` is the engine's own type now.
- `RuneRange` inside `Fonts/`/`Text/` -> `Fonts/RuneInterval` (same shape, internal). The public `PeachPDF.RuneRange`
  stays on `PdfGenerator.AddFontFromStream` and is converted once at that boundary.
- Internal `FontCollection` (the `ttcf` reader from #1367) -> `SfntCollection`, so it cannot be confused with a
  future public font collection type.
- Deleted as dead: `PlatformFontResolver`/`PlatformFontResolverInfo` (a stub that could only return null) and
  `AdobeGlyphList20` (compiled out behind `#if DEBUG_`).

**Deliberately not done here.** `XFontStyle`, `XStyleSimulations`, `XFont`, `XGlyphTypeface`, `XPdfFontOptions` and
`FontDescriptorCache`/`FontDescriptor.ComputeKey(XFont)` still tie the resolver and descriptor layers to PdfSharpCore.
They are not infrastructure: that whole layer is replaced by the extraction PR (a size-free typeface plus a query
type), so re-homing it now would mean renaming it twice.

**Traps found by running it**
- The repo has `core.autocrlf=true` and mixed line endings on disk (`FontHelper.cs` is CRLF, others LF). A scripted
  edit that matches multi-line text with `\n` silently finds nothing in a CRLF file: normalise on read.
- A test/adapter that spelled `PeachPDF.RuneRange` fully qualified is not fixed by adding a `using`; it needed the
  qualified name changed too.
