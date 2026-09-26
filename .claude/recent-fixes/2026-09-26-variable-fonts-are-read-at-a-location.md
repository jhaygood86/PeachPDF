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
