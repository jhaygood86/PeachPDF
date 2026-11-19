# JPEG 2000 with an Indexed /ColorSpace, and the JPX corpus gaps left

`jpxdecode_indexed` (a 2-bit codestream, palette red/green/blue/white) drew gray: the codestream's component is a palette index (PDF
8.9.5.1), but the decode was read as gray levels. `FromJpx` now decodes again with PeachImage's `Jpeg2000DecoderOptions.IgnoreColorSpace`
(raw components, the file's own `pclr` palette box not applied) and looks each value up in the Indexed space.

**Trap: the depth.** The dictionary's `/BitsPerComponent` does not apply to JPX (the codestream carries its own), and the decoder scales
a 2-bit index to 8 bits (0, 85, 170, 255) while `jpxdecode_indexed2` holds raw 8-bit indices (0, 1, 2). The depth is therefore inferred:
the smallest of 1, 2, 4, 8 bits whose levels hold every decoded value, compared in absolute 8-bit units. A first version compared relative
to the step and called raw 0/1/2 a 1-bit image (value 1 is within 0.4% of "0" at a 255 step), which broke `jpxdecode_indexed2`; the corpus
caught it only because the earlier sibling test was already passing. Known ambiguity: a genuine 8-bit stream that uses only 0 and 255 reads
as 1-bit.

Corpus: 155 -> 156 of 207. The other JPX failures are accepted gaps, not defects (see the accepted-gaps note): PDFium's own DeviceCMYK
conversion (jpxdecode, jpxdecode_without_*), PDFium drawing nothing when a JPX's components contradict its /ColorSpace
(jpxdecode_with_mismatch_colorspace), and bug_557223 (a codestream that declares 720x243 inside a 32x32 image).
