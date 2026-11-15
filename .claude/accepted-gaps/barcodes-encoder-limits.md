# Barcodes: encoder limits inherited from the FreePDF port

- **QR code** encodes the whole text as ONE segment: numeric if all digits, alphanumeric if every character is in the 45-character set, otherwise byte (UTF-8, with ECI 26 unless `BarcodeOptions.Utf8Eci` is off). No mixed-mode segmentation (so mixed text makes a larger symbol than an optimal encoder), no kanji mode, no FNC1/GS1 QR, structured append, Micro QR or rMQR.
- **Data Matrix** encodes ASCII (digit pairs packed, Upper Shift for UTF-8 after ECI 26) and Base 256 only. C40, Text, X12 and EDIFACT are not implemented, so text they would pack tighter gives a larger symbol; the public `DataMatrixEncoding` enum deliberately lists only the two real choices. GS1 Data Matrix is a leading `Code128.Fnc1` in the text; there is no AI parsing.
- **GS1-128**: `Gs1Elements.ToCode128Text` places FNC1 first and after variable-length elements by the predefined-length table of the GS1 General Specifications (00, 01, 02, 11-17, 20, 31xx-36xx, 41x); it does not validate an element's data against its AI's format or check digits.
- **Linear codes** have no EAN-5/EAN-2 add-ons, UPC-E, Code 39, ITF or Codabar. Human-readable text under EAN/UPC uses the font family you give (a monospace stack in SVG); it is not OCR-B-metric exact.

Revisit with new information, e.g. a real need for a denser QR for mixed content (the optimal-segmentation algorithm is well known) or for C40 Data Matrix.
