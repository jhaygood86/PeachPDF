# Variable-font instances: font bounding box, vertical metrics and `avar` 2

An instance from `Typeface.WithAxes` now has its own font bounding box, its own vertical advances and origins, and reads the version 2
`avar` table (an axis whose value depends on the others). What is still open in the tracking issue is the `COLR` v1 variable paints and `cvar`;
the accepted-gap file was shrunk to those.

## What the load-bearing idea was

- **The box is derived from the glyphs, by the rule fontTools' instancer follows when it saves the instance**, because no table says how
  `head`'s box moves. For `glyf` outlines it is the box of every *point* of every glyph, off-curve points included (that is what the bounds
  in a `glyf` entry are), rounded to nearest; for `CFF2` it is the box of the *drawn curves* (fontTools uses the exact extremes of the cubics
  there), rounded outwards. Getting the two rules the wrong way round costs real units: the outline this engine builds elevates a quadratic
  to a cubic whose control points sit two thirds of the way to the TrueType off-curve point, so a box taken from the decoded outline would
  be tighter than the font's own glyph bounds by up to a third of a bulge. `GlyphOutlineDecoder.TryGetControlBounds` therefore reads the raw
  points (the same reader `DecodeSimple` now uses, `TryReadSimplePoints`) and transforms them through the composite's components
  (`Affine.Then`), instead of looking at the outline.
- **It is lazy.** Reading the box means reading every glyph, so `OpenTypeDescriptor.XMin` to `YMax` (now `virtual` in `FontDescriptor`) are
  computed on first use; nothing that only shapes or draws pays for it. The default typeface never computes it (`head` is right there).
- **Vertical advance** is `vmtx` plus `VVAR`'s advance mapping, and without `VVAR` the distance between the last two phantom points of
  `gvar` (top, bottom; the horizontal advance uses points 0 and 1). `VVAR` is asked with the original glyph index, not the one clamped to
  `numOfLongVerMetrics`, as `HVAR` is.
- **Vertical origin**: a font with `VORG` (CFF/CFF2) adds `VVAR`'s vertical origin mapping (no mapping means no variation, as HarfBuzz
  does); the fallback for every other font is `vhea.ascent`, now moved by `MVAR` `vasc` (and the typographic ascender by `hasc`).
- **`avar` 2** (`AvarTable`, which now owns the segment maps too): after the per-axis segment maps and rounding to 2.14, item
  `(0, axis)` (or the entry of the `DeltaSetIndexMap`) of the store is evaluated **at the whole mapped location** and *added*, in units of
  1/16384, rounded to a whole unit first (`otRound`); every axis's delta is worked out from the coordinates after the segment maps, none from
  another axis's result. This mirrors `avar.renormalizeLocation` in fontTools and `map_coords_2_14` in HarfBuzz.

## What running it showed

- **fontTools' instancer does not update `VORG`** ("VORG table not yet updated to reflect changes in VVAR table"), so the expected origins
  in `VariableCff2VerticalTest.golden.json` come from fontTools' own `VarStoreInstancer` over `VVAR`'s `VOrgMap`, not from the instancer.
- **The instancer's `head` box is only right after a save**: it is recalculated by `TTFont.save` (`recalcBBoxes`), so the generator saves and
  reloads before reading it (`box_of`). Reading it from the in-memory instance gives the default's.
- **`avar` 2 cannot be checked by the instancer directly**, so the oracle is the same font *without any `avar`* instantiated at the user
  values that normalize to the location `renormalizeLocation` returns. That is exact for an avar-less font.
- The vertical, CFF2 and avar 2 fixtures are new fonts (`VariableVerticalTest`, `...NoVvar`, `VariableCff2VerticalTest`,
  `VariableAvar2Test`); the existing fixtures are byte-identical after regenerating (checked with `git status`).
- Damaged `vhea`/`vmtx`/`VORG` tables are refused when the *font is loaded* (the readers assert, pre-existing), so the byte-flip sweeps
  cover `VVAR`, `avar`, `gvar` and `glyf`, which are the tables this change reads lazily. A composite that names itself is bounded by the
  composite depth (8) and by a shared glyph budget for the whole box (2^18 reads; the CFF2 walk has a 2^27 charstring step budget, taken off
  the per-glyph limit); a font that exhausts either keeps `head`'s box.

## Deliberately not done

- The vertical origin of a TrueType font is not derived from `vmtx`'s top side bearing plus the glyph's `yMax` (the specification's rule
  for `glyf` fonts); this engine has always used `vhea.ascent` there and PeachPDF's vertical text relies on it. `VVAR`'s top and bottom
  side bearing mappings are therefore not read.
- The exported (embedded) instance keeps the source font's `head` box; PDF viewers do not read it, and the PDF `FontDescriptor` already
  carries the instance's box through `TypefaceMetrics`.

## Evidence

`VariableFontBoxVerticalAndAvar2Tests` (box at 12 locations for three fixtures, vertical advances, origins, avar 2 outlines and advances at
9 locations against fontTools, byte-flip sweeps, a self-referencing composite), the `AvarTable`/`VvarTable` cases in `VariationTablesTests`,
and `VariableFontIntegrationTests.EachInstance_DescribesItsOwnFontBox` (the PDF descriptors of two instances differ).
