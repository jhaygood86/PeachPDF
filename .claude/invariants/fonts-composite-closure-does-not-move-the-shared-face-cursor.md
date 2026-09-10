# Composite glyph closure does not move the shared font-face cursor

`OpenTypeFontface` instances are cached process-wide, and `Position` is one mutable cursor shared by all
table readers. `GlyphDataTable.CompleteGlyphClosure` runs during PDF font subsetting while another render
or xUnit class can shape text against the same face.

Composite closure must parse the `ReadOnlySpan<byte>` returned by `GetGlyphData`; it must not seek or read
through `OpenTypeFontface.Position`. Locking the glyph-table method does not coordinate with GSUB's
font-face lock. The measured failure was simultaneous `IndexOutOfRangeException` crashes in full-font
subsetting and SVG small-caps GSUB lookup on Ubuntu CI.
