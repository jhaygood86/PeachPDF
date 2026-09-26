# Text hinting: CFF2 (variable CFF) outlines are not grid-fitted

The CFF engine (`Internal/Hinting/FreeType/Ps*.cs`, Adobe's engine as FreeType ships it) is ported for CFF fonts only: the branches of its interpreter
for CFF2 (`isCFF2`: no width operand, implicit `endchar`, `vsindex` and `blend`) and for Type 1 were left out, and `CffFace.TryCreate` accepts a `CFF `
table of version 1 only. PeachDrawing.Text has no CFF2 outline reader yet either, so a font with CFF2 outlines gets the scaled design outline
(`GlyphOutline.IsGridFitted` false). Tracked in [#1441](https://github.com/jhaygood86/PeachPDF/issues/1441).
