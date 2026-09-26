# Text hinting: CFF outlines are not hinted

`GridFitting.Standard`/`Monochrome` (and so `PdfGenerateConfig.TextHinting`) run the TrueType bytecode of a font. A font with CFF outlines has
none, and `Type2CharstringInterpreter` skips its stem hints and Private DICT alignment zones, so such a font gets the scaled design outline
(`GlyphOutline.IsGridFitted` false). The reference for applying them is Adobe's CFF engine as FreeType ships it (`cf2*`). Tracked in
[#1431](https://github.com/jhaygood86/PeachPDF/issues/1431).
