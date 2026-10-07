# `CBDT` formats 1, 2, 5, 6, 7, 8, 9 and `sbix` `tiff`/`mask` pictures are not drawn

PeachPDF draws bitmap color glyphs from `CBDT`/`CBLC` image formats 17, 18 and 19 (PNG) and `sbix` `png `, `jpg ` and
`dupe`. The EBDT-style uncompressed formats (1, 2, 5, 6, 7), the composite formats (8, 9) and `sbix` `tiff`/`mask` are
left undrawn: a glyph in such a font renders blank or as its plain outline.

**Why not done:** each needs its own decoder plus conversion to an RGBA raster before embedding, and no freely licensed
font that uses any of them could be found to test against. The open test fonts available (Simon Cozens' test-fonts,
bundled as `assets/fonts/SimonCozens*.otf`) use only CBDT format 17 and sbix `png `, and a decoder verified only against
synthetic fixtures was judged not worth the risk. Tracked in
[#1657](https://github.com/jhaygood86/PeachPDF/issues/1657); close it, and delete this file and the matching sentence in
`docs/html-css-support.md` (Per-character font matching), once a licensed font exercising one of these formats exists.
