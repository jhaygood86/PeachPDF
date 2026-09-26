# `COLR` version 1 variable paints are read at a location

A `Typeface` from `WithAxes` now gives colour paint graphs with the variation deltas applied: `PaintVar*` (formats 3, 5, 7, 9, 13, 15, 17,
19, 21, 23, 25, 27, 29, 31), `VarColorLine`/`VarColorStop`, `VarAffine2x3` and the variable clip boxes. The public `ColorPaint` nodes carry
the numbers that apply at the location, so `ColorGlyphPainter` (PeachPDF) needed no change. One new public member, `Typeface.TryGetColorClipBox`
(and `ColorClipBox`), because the clip list was not read at all before. What is still open in the tracking issue is `cvar`.

## What the load-bearing idea was

- **Deltas are added while the paint is parsed** (`ColrTable.ParsePaintCore` with a `Location`), not in a second pass over a finished
  graph, because the value has to be resolved in the unit it is written in: an FWord/UFWord is `value + delta`, a 2.14 value (scale,
  opacity, offset, angle) is `value + delta / 16384`, a 16.16 value (`VarAffine2x3`) is `value + delta / 65536`, and an angle is then
  turned into radians from half turns. `varIndexBase + k` is the index of the k-th variable field of the record, in the order the
  fields are listed (a linear gradient's `x0 y0 x1 y1 x2 y2` are 0 to 5, a colour stop's offset is 0 and its alpha 1).
- **`ColrVariations`** is the store plus the `DeltaSetIndexMap`: the index goes through the map when the table has one, otherwise the
  outer index is the index's high 16 bits and the inner index the low 16. `0xFFFFFFFF` is "does not vary". A map that will not parse makes
  the whole store unusable (the indices would read the wrong deltas), a store that will not parse leaves the paints at their defaults.
- **The paint cache is keyed by location.** `ColrTable` is shared by every instance of a face and cached parsed paints by offset; it now
  caches by (location key, offset), and clears itself at 131,072 entries (paints and colour lines each) so that asking for many locations cannot grow it without limit.
  The default location and a font without a variation store share the empty key and are parsed exactly as before (no delta is added, nothing
  is clamped or sorted), so the PDF of a non-variable font is unchanged.
- **Three guards that only apply where a delta moved a value**: an opacity is clamped to 0..1, a radius cannot be taken below 0, and colour
  stops are put back in order (with a stable sort, `OrderBy`: `List.Sort` is not stable above 16 elements, and stops that share an offset make
  a hard edge, so their order is the colours) only when a delta moved a stop offset and the result is unsorted. A font's own unsorted stops,
  out-of-range alpha and radii are left alone, at every location.
- **A colour line is read once per location** (`_lineCache`). Paints may share one line (legal, offsets are shared) and a line may have 65,535
  stops, so a font could otherwise make the paint cache hold many gigabytes of copies of one line; the review that found it noted that
  bounding the paint cache by entry count alone does not bound its size.

## What running it showed

- **A pre-existing bug: sweep gradients were drawn turned by 180 degrees.** The angles of `PaintSweepGradient` are stored with a half turn
  taken off (fontTools calls it `BiasedAngle`, bias 1.0: `stored = degrees / 180 - 1`, so that +360 fits in the 2.14 range), and the reader
  used the stored value. The reference values from fontTools exposed it (the engine read -170 for 10 degrees). Confirmed against Chrome,
  which puts the seam of the `ColorTestV1` `S` glyph on the positive x axis, where PeachPDF put it on the negative one. The fix is in
  the same code (`stored + 1`), and is a visible change to any sweep gradient in a colour font (a migration note records it).
- **fontTools' instancer does not instantiate `COLR`**, but its `varLib` merger builds variable `COLR` from masters (it wants the plain
  paint formats in the masters and writes the `Var*` formats, a `VarIndexMap` and a store itself). So the fixture is built from six masters
  and the reference is the masters' numbers interpolated with `VariationModel`: the definition of what the store's deltas add up to. The
  deltas are whole numbers, so the tests allow 1.5 units for FWord-derived values and 0.004 for the fixed-point ones. Chrome (which reads
  variable `COLR` paints) draws the fixture at three locations the way PeachPDF does, which is independent evidence for the delta units.
- **The loader was not safe against a damaged `COLR` table**: a hostile `numLayers` allocated up to 16 GB, and a damaged offset made the
  whole font fail to load with `IndexOutOfRangeException` from `FontSet.TryMatch`. Every count is now checked against the table length,
  and the reads that go through the shared cursor are guarded, so a damaged table leaves out what it names and the rest still works. A paint
  that reaches past the end of the font is no paint (`ParseGuarded`). A byte-flip sweep (three values over every byte of the fixture's `COLR`
  table) reads every glyph's paint, layer and clip box at an instance and asserts nothing throws.
- The clip list and the variation data are read from the table's bytes, not through the face's cursor, so a malformed one costs the font
  nothing.

## Deliberately not done

- Nothing uses the clip box inside PeachPDF (the painter walks the paint graph and draws vector fills, which need no surface size); the
  member exists because the font's data was unread and a renderer that draws into bitmaps needs it.
- The reader does not sort the clip records or check them for overlap; it binary-searches them as the specification requires them to be
  ordered.
- Colour glyphs over CFF2 outlines are still not reported as colour fonts ([the CFF2 entry](2026-09-26-variable-fonts-with-cff2-outlines.md)).

## Evidence

`VariableColorFontTests` (fourteen glyphs at eleven locations against the interpolated masters, clip boxes, byte-flip sweep, the caching
identity, the `ColrVariations` index arithmetic with and without a map), `VariableColorFontIntegrationTests` (the PDF's opacities follow the
weight), and the `variable_fonts_color` showcase rasterized with PDFium and MuPDF (identical) and compared with Chrome.
