# CFF2 (variable CFF) outlines are hinted

`Typeface.TryGetOutline` with a `GridFitting` other than `None` (and `PdfGenerateConfig.TextHinting`) now fits a variable font with CFF2 outlines at the
location of the typeface, by the stem hints and blue zones of its charstrings and Private DICTs, blended for the location. The accepted gap and its issue
are closed. The engine is the port of FreeType 2.14.3's Adobe CFF engine that already fitted CFF fonts (`Internal/Hinting/FreeType/`, `PORTING-NOTES.md`
lists every file and difference); what was missing were the `isCFF2` branches of its interpreter and the CFF2 half of FreeType's CFF loader.

## What the load-bearing idea was

- **A CFF2 font is opened the way FreeType opens it, not the way this package's own CFF2 reader reads it.** `CffFont.Load` is the port of `cff_font_load`
  with its CFF2 branches (a Top DICT whose length is in the header, INDEXes with a 32-bit count, FDArray always there, `vstore`), so every refusal is
  FreeType's. It does not use `Cff2Table`, the reader the unhinted path uses: that one is written to draw glyphs in double precision and to be lenient
  where a PDF viewer is, and nothing about it says what FreeType refuses.
- **The location is FreeType's normalized vector, handed to the face.** FreeType opens a font that has `fvar` at its default instance, so its `blend`
  operators always see a vector with one coordinate per axis (zeros at the defaults), and a CFF2 font without `fvar` sees none and blends as the default
  design. The two differ for a font whose regions have a peak of 0 on every axis, and in whether a store whose axis count is not `fvar`'s is an error
  at all; `HintingEngine.NormalizedCoordinates` gives exactly that (the typeface's coordinates, exact in 16.16 because they are multiples of 1/16384).
- **Blend arithmetic is FreeType's 16.16, twice.** `cff_blend_build_vector` (region factors by `FT_MulDiv`, peak 0 skipping an axis, the region ignored outside
  its extent) builds the vector; `cf2_doBlend` and `cff_blend_doBlend` sum `value + MulFix(delta, factor)` in 32-bit wrapping arithmetic. Nothing here uses
  the package's `ItemVariationStore` scalars, which are doubles. Reading FreeType before writing found the subtleties the test corpus then confirmed:
  a blended stack is `plain..., bases, deltas, n, blend` with the plain operands kept, `blend` does not clear the stack, its results are 5-byte `255` numbers
  in a buffer of their own (here: appended to a copy of the Private DICT, with operands as positions in it), and `vsindex` after `blend` is an error
  in a DICT and in a charstring.
- **The Private DICT is a function of the location.** A CFF2 Private DICT blends (blue zones, `StdHW`, `StdVW`, `BlueScale`), so the blue zones the engine
  uses at a location are a re-parse of the DICT with that location's vector, which FreeType does inside `cf2_font_setup`, ignoring the outcome. `CffFont`
  does it once when the face is made (`ApplyVariation`), for every subfont; a re-parse that fails leaves the DICT as far as it was read, as in FreeType. A
  face is per location, and so is a `HintingEngine` (one per typeface, and a typeface is one location), so the size and glyph caches are per location by
  construction; the location's identity is in `SizeKey` as well, so that nothing one location gave could be taken for another's if an engine were ever shared.
- **CFF2 charstrings differ from CFF's in small ways that all had to be ported**: the width is the default (0) and `haveWidth` starts true, the operand stack
  is `maxstack` (513) entries, an explicit `return` or `endchar` is ignored (only the end of the charstring ends it), the arithmetic, storage and
  conditional operators (`12 3` to `12 30` and the reserved ones) are unknown operators that clear the stack, and `endchar` never composes an accent.

## What was found by running it

- **The first golden run matched: 269 of 269 runs, then 146 fault fonts and 400 mutants.** Nothing had to be fixed in the interpreter or the loader
  after the port was first compiled. A test that could not fail would look the same, so it was checked the other way: with the Private DICT re-parse left
  out, 129 of the 158 engine runs of the first fixture fail. The reference is not vacuous either: 3,600 glyph loads and 970 refusals in the multi-Font-DICT
  fixture, and the location changes the outlines of most glyphs that load.
- **The random glyph generator of the CFF fixtures makes bad CFF2 glyphs.** Its arithmetic operators (`12 x`) are ignored by a CFF2 interpreter, which
  clears the stack, so the `rlineto` after them had one operand and the glyph failed: 40% of the glyphs were refusals. They are followed by a whole line now
  (about 10% of the glyphs are refused, as in the CFF fixtures).
- **A differential fuzz found the one difference and no other.** 50,000 random mutants (1 to 4 bytes of the CFF2 table changed) of three fixtures were run
  through FreeType and the port outside the suite (`--mutants` of the generator makes a bigger file; the committed one has 400): the same face refusals, the
  same glyph refusals and the same points on all but 3. Those 3 have the top bit of an INDEX's count set with two-byte offsets: FreeType built for a 32-bit
  `long` (the Windows build the goldens come from) computes `(count + 1) * offsize` modulo 2^32, reads a table of a couple of bytes, and opens a face whose
  glyphs it draws from elsewhere. The port refuses a count of 2^31 or more; that is stricter and ends as the unhinted outline, and is recorded in
  `PORTING-NOTES.md`.
- **FreeType 2.14.3 cannot read FDSelect format 4.** `CFF_Load_FD_Select` knows formats 0 and 3, so a CFF2 font with several Font DICTs whose FDSelect is
  format 4 (which has 32-bit glyph numbers and is valid CFF2) is refused by FreeType, and by the port, which does what FreeType does.
- **The font source cache aliases fonts that differ by a byte.** `FontFileData.CalcChecksum` (an Adler-32 of the bytes and the length) is the key of the
  process-wide cache behind `FontSet.AddData`, and two variants of one font that differ in one or two bytes can collide (the header tests did: variant 3
  was served the font of variant 2). The variant and fuzz tests make the face with `FontFileData.CreateCompiledFont` instead, which is not cached. This is
  not new and was not changed; it is the reason a test that damages a font must not go through a font set.
- **Design coordinates and normalized coordinates are not the same question.** FreeType normalizes a design coordinate in 16.16 (`FT_DivFix`, and `avar`
  with `FT_MulDiv`); the package rounds to 2.14 (as fontTools and the variation tables do). The two are the same number where the design coordinate is a
  dyadic fraction of the axis, so the fixtures record locations of that kind, and the golden file carries the vector FreeType kept for each: the engine
  tests hand it to the port, and the API tests (`WithAxes`) check the package's vector is the same before comparing outlines. The real font's `avar` maps 0.6
  to 0.4, so some of its locations have a vector that is not a multiple of 1/16384; those are compared through the engine only.
- **Two amplification traps found in review, both untrusted-font side.** A blend leaves its results on the stack, so `512 blend` can be repeated for two
  bytes of DICT each time while every repeat appends 2,560 bytes of results (and, resizing an array for each, copies the whole buffer): a Private DICT of
  half a megabyte asked for gigabytes and minutes. What a Private DICT may append is bounded to 64 KB now, and the buffer grows geometrically
  (`AChainOfBlendsThatWouldFillMemoryIsRefusedInBoundedTimeAndSpace`). And a charstring `blend` whose operands are missing used to keep reading, `numBlends x
  regions` times (513 x 65,536 for a hostile store), after its first read had already recorded the error that loses the glyph; the loop stops at the error.
- **HVAR advances agree.** The advance of a hinted glyph is `hmtx` plus the `HVAR` delta, rounded to a pixel; the package's delta is a double sum and FreeType's a
  16.16 one, and they gave the same integer at every recorded location of the real font, including the raw ones.

## Deliberately not done

- The auto-hinter, `cvar`, non-square pixels and the TrueType side of variable-font hinting stay as they were (see the accepted gaps under `text-hinting-`).
- Type 1 and multiple-master code of the engine is not ported; a CFF2 font has neither.
- A `FontMatrix` in a Font DICT and the Top DICT is read (the fixtures have both), as for CFF, and blends in a Top or Font DICT are not: FreeType has no
  `blend` field there either.
- The random fixtures are not the showcase font: they are unreadable shapes, and no font with real hinted CFF2 outlines is bundled. The showcase
  (`text_hinting_cff2_standard` and `text_hinting_cff2_none`) uses `HintingCff2Boxes.otf`, a 2 KB CC0 variable font of boxy letters written for it
  (`generate_hinting_cff2_boxes.py`: one `wght` axis, strokes that thicken inward so the advances need no HVAR, hints and blue zones blended for the
  weight). Rasterized with PDFium and MuPDF, both show the hinted page's tops and bars crisper than the unhinted page's at 9 to 16 px, at all four weights.

## Evidence

`PeachDrawing.Text.Tests` `HintingCff2GoldenTests` (engine and API, 269 runs), `HintingCff2VariantTests` (146 faulty fonts, 35 charstring cases, 400 mutants),
`HintingCff2RobustnessTests` (every byte of a table flipped with three masks, random damage, every truncation, every variant read in bounded time and space,
the caches of two locations apart); the existing CFF tests unchanged.
