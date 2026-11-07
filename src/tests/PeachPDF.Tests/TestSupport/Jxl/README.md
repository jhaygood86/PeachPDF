# JPEG XL test fixtures

Small `.jxl` files copied from the test assets of [PeachImage](https://github.com/jhaygood86/PeachImage)
(BSD-3-Clause; the JPEG XL conformance files originate from the libjxl project, also BSD-3-Clause).
They are committed rather than generated because several are specific encodings (JPEG recompression, splines, blend modes, upsampling) that PeachImage's own encoder does not produce.

- `recompressed_*` — made by `cjxl` from a JPEG; they carry JPEG reconstruction data (`jbrd` box).
- `conformance_*`, `rgb*`, `gray`, `rgba`, `icc_lossless` — ordinary JPEG XL images (lossless/lossy, alpha, 16-bit, ICC, animation).
