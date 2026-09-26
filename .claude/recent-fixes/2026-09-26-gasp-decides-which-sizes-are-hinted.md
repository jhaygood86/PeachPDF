# The `gasp` table decides which sizes are hinted

`HintingEngine.Get` no longer hints a size at which the font's `gasp` table does not ask for grid-fitting: the answer is `HintedGlyphResult.Failed`, so
`Typeface.TryGetOutline(glyph, request, ...)` gives the scaled design outline with `IsGridFitted` false and `TryGetGridFittedAdvance` says false, exactly as
for a font that cannot be hinted. Before, a caller that asked for fitting got it at every size, including the small ones a font turns hinting off at on
purpose. `PdfGenerateConfig.TextHinting` follows, since the raster backend goes through the same call. `TtGasp` (`Internal/Hinting/FreeType/`) reads the table.

## What the load-bearing idea was

- **FreeType does not decide anything from `gasp`.** The plan for this said the table decides "as FreeType does with its load flags"; reading 2.14.3 shows it
  does not: `tt_face_load_gasp` loads the table and `FT_Get_Gasp(face, ppem)` answers the query, and nothing in the loader or the hinter consults either. What
  is FreeType's, and compared exactly, is the query (first range whose `maxPPEM` is at least the size, version 0 masked to its two low bits, `-1` for no table, a
  version above 1, or no range reaching the size). What to do with the flags is the caller's, and `ftgasp.h` says it: `FT_GASP_DO_GRIDFIT` "really means TrueType
  bytecode interpretation. If this bit is not set, no hinting gets applied." That sentence is the policy: hint iff the bit is set, or the table says nothing.
- **Only `GASP_GRIDFIT` counts, for both modes.** The header says `DO_GRIDFIT`/`DO_GRAY` are for standard rasterization and `SYMMETRIC_GRIDFIT`/
  `SYMMETRIC_SMOOTHING` for ClearType (and then `DO_GRIDFIT` is "consequently ignored"). The raster backend draws grey-scale anti-aliased and bilevel text, never
  ClearType, so the standard flag is the one that applies; a range with only the ClearType flags (0x000C in the fixture) is therefore "not fitted".
- **The gate is in the engine, not in `TtFace`,** so it also covers a CFF font that has a `gasp` table (the table is an OpenType one, not a TrueType one). The gate
  is before the caches, so a refused size is not cached as a failure per glyph.
- **The size is the whole ppem the outline is fitted at:** `EffectivePpem` (a font that asks for integer ppems is fitted at the nearest whole one) and then
  `(ppem26.6 + 32) >> 6`; a request for 8.4 ppem of the fixture is 8 (not fitted) and 8.5 is 9 (fitted).

## What was found by running it

- **The fixture was not hinted at all at first, for a reason unrelated to `gasp`.** A first `HintingGasp.ttf` from fontTools' `FontBuilder` had glyph programs but
  `maxp.maxSizeOfInstructions` 0, and `TtFace.HasInstructions` (which is how the port tells a hinted font: an `fpgm`, a `prep`, or a declared size of glyph program)
  was false, so every size came back unfitted, allowed by the table or not, and the tests said so with "is not fitted" at sizes FreeType hints. FreeType itself
  hints such a font; real fonts always declare the size. The generator now sets the maximum profile and the bounding box (`recalcBBoxes` off, which also keeps
  the file deterministic; the `head` timestamps are pinned).
- **The API closes every contour with a line back to its start,** which FreeType's point list does not repeat; the point walk of the test drops it.
- **The reference is `FT_Get_Gasp` over ten tables** made byte by byte (a normal version 1, version 0 with bits it does not define, version 2, no ranges, a last range
  ending at 100 ppem, ranges out of order, a range of no flags, a range of zero size, no table), at every ppem from 0 to 300 and three larger, plus FreeType's hinted
  outlines of the two glyphs at 13 sizes. Disabling the gate (`AllowsGridFit` returning true) fails 9 of the 27 tests.
- **One deliberate difference from FreeType's reader:** it reads the ranges past the end of the table when the font file goes on there (`FT_FRAME_ENTER` checks the
  stream, not the table); the port stays inside the table and treats a table that does not hold its ranges as absent.

## Deliberately not done

`LTSH` and `VDMX`: FreeType 2.14.3 has no code for either table, so no golden can differ. The gap note is now only the non-square pixels part
(`text-hinting-pixels-are-square.md`, issue kept open). A switch to hint regardless of `gasp` (a caller can ask `GridFitting.None` for the design, but has no way
to force fitting where a font says no).
