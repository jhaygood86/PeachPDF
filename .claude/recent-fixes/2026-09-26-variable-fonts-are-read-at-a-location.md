# Variable fonts are read at a location

`Typeface.WithAxes` returns the same font read at a location in its design space: TrueType outlines follow `gvar`, advances follow
`HVAR` (or the phantom points of `gvar` when there is no `HVAR`), and the font-wide metrics follow `MVAR`. What is not read is recorded
in the accepted gaps (layout tables, CFF2, bounding box and vertical metrics).

## What the load-bearing idea was

An instance is a `LoadedTypeface` that shares the font data and the parsed face with the default one and has **its own descriptor**,
which carries the location: `OpenTypeDescriptor.Variation` reaches `GlyphOutlineDecoder`, `GlyphIndexToWidth` and the metrics in
`Initialize`. So everything that already asks a descriptor (metrics, shaping advances, outlines, ink scanning) follows the location
without knowing about it. One location is one `Typeface` object (`WithVariation` memoizes on the normalized location's key), and a
location at every default is the default typeface itself, so a cache can key by identity as it does today.

The variation tables are parsed from the font's bytes with span readers (`BigEndian`), not through the face's shared cursor, so
reading them takes no lock. A malformed table makes the reader return "no variations" rather than throw.

## What running it showed

- **The reference has to be fontTools' own instancer**, not this reader's arithmetic: `assets/fonts/generate_variable_fixture.py`
  builds a font with varLib from six masters (one at an intermediate weight, giving `gvar` intermediate regions, and `avar` mapping)
  and writes what `instantiateVariableFont` makes of it at twelve locations. The engine matches it within one design unit for outlines,
  advances and metrics; the instancer rounds to integers and the engine does not.
- The fixture is built to exercise the parts that break: `C` has deltas for 5 of its 16 points, so the missing ones are interpolated
  (IUP); `Aacute` is a composite whose component offsets vary (the composite's `gvar` points are its components, plus four phantom
  points); a clamp beyond the axis range; and a copy without `HVAR` for the phantom-point advance path.
- `SOURCE_DATE_EPOCH` pins the timestamps fontTools writes, so running the generator again writes the same bytes.

## Embedding an instance

`TypefaceExporter.ExportSubset` on an instance goes through `OpenTypeFontface.CreateFontSubSet(..., variation)`, which writes each
glyph afresh (`InstanceGlyphEncoder`: the points or component offsets with the deltas applied, no instructions, compact flags) and
replaces `hmtx` with a `RawFontTable` whose advances follow `HVAR`/`gvar` and whose left side bearings are the new leftmost points. **The left
side bearing matters:** FreeType places a glyph by `lsb - xMin`, so keeping the source's `hmtx` would shift every glyph whose left edge
moved. `cvt`, `fpgm` and `prep` are left out because nothing they hint survives. A one-off comparison of the exported glyphs with the
instancer's at `wght=700, wdth=90` (points, contour ends, on-curve flags, advances) agreed to within 0.25 of a unit for every glyph; the
composite's left bearing differs by one unit because its header bounds come from the outline's control points.

## Review hardening

A read-only review of the slice found these; all are fixed and each has a test in `VariationTablesTests`.

- **A hostile font can make the parsers allocate gigabytes** with a few kilobytes of input: an `ItemVariationStore` whose 65535 data sets
  all name one large set, a `DeltaSetIndexMap` claiming 2^31 entries, an `fvar` with `instanceSize` 0. Every count is now checked
  against the bytes the table has before anything is allocated, and each data set is read once however many entries name it.
- **An axis with `min > default` or `default > max`** made `Math.Clamp` throw out of `WithAxes`; such an `fvar` now makes the font not
  variable. A byte-flip sweep over every byte of the five variation tables (three values each) asserts that reading an instance never throws.
- **Rounding is half up** (`FontVariations.Round`), as the specification, FreeType and fontTools do. `Math.Round` rounds half to even,
  which differs on the half-unit deltas that are common (delta 1 at scalar 0.5).
- **The instance cache is bounded and locations are quantized to 1/64**, so animating an axis cannot grow it without limit; a `Typeface`
  compares by location, so dropping the cache loses only object identity.
- Smaller: a tuple or region that straddles zero is ignored as the specification says, a point-matching composite component gets no
  `gvar` offset delta, a 4-byte `DeltaSetIndexMap` entry keeps its top bit, an `HVAR` whose advance map will not parse is dropped (the
  implicit glyph-index mapping would give wrong advances), and the sub/superscript sizes and offsets follow `MVAR` (`sbys`, `sbyo`,
  `spys`, `spyo`).
- `hasc`, `hdsc` and `hlgp` are applied to `hhea` as well as to the `OS/2` typographic values, as browsers and HarfBuzz do, although the
  specification only names the typographic ones.

## Traps

- **Normalized coordinates are rounded to 2.14 fixed point** (`FontVariations.Normalize`), as the tables are written; comparing with
  an unrounded value drifts by a design unit at 1000 units per em.
- **`GlyphIndexToWidth` clamps a glyph index past `numberOfHMetrics`** to the last metric, but `HVAR` must be asked with the original
  index.
- **The advance from `gvar`** is the distance between the first two phantom points, which are the last four points of the glyph, so
  the point count of a glyph (for a composite, its components) has to be known even when the outlines are not decoded.
- **A `Typeface` from `WithAxes` is not distinguished by PeachPDF's per-face caches yet**: they key by `ContentHash` plus synthesis,
  which two instances share. Nothing in PeachPDF calls `WithAxes` yet; the PDF embedding of an instance (which must write a static font,
  because PDF cannot embed a variable one) and the CSS wiring are the next slice and must add the location to those keys.

## Evidence

`VariableFontTests` (13 tests) in `PeachDrawing.Text.Tests`: axes, identity, clamping, both fixtures at every golden location for every glyph
(contours, segments and points), the font-wide metrics, and shaping an instance. The whole `PeachPDF.Tests` suite passes unchanged.
