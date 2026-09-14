# Acid2's checkerboard interlock can stipple at low raster resolutions

Acid2's eyes use two 2×2 diagonal-transparency image grids offset by one pixel so their transparent
and opaque pixels interlock into a solid band. PeachPDF emits both grids with the correct one-pixel phase. PDF rasterizers independently filter and
coverage-resolve the tiny transparent image XObjects, so the shared band varies by rasterizer and
output resolution: MuPDF renders it largely solid while PDFium can retain a visible stipple.

An isolated fixture reproduces the resolution-dependent result without Acid2's layout. Raw content
stream inspection also confirms the one-pixel phase, so this is not a background-repeat positioning
or paint-order error.

Native PDF tiling patterns were prototyped as an alternative. They made the result worse in both
PDFium and MuPDF, producing visible stripes or checks at 96 and 192 DPI, so that approach was
discarded. The ordinary repeated-image path remains the more faithful output.
