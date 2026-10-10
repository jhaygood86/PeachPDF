# /BrotliDecode streams for PDF 2.0 (and the Brotli encoder seam)

Under `PdfVersion.Pdf20`, `PdfGenerateConfig.BrotliCompression` (default true) makes every general-purpose stream use
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
