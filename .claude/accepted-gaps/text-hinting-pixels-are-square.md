# Text hinting: pixels are square

A size is one number (`OutlineRequest.PixelsPerEm`), so FreeType's stretched-ratio (non-square pixel) paths in the interpreter (`Current_Ratio`,
`Read_CVT_Stretched`, the non-square branches of `MD`/`MDRP`/`IP`) are not ported, and a hinted outline is always fitted for square pixels. A caller
with a device whose pixels are not square has to fit at one size and scale the result; PeachPDF's raster backend only hints text that reaches the
bitmap with the same scale in both directions. Two-axis sizes would need a size model with an x and a y ppem in `TtSize`/`CffSize`, the ported
stretched branches, and goldens made with `FT_Set_Char_Size` at different horizontal and vertical resolutions. Only worth doing when a real consumer
needs it. Tracked in [#1433](https://github.com/jhaygood86/PeachPDF/issues/1433).

What is *not* a gap: `LTSH` and `VDMX` are not read, and cannot make a difference to a comparison with FreeType, which has no code for either table
(2.14.3). `hdmx` is used, as FreeType does, and `gasp` decides which sizes are fitted (see `TtGasp`).
