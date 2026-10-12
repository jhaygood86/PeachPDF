# `PdfImage.ReadBgraBitmap` reads the BMP an `IImageSource` writes, and validates only that shape

`ReadBgraBitmap` (`src/PeachPDF/PDF/Advanced/PdfImage.cs`) re-derives a PDF image XObject's raw RGB
samples, 1-bit `/Mask` and `/SMask` from the BMP bytes an `IImageSource.SaveAsPdfBitmap` produced. It
accepts exactly one shape and throws `NotImplementedException` (aborting the render of the whole
document, not just that image) for anything else:

- file signature `BM`, file size equal to the stream length, width/height equal to the effective pixel
  size the PDF image will declare;
- an info header of 40 bytes (`BITMAPINFOHEADER`, must be uncompressed) or 108 bytes (`BITMAPV4HEADER`,
  `BI_BITFIELDS`: the low word of the compression field is not checked, its high word must be 0);
- one plane and **32 bits per pixel**, read at byte offset 28 for both header versions;
- pixels stored bottom-up as BGRA, four bytes per pixel (so every row is already 4-byte aligned).

`PeachImageSource.SaveAsPdfBitmap` (PeachImage's `BmpImageEncoder.EncodeRgba32`) writes the 108-byte
variant. A change to that encoder (another header version, another bit count, a compression code of
65536 or more) has to be matched here, and `PdfCanvasImage`/`ImageSource` implementations must keep
writing the same shape.

**The trap this note replaces:** the previous guard was
`A || B || bigHeader ? C : D`, which C# parses as `(A || B || bigHeader) ? C : D`, so for a 108-byte
header the bit-count and plane checks never ran; it also read the bit count from offset 30 (the
compression field) for that header. It only worked because byte 32 happens to be the high half of
`biCompression`. The reader now checks planes and bit count unconditionally. If you touch these
conditions, keep the parentheses explicit and test a header that is wrong in each field.
