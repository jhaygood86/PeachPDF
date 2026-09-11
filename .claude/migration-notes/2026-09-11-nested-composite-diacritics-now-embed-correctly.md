# Diacritics on nested-composite glyphs now embed correctly

Previously (through the released v0.9.18): a font whose accented glyphs were authored as nested
composites - a precomposed letter referencing a base letter plus a mark glyph, where that mark glyph is
itself composite (common in large fonts that merge many scripts, e.g. Go Noto CJKCore) - would embed the
mark's wrapper glyph but not the wrapper's own component, the mark's actual outline. The generated PDF
rendered the base letter with no diacritic, even though the underlying text (and copy-paste via
ToUnicode) was correct.

Now: font subsetting resolves composite-glyph components transitively, at any nesting depth, so the
diacritic mark's real outline is always included in the embedded subset and renders as authored. No
document author action is needed; this only affects the specific class of fonts described above -
fonts whose accented glyphs are simple glyphs, or single-level composites, were unaffected either way.
