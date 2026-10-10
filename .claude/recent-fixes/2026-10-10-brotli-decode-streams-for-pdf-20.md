# /BrotliDecode streams for PDF 2.0 (and the Brotli encoder seam)

Under `PdfVersion.Pdf20`, `PdfGenerateConfig.BrotliCompression` (opt-in, default **false**) makes every general-purpose stream use
`/BrotliDecode` instead of `/FlateDecode`. The load-bearing idea is one chokepoint: `StreamCompression.Encode(options, data, out filterName)`
(`PdfSharpCore/Pdf.Filters/`) replaced every direct `Filtering.FlateDecode.Encode` + hard-coded `"/FlateDecode"` pair (content, forms,
fonts, ToUnicode, embedded files, `PdfStream.Zip`, raster image/mask/SMask data). A new call site must use it or it silently stays Flate.

- `PdfDocumentOptions.UseBrotli` = request && `PdfVersion == Pdf20`; the force-off lives there, not in the config, so the multi-`AddPdfPages` path
  is covered. Encoder unavailable (`BrotliCompression.TryCompress` false) falls back to Flate per stream, never throws.
- Deliberately still Flate: PNG-predictor passthrough (`/DecodeParms /Predictor` is Flate/LZW-only), GIF LZW passthrough, the JPEG
  Flate-array experiment, XMP / ICC / object and xref streams.
- The encoder seam is `PeachDrawing.Text.Compression.BrotliCompression` (mirrors `BrotliDecompression`, stream-based, quality 0-11, last registration wins,
  process-wide: tests that register one must run in a non-parallel collection - `BrotliCompressorCollection`/`BrotliDecompressionCollection`).
- Side fix: `HyphenationEngine.LoadPatternSet(tag)` ignored the registered decoder, so `hyphens: auto` could not work in WASM.
- Evidence: PeachPDF.Tests net8.0 16310 pass; stream extracted from a PDF 2.0 render decodes with `BrotliStream`.
- Not yet done: managed (WASM) encoder in `PeachDrawing.Text.Brotli`, JPEG XL passthrough, default flip to PDF 2.0, PDF/A-4 and PDF/X-6.

## Checked against open-source implementations
- Filter name `/BrotliDecode` (inline-image abbreviation `/Br`, unused here): MuPDF (`pdf-stream.c`, writes it in `pdf-write.c`), pdf.js (`parser.js`
  `case "BrotliDecode"`), pypdf (`BROTLI_DECODE`), iText (`BrotliFilter`) all key on that name alone.
- **No reader or writer checked gates it on a catalog `/Extensions` entry or a version bump** (MuPDF's writer adds neither). We write neither; the
  EXTN-BROTLI-1 text itself was unreadable (pdfa.org returns 403), so whether it *recommends* declaring the extension is unverified.
- `/DecodeParms`: MuPDF and a (closed, unmerged) pdf.js patch treat Predictor/Colors/Columns/BitsPerComponent exactly like Flate; pypdf documents none. We
  write none, which every one of them reads. PNG-predictor passthrough stays Flate because its IDAT bytes are already Flate.
- Quality: MuPDF uses 1 / 6 / 11 for speed / default / best. We had 4 / 9 / 11. Measured on 13-22 MB fonts (python-brotli, same libbrotli): q9 is 7-10x slower
  than q6 for 1-4% smaller output, q11 ~100x slower; adopted 1 / 6 / 11.
- pdf.js notes the browser `DecompressionStream` rejects bytes after the end of the Brotli data (it falls back to its own decoder), so `/Length` must stay exact.
- Reader support: pdf.js (5.7+), MuPDF (1.26+, experimental); Acrobat does not read it yet.

## Why it is opt-in
Measured with pypdfium2 (PDFium 152) and PyMuPDF 1.28 on the same document: PDF 2.0 + Flate renders in both; PDF 2.0 + **Brotli renders a blank page in
PDFium** (one grey level) and normally in MuPDF. PDFium is Chrome's and Edge's viewer engine, and the docs site's showcase thumbnails use it, so a
default-on Brotli would have made default output blank in them. `BrotliCompression` therefore defaults to false (CLI: `--brotli`); revisit when PDFium and
Acrobat read the filter.
