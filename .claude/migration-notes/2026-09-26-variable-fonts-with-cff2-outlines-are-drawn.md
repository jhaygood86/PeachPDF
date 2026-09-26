# Variable fonts with CFF2 outlines are drawn at their location

**Before:** a variable OpenType font whose outlines are CFF2 (a `CFF2` table, as in many variable fonts made from PostScript-style
designs) had no outlines to the engine. Text in it was measured at the location (advances follow `HVAR`), but drawn from the whole font
file embedded as it was, which a PDF viewer draws at the font's default design, so `font-weight`, `font-stretch` and
`font-variation-settings` changed the spacing and not the shapes. Its glyphs were also missing from anything that needs an outline
(`background-clip: text`, text as SVG paths, the raster backend).

**Now:** the glyphs are drawn at the location. The PDF embeds each distinct location as a static OpenType font with CFF outlines that
holds only the glyphs the document uses (the same one-font-per-location behaviour variable fonts with TrueType outlines already had),
and the outline-based features work. Text in such a font at `font-weight: 700` now looks bold, and the shapes no longer disagree with
the widths. A CFF2 font used only at its default location is also embedded as a smaller static font of its used glyphs, instead of as
the whole variable font.

Static CFF fonts are unaffected: their font files are still embedded whole, byte for byte.
