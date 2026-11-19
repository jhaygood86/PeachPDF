# Type0 fonts that are not embedded and carry no ToUnicode map

Twelve pdf.js files (ThuluthFeatures, issue11242_reduced, issue12418_reduced, issue15441, issue4722, issue5801...) use a CIDFontType2 font that is not
embedded, with an Identity-H encoding and no `/ToUnicode`. The codes are glyph indices of the original font, so without the font file there is no way to
know which character a code stands for: `Paint` reports `font: Type0 font that is not embedded and has no ToUnicode map` and draws nothing. PDFium draws
text anyway, by looking the named font up on the machine and using the codes as glyph indices of whatever it finds, which is right only when the installed
font is the very font the document was made with and garbled otherwise (the reference renderings here come from a machine that has them). A reader that
shows wrong letters is worse than one that says it cannot, so this is left as it is. A document that wants to be read without its fonts embeds them or
gives a ToUnicode map, and then this importer draws the text from a substitute.
