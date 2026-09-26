# Variable fonts with CFF2 outlines

A font with a `CFF2` table now has outlines at a location: `Typeface.TryGetOutline` on a `WithAxes` typeface runs the glyph's charstring with every
`blend` resolved, and `TypefaceExporter.ExportSubset` writes such a typeface (at any location, the default one included) as a static CID-keyed OpenType/CFF
font of the glyphs asked for, which is what a PDF can embed. The accepted gap and issue (#1407) are closed; hinting of CFF2 (#1441) is still a gap.

## What the load-bearing idea was

- **CFF2 is not CFF with two more operators.** Its own reader (`Cff2Table`): a header that gives the Top DICT's length (no Name, String or Top DICT INDEX), INDEXes
  with a 32-bit count (`CffIndex.ReadCff2`, which checks every count and offset against the table before allocating anything), an FDArray that is always there,
  an FDSelect of formats 0, 3 and 4 (4 has 32-bit glyph numbers and 16-bit Font DICT indexes), a `vstore`, and the Private DICT's `vsindex`. The DICT parser had to
  learn operators 22 to 24 (`CffDict.Parse(..., isCff2: true)`); in CFF they are reserved bytes that it skips without clearing the operands.
- **The scalars come from the store the font already has.** A CFF2 VariationStore is an `ItemVariationStore` whose data sets have no items, only region lists, so
  `ItemVariationStore.GetRegionScalars(dataSet, coordinates)` is the whole addition to `Variations/`. `blend` is `v(i) + sum(scalar(r) * d(i,r))`; the operands are
  `n` values, then `n * k` deltas row by row, then `n`, which fontTools' own output confirmed (the layout is easy to write the other way round).
- **`vsindex` is not the Private DICT's alone.** A charstring may set it (`vsindex` operand, before any `blend`), and it stays for the rest of the charstring, the
  subroutines it calls included. The fixture has all three: Font DICT 0 with no `vsindex`, a charstring that starts with `1 vsindex`, and a second Font DICT (chosen by
  FDSelect) whose Private DICT says `vsindex 2`.
- **The interpreter is shared.** `Type2CharstringInterpreter`'s `Interpreter` takes an optional `Cff2Blender`: no width, no `endchar`, a stack of 513 and the two operators
  when it is there. The flex operators (`12 34` to `12 37`) were added for both flavours, since real CFF2 fonts use them and they are shorthand for two curves.
- **Export re-serialises; it does not copy.** Charstrings of a CFF2 font cannot be copied into CFF (blends, no width, no endchar), and the outline is what a location
  means, so each glyph's outline at the location is written as `rmoveto`, `rlineto` and `rrcurveto`. Absolute coordinates are rounded to whole units *before* they are
  made relative, so rounding does not accumulate along a contour (rounding each delta drifts by half a unit per operator). CID-keyed with the identity charset (glyph
  index = CID) needs no String INDEX that grows with the glyph count, and the subset keeps every glyph in its place with an empty `endchar` charstring for the ones
  that were not asked for. The sfnt around it (`head` with its checksum adjustment, `hhea`, `hmtx` up to the last glyph asked for, `maxp` 0.5, `OS/2`, `name`,
  `post` 3, and `cmap` when kept) is written by `StaticCffFontBuilder`.
- **A font with a `CFF ` table is still handed over whole**, byte for byte (`ExportSubset_OfAFontWithCffOutlines_HandsOverTheWholeFont` compares the bytes).

## What running it showed

- **fontTools' instancer cannot instantiate a Private DICT that has a `vsindex`** (`instantiateCFF2` does `values[0]` on the integer, in 4.63), so
  `generate_variable_fixture.py` builds the reference from a copy whose `vsindex` is written into the glyph's charstring instead; the two mean the same and the
  font itself keeps the Private DICT entry. `hasattr(private, 'vsindex')` is also true after `del private.vsindex` until the entry is removed from `rawDict`.
- **The instancer rounds every blended operand and the operands are relative**, so its points stray from the exact ones by a unit or more along a contour (measured:
  1 at the end of a line, 1.5 in a counter). The tests are exact (1e-6) at the locations where every scalar is 0 or 1 (default, each end of each axis, the clamped ones),
  and within 2 units elsewhere. Do not tighten the second without changing what is compared.
- **A store's data sets can overlap.** `ItemVariationStore.TryParse` read each data set once per *offset*, and a hostile table can give many data sets different
  offsets inside the same bytes (a 6-byte header that a repeating pattern keeps re-forming), each claiming 65,535 region indexes: gigabytes from kilobytes. The values of the
  data sets together cannot exceed the table's bytes in a real store, so the parser now refuses one whose data sets claim more values than that. The test
  (`AVariationStoreWhoseDataSetsShareTheSameBytes_IsNotSupported`) fails without the check.
- **Subroutine depth does not bound the work.** Ten levels of subroutines each calling the next sixteen times is 16^9 calls. `MaxSteps` (2^20 operands and operators per
  glyph) now bounds it, for CFF too, which had the same exposure; a real glyph needs a few hundred.
- **A Private DICT of size 0 at the very end of the table is legal**, and was refused by an offset check that wanted the offset to be inside the table.
- **Amplification found in review, all on the untrusted-font side.** (1) Font DICTs are disjoint slices, but any number may name one Private DICT and Private DICTs one
  Local Subrs INDEX, so 65,535 Font DICTs over a 5,000-entry INDEX allocated hundreds of megabytes; both are now read once by position
  (`ManyFontDictsThatNameOnePrivateDict_ReadItsLocalSubrsOnce` measures the allocation). (2) `CffDict.ReadReal` ran on to the next 0xF nibble wherever it was, not
  at the end of its DICT. (3) `blend` recomputed the region scalars of a data set that can name one region tens of thousands of times, per glyph:
  `ItemVariationStore.GetRegionScalars` now works each distinct region out once, and `Cff2Table` keeps the factors of the last location asked about.
  (4) The width operand is clamped to a signed 16-bit number (the `hmtx` advance keeps the whole value).
- **What PDF viewers do with it.** The PDF writer's CID font dictionary says `CIDFontType2` and puts a CFF OpenType file in `FontFile3 /OpenType`, for every CFF font, not
  only this one. PDFium and MuPDF both draw the result correctly (the `variable_fonts_cff2` showcase, four weights and two widths, rasterized with both, agree and the
  weights differ), and fontTools reads the file back as a CID-keyed CFF font with the expected outlines, so the pre-existing combination was left alone.

## Deliberately not done

- Grid-fitting CFF2 outlines: [text-hinting-cff2-outlines-are-not-hinted](../accepted-gaps/text-hinting-cff2-outlines-are-not-hinted.md).
- `FontMatrix` (the Top DICT's and the Font DICTs'): the outlines are in the `head` units-per-em, as for CFF.
- The arithmetic, storage and conditional operators and the four-operand `endchar` of `seac` (not in CFF2 at all): a glyph that uses one has no outline, as in CFF.
- COLR colour glyphs over CFF2 outlines: `IsColorFont` still needs `glyf`, as for CFF (the synthetic selection glyph of an empty COLR base is a TrueType contour).
- A `blend` in a DICT (Private DICT values such as the blue zones): only the outlines are read.

## Evidence

`Cff2OutlineTests` (golden: fontTools' instancer, 12 locations, 8 glyphs), `Cff2TableTests` (blend, `vsindex`, the width, `endchar`, the stack of 513, FDSelect formats
0/3/4, flex operators, damaged tables and INDEXes, a byte-flip over every byte of the fixture's `CFF2` table with three masks, 3,000 rounds of random multi-byte damage,
every truncation), `Cff2ExportTests`, `VariableCff2FontIntegrationTests`. `PeachDrawing.Text.Tests` 1,651 and `PeachPDF.Tests` 14,312 pass in Debug and Release on net8.0.
