# Refusing a signed source in a PdfOperation (PdfSignedSourcePolicy)

`PdfOperation.WithSignedSourcePolicy(PdfSignedSourcePolicy.Reject)` makes `Save`/`ToArray`/`AppendTo` throw `InvalidOperationException`
when a file the output draws or copies pages from is signed (`PdfPageSource.HasSignatures`), where the default `Warn` writes the output
without the signature and names the loss in `Warnings` (see the [phase 0 migration note](../migration-notes/2026-11-22-pdfoperation-warns-about-signed-sources.md)).

**Load-bearing ideas:** the check is in `PdfOperationWriter.Append`, before any page is drawn or copied, so a refused operation writes
nothing and leaves a composer it was appending to with no pages (the tests assert both). Only the sources of the output's own pages are
checked: an overlay or underlay is drawn over a page on purpose, and its signature was never the output's to keep. The policy is set after
loading, like `WithCopyMode`, so the files are already open when `Append` decides; it is read at write time, not at `MergeBytes`.

**Not done:** a per-source policy (reject one merged file but not another); the message names `PdfFileSigner` as the way to sign the output
but cannot say which source was signed.
