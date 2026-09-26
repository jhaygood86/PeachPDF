# SVG-in-OpenType glyphs are drawn

**Before:** a colour font that stores its glyphs as SVG documents (the `SVG ` table) drew the glyphs' plain outlines, which for such a
font is often blank or a flat monochrome shape.

**Now:** each glyph's SVG document is rendered as vector content, in the font's palette colours (`font-palette` applies) with the text
colour for `context-fill`/`currentColor`, and shared as one form between occurrences. A font that also has bitmaps or `COLR` paints keeps
using those first. Text stays selectable. Inside a rasterized element (filters, shadows, PDF/A flattening) such a glyph is still its
outline. A document that cannot be used (unparseable, or larger than 4 MiB inflated) draws the outline as before.
