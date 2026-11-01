# JPEG XL test fixtures

Small `.jxl` files copied from the test assets of [PeachImage](https://github.com/jhaygood86/PeachImage) v0.5.0
(BSD-3-Clause; the JPEG XL conformance files originate from the libjxl project, also BSD-3-Clause).
PeachImage can decode JPEG XL but has no encoder, so these are committed rather than generated.

- `recompressed_*` — made by `cjxl` from a JPEG; they carry JPEG reconstruction data (`jbrd` box).
- `conformance_*`, `rgb*`, `gray`, `rgba`, `icc_lossless` — ordinary JPEG XL images (lossless/lossy, alpha, 16-bit, ICC, animation).
