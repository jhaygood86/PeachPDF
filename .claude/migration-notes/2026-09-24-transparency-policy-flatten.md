# Documents targeting PDF/A-1 or PDF/X-1a/X-3 can flatten transparency instead of failing

**Before:** a document targeting PDF/A-1 (`PdfA1B`/`PdfA1A`) or PDF/X-1a/X-3 that used anything needing transparency (`opacity`, `rgba()`,
an alpha gradient stop, an alpha PNG, `mix-blend-mode`, a blurred shadow, a `filter`, an SVG mask) failed generation with an
`InvalidOperationException` naming the feature. There was no way to keep the document and the conformance level.

**Now:** that is still the default (`PdfGenerateConfig.TransparencyPolicy = Reject`), so nothing changes for existing callers. Setting it to
`Flatten` (CLI: `--flatten-transparency`) renders the affected elements, with what is behind them, into opaque bitmaps at
`RasterizationDpi` and embeds those instead; the file has no transparency and stays conformant. See
[Flattening transparency](../../docs/usage-examples.md#flattening-transparency-for-pdfa-1-and-pdfx).

Also changed, for everyone: a `backdrop-filter` (and any other raster region whose bitmap is fully opaque) is now legal under PDF/A-1 and
PDF/X-1a/X-3 with no policy set, because an opaque bitmap needs no soft mask. The error message of the rejection now mentions the option.
