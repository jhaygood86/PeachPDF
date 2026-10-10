# Experimental /JXLDecode pass-through for JPEG XL images

`PdfGenerateConfig.JxlPassthrough` (default **false**, PDF 2.0 only, `PdfDocumentOptions.UseJxlPassthrough`) embeds an eligible JPEG XL image as its
original bytes with `/Filter /JXLDecode`, no `/DecodeParms`, `/DeviceGray|RGB` (or ICCBased) and `/BitsPerComponent` 8 or 16.

- **No standard exists.** The PDF Association has only said JPEG XL is its preferred new format. The sole open-source design (PDFium's unmerged experiment)
  uses `/JXLDecode` as a placeholder name and leaves DecodeParms, ICC-vs-ColorSpace and alpha open (alpha would become a separate `/SMask`). We copy that
  shape; it is why the option is off by default and documented as unreadable by shipping readers. Revisit when the PDF Association publishes the filter.
- **Eligibility** (`PeachImageSource.WrapJxlPassthrough`): no alpha (PeachImage has no JXL encoder to split an SMask out), not animated, orientation Normal
  (a PDF image cannot rotate itself), Gray8/Gray16/Rgb24/Rgb48. CMYK, float and everything else keep the raster path.
- **Decorator, not a new decode path.** `PeachJxlPassthroughImageSourceImpl` wraps the exact source `DecodeJxl` returned before, so a render with the option
  off is unchanged. The decision lives in `PdfImage.InitializeJpeg` (it outranks the JPEG pass-through a recompressed-JPEG JXL also carries) and
  `PdfImageTable.IsJxlPinnedToNaturalSize` mirrors its condition so the image is never resized; `ImageCompression.Lossy` opts out of both.
- Evidence: `JxlPassthroughTests` (verbatim bytes in the PDF for lossy/lossless/ICC/gray/16-bit, off by default, forced off under Pdf17 and Lossy, alpha and
  animated stay raster); existing JXL tests unchanged. Not verified in any reader: none decodes it yet.
