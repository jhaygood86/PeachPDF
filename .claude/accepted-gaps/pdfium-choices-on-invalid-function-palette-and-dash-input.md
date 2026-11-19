# Where PDFium differs from the specification on invalid input

Three PDFium corpus tests stay failing because the file is invalid and PDFium's answer is its own, not the specification's:

- **`bug_42270333`:** type 4 functions whose `/Range` is [-1 1] for a color component (a shading's R, G and B). The specification takes the function's
  output as the color component (clamped to 0..1 by DeviceRGB), so a value of 0 is 0; PDFium treats the range as a scale onto 0..1 and gets 0.5 (orange
  where this importer paints red).
- **`image_transformer_other`:** a 1-bit image whose Indexed space has `hival` 0 but whose data holds index 1. This importer clamps to the last entry
  (the gray one); PDFium paints black.
- **`dashed_lines`:** dash arrays with a negative value (`[6 5 -4 3 2 1] 5 d`) and a negative phase. Invalid per the specification (8.4.3.6); this
  importer keeps a solid line for the first, and PDFium draws a pattern. A correct file never contains them.

Matching PDFium here would mean reproducing its error recovery; none of these is something a producer writes on purpose (they are fuzzing and
edge-case tests).
