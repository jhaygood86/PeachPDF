# PdfOperation reports a signed source

**Before:** loading, merging or overlaying a digitally signed PDF through `PdfOperation` wrote a new file with no
signature, and nothing said so (`Warnings` was empty).

**Now:** `PdfOperation.Warnings` contains `digital signature not carried: ...` when any source is signed or carries a
document timestamp, in both `PdfCopyMode.Redraw` and `ObjectCopy`. `PdfPageSource.HasSignatures` reports it up front.
The output is still unsigned; a signature covers the exact bytes of its file, so it cannot be carried into a new one.
