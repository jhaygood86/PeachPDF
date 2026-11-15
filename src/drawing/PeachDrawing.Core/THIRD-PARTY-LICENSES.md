# Third-Party Licenses (PeachDrawing.Core)

PeachDrawing.Core is BSD 3-Clause-licensed (see [LICENSE](../../LICENSE)). The drawing abstraction itself is original PeachPDF code. The one part that carries third-party code is the barcode encoders in the `Barcodes` folder.

## FreePDF barcode encoders

The encoders for QR codes (`QrEncoder.cs`), Data Matrix (`DataMatrixEncoder.cs`), Code 128 and GS1-128 (`Code128.cs`) and EAN-13, EAN-8 and UPC-A (`EanUpcSymbology.cs`), and the shape of the public barcode API built on them (`Barcode`, `BarcodeOptions`, the quiet-zone defaults and the human-readable digit layout), are ported to this package from [FreePDF](https://github.com/io7/FreePDF) (`PdfSharp.BarCodes`), an MIT-licensed continuation of PDFsharp. Each ported file carries a header naming its origin. FreePDF states that these encoders were written from ISO/IEC 18004:2015 (QR code), ISO/IEC 16022:2006 (Data Matrix), ISO/IEC 15417 (Code 128), ISO/IEC 15420 (EAN and UPC) and the GS1 General Specifications, and that no source from another barcode library was read or reused. Its Data Matrix encoder replaced an earlier GPL-derived file; the replacement was written from the standard and does not follow the structure of the file it replaced.

```
Copyright (c) 2001-2026 empira Software GmbH, Troisdorf (Cologne Area), Germany

http://docs.pdfsharp.net

MIT License

Permission is hereby granted, free of charge, to any person obtaining a
copy of this software and associated documentation files (the "Software"),
to deal in the Software without restriction, including without limitation
the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the
Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included
in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
DEALINGS IN THE SOFTWARE.
```

The package depends on [PeachDrawing.Text](https://www.nuget.org/packages/PeachDrawing.Text), which carries its own notices.
