# JPEG XL support (PeachImage 0.5.0)

**Load-bearing idea.** `PeachImageSource.DecodeJxl` first asks `JxlJpegReconstruction.TryReconstructJpeg`; if the file
was a recompressed JPEG it feeds the rebuilt JPEG bytes back into `Decode`, so the existing JPEG routing (CMYK/YCCK and
ICC-tagged RGB/gray pass-through as `/DCTDecode`, plain re-encode otherwise, Exif orientation) applies unchanged. There is
deliberately no second pass-through implementation. This check must come **before** the `Cmyk32` branch, since a CMYK JXL
would otherwise be rejected by `DecodeCmyk` (JPEG/TIFF only). A non-recompressed CMYK JXL is color-managed with
`ConvertToSrgb`.

**Found by running it.**
- PeachImage 0.5.0 reported `ImageInfo.IsLosslessEncoding == false` for every JXL and rotated JXL pixels itself (the
  sideways fixture decoded 243x201 but its reconstructed JPEG was 201x243). 0.5.1 fixed both: lossless (Modular,
  non-XYB) reports true, and no decoder rotates, so the JXL and JPEG routes agree on stored dims and orientation comes
  from `ImageInfo.Orientation` (see the image-orientation recent fix).
- Untagged RGB/gray recompressed JPEGs are *not* passed through (same as a plain `.jpg`); only the ICC-tagged sideways
  fixture is, which the tests assert byte for byte.
- `JxlFormatException` derives from `ImageFormatException`, so the existing catch in `Decode` already normalizes a
  truncated JXL to `InvalidOperationException` (image skipped, render continues).

**Evidence.** `PeachImageSourceJxlTests`, `JpegXlImageIntegrationTests`, the `jpeg_xl` showcase rasterized through PDFium
and MuPDF. PeachImage has no JXL encoder, so fixtures are committed under `PeachPDF.Tests/TestSupport/Jxl`.
