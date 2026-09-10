# Font subsetting no longer races shaping

`GlyphDataTable.CompleteGlyphClosure` used the process-wide cached `OpenTypeFontface.Position` cursor to
read composite-glyph components. Its method-level synchronization locked the glyph table, not the font
face, so it did not coordinate with GSUB readers that correctly lock the shared face.

The full-font allocation test repeatedly subset Source Sans while an SVG small-caps test read that same
cached face's GSUB table. On Ubuntu CI, their cursor writes interleaved: both tests failed with
`IndexOutOfRangeException`, one in `CreateFontSubSet` and one in `GetActiveLookupIndices`. Adding bounds
checks would only have changed the symptom.

Composite closure now parses each glyph's existing `ReadOnlySpan<byte>` directly with big-endian span
readers. It neither moves nor locks the shared cursor and removes the old synchronized-method overhead.
A deterministic regression test verifies that closure leaves `OpenTypeFontface.Position` unchanged.

The focused COLR/CPAL and SVG font-variant classes passed together (24 tests), followed by all 10,551
runnable net8.0 tests. The complete branch retained 100% diff coverage, the solution rebuilt with zero
warnings, and the independent diff review found no actionable issues.
