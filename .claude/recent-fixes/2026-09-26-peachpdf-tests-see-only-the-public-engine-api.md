# PeachPDF.Tests sees only the public engine API

The last `InternalsVisibleTo` bridge is gone: `PeachDrawing.Text` now grants its internals to `PeachDrawing.Text.Tests` alone,
so `PeachPDF`, `PeachPDF.Cli` and `PeachPDF.Tests` all compile against the public surface and nothing else.
`PublicSurfaceTests.TheEngineGrantsItsInternalsToNoAssemblyButTheTests` now demands exactly that one entry.

## What was left to do

Earlier slices had already moved the parser, table and shaping tests into `PeachDrawing.Text.Tests` and rewritten the
hyphenation, generic-family, WOFF2 and vertical-table ones. Removing the line and building `PeachPDF.Tests` listed what
remained, which was small:

- `BundledFonts.AnySupportedFontPath` and `GetOrRegisterKnownFamily` took the engine's `FontResolver`/`TtfFontDescription`.
  Only engine tests called them, so they moved to a `partial` in `PeachDrawing.Text.Tests/TestSupport/BundledFonts.Internals.cs`;
  the shared `BundledFonts.cs` linked into both projects no longer names an engine internal.
- `FontVariantCaps`/`Numeric`/`Position` integration tests read `Typeface.Face.Descriptor` to shape a string; they call
  `Shaper.Shape(typeface, text, box.ActualTextShapingFeatures)` and `Typeface.TryMapRune` instead. The assertions (which glyph
  each feature lands on) are the same.
- `PdfTypefaceMetricsTests` compared the PDF-side conversions with the engine descriptor's original `DesignUnitsToPdf`,
  `GlyphIndexToPdfWidth` and `GetBaseName`. It now checks them against the definition written out over the public
  `Metrics.UnitsPerEm`, `GetAdvance`, `FullName`, `IsBold` and `IsItalic`.
- `MathGlyphAssemblyShaperTests` built `MathGlyphAssembly` with internal init-only setters. The type gained a public constructor
  `MathGlyphAssembly(double italicsCorrection, IReadOnlyList<MathGlyphPart> parts)` (the properties are now get-only, and the
  table reader uses the constructor too). It is the one addition to the public API; the register row and `PublicApi.txt` carry it.

## Trap

A test that reaches for an internal is not a reason to widen the bridge. Either the test is about the engine alone (move it to
`PeachDrawing.Text.Tests`, which keeps its own `InternalsVisibleTo`) or the public API is missing a small documented member
(add it and its register row). Re-adding `PeachPDF.Tests` to the engine's csproj is what `PublicSurfaceTests` is there to catch.

## Evidence

`PeachPDF.Tests` and `PeachDrawing.Text.Tests` pass unchanged in count apart from the tests split or added above; the solution
rebuilds with zero warnings.
