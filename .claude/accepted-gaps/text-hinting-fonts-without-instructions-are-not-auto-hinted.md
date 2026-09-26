# Text hinting: fonts without instructions are not auto-hinted

A TrueType font with no instructions, and (until the CFF gap is closed) every CFF font, is answered with the scaled design outline. FreeType's
auto-hinter (`src/autofit`), which hints from the outline itself, was deliberately not ported: it is a much larger body of code than the
bytecode interpreter, and the licence work of PR 1 covers only the files it lists. Tracked in
[#1434](https://github.com/jhaygood86/PeachPDF/issues/1434).
