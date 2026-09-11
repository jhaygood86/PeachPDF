# Nested composite glyphs no longer lose components in font subsets

`GlyphDataTable.CompleteGlyphClosure` decides which glyphs a PDF font subset must embed. It snapshotted
the originally-requested glyph set and made one flat pass adding each glyph's direct composite
components. A component that is itself composite never got its own components added, so a two-level
nested composite silently dropped its innermost glyph from the embedded subset.

Real fonts hit this for precomposed accented letters shaped as base + mark, where the mark is itself a
composite wrapping the actual diacritic outline (issue #1009's reporter hit it with `Go Noto CJKCore`:
`aacute` → `[a, glyph00814]`, and `glyph00814` → `[acute]`). The wrapper glyph index made it into the
subset, so nothing threw - it just drew nothing, leaving the base letter rendered and the diacritic mark
silently missing, while cmap/ToUnicode-driven copy-paste stayed correct. `SourceSans3-Regular.ttf`
(`BundledFonts.Ttf`) reproduces the same two-level shape for real for `U+1EA0` ("Ạ"): `uni1EA0` → `[A,
uni0323]`, `uni0323` → `[uni0307]`.

Fix: `CompleteGlyphClosure` now drains a worklist queue instead of one fixed-size pass - each
newly-discovered component glyph is enqueued so its own components get resolved in turn, closing the set
transitively regardless of nesting depth. Still reads each glyph only through `GetGlyphData`'s
`ReadOnlySpan<byte>` view, never through the shared `OpenTypeFontface.Position` cursor - see
[fonts-composite-closure-does-not-move-the-shared-face-cursor](../invariants/fonts-composite-closure-does-not-move-the-shared-face-cursor.md),
which this change does not touch.

`GlyphDataTableTests.CompleteGlyphClosure_IncludesNestedCompositeComponents` reproduces the failure
directly: it takes a real composite glyph from `BundledFonts.Ttf` and rewrites its accent component's own
glyph record in place to make it composite too (the same shape `Go Noto CJKCore` uses), then asserts the
resulting closure contains the doubly-nested component. `dotnet build PeachPDF.slnx -t:Rebuild`: 0
warnings. Full `PeachPDF.Tests` suite passed on net8.0 and net10.0, with all changed executable lines
covered by diff coverage.
