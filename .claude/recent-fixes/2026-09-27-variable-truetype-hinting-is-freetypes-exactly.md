# Hinting a variable TrueType instance is FreeType's arithmetic, exactly

A TrueType instance from `Typeface.WithAxes` was hinted after this package's own double-precision `gvar` deltas moved the points, with `cvar` added to the
control values as `Round(delta * 64)`, at a location normalized to 2.14. It is now hinted by a port of `ttgxvar.c` (`Internal/Hinting/FreeType/TtVar.cs`): the
normalized coordinates, the tuple scalars, the summed deltas, the interpolation of the points a tuple leaves out, the phantom points, `cvar`, `HVAR`, `VVAR`,
`MVAR` (the `gasp` ranges and the typographic ascender and descender) and `avar` version 2 are FreeType's, in 16.16, and compared with FreeType 2.14.3 point for
point in 26.6, refusals included. Closes [the exactness gap](https://github.com/jhaygood86/PeachPDF/issues/1432) (the accepted-gap file is deleted).

## What the load-bearing idea was

- **A port of the whole of `ttgxvar.c` that matters, not a fix of the rounding.** The differences from the double path were not only where the sum is rounded: FreeType
  keeps the deltas as 16.16 `FT_Fixed`, multiplies each by the tuple's scalar with `FT_MulFix`, interpolates the missing points in 16.16 (`FT_DivFix` for the scale)
  and *then* rounds twice, once to a whole font unit for the points the interpreter sees (`FT_fixedToInt`, truncating to 16 bits) and once to 26.6 for the outline
  that is scaled (`FT_fixedToFdot6`); `TT_Process_Simple_Glyph` scales the second, `(FT_MulFix(unrounded, scale) + 32) >> 6`, and hands the interpreter the first as its
  unscaled copy. None of that is reachable from a floating-point sum, so the tables are read again by a port (`TtVarTables` per font, cached; `TtBlend` per location,
  immutable).
- **The hinting side takes FreeType's normalized vector, not the package's.** `TtVarTables.NormalizedCoordinates(font, variation)` makes it from the design coordinates
  (`UserValues`, multiples of 1/64, exact in 16.16) with `ft_var_to_normalized` and the `avar` table in 16.16 (segment maps by `FT_MulDiv`, version 2's cross-axis
  mapping by an item variation store and a delta-set index map). The package's `VariationCoordinates.Normalized` (2.14, for the unhinted outlines, unchanged) differs
  from it by up to 2 in 16.16, and every consumer of hinting takes the new one: TrueType, CFF2 (the CFF2 tests can now reach every design location through the API,
  not only the dyadic ones) and the ranges of `gasp`. The same store code serves `HVAR` (advances, also for CFF2 now), `VVAR`, `MVAR` and `avar`.
- **The phantom-point rules of the loader are per face, not per glyph.** `TT_Process_Simple_Glyph` reads `variation_support` (HVAR/VVAR loaded) to decide whether the
  phantom points come from the deltas (no table: the advance follows `gvar`) or are scaled from the table-adjusted advance (`FT_MulFix`, hinted only); the VVAR flag is set
  only for a font with vertical metrics, because FreeType loads `VVAR` only when it asks for a vertical metric. Empty glyphs and composites go through the same
  function with a small outline of component offsets (each its own contour) and the phantom points, and the offsets are truncated to `FT_Int16` afterwards.
- **A face nothing was set on has no blend at all.** `TtFace.TryCreate(font, name, null)` is FreeType's face before any variation function was called; `normalized` all zero
  is a face that was set to the defaults (it has a blend that varies nothing). The two differ in `GETINFO` (the variation bit), `GETVARIATION`, and, for a `cvar` tuple
  whose peak is 0 on every axis, in the control values. The public API's default typeface is the first.

## What running it showed

- **`FT_GET_*` are checked reads in 2.14.3.** The port was first written with the older belief that `FT_GET_USHORT()` and friends read a frame without a bound (a table that
  declares more than it holds reads on into the next table). The golden data said otherwise on the first truncated `avar` (8 bytes, a font FreeType still sets locations
  on): `FT_Stream_GetUShort` gives 0 and does not move the cursor when fewer than two bytes are left in the frame. Only the `FT_NEXT_*` reads through a local pointer are
  unchecked, and the code that uses them checks the room it needs first. `FtMemStream` keeps the two apart.
- **The harness can make FreeType do something an application does not.** Asking a face that was never set for its coordinates (`FT_Get_Var_Blend_Coordinates`) makes FreeType
  select the default instance (`TT_Get_MM_Blend` calls `tt_set_mm_blend`), which varies the control values by `cvar`, including a tuple whose peak is 0 on every axis; the
  seven mutants out of 20,000 that differed at the "as it opens" location were that, not the port. The generator no longer asks. Likewise a mutation-testing script that puts
  the original file back with an older timestamp than the build output leaves the mutated assembly in place (`dotnet build` sees nothing to do): the "differences" after a
  mutation run were the last mutation. Touch the file.
- **The first golden run matched in the engine (every run, once the loader was written)** and the mutation checks show it is not vacuous: rounding `FT_fixedToInt` down fails 14 runs, truncating
  `FT_fixedToFdot6` 657, not applying `cvar` 667, `HVAR` 774, scaling from the rounded points 1,315, no interpolation 1,381, ignoring the phantom points 247, `MVAR` on
  `gasp` 218, the avar map rounded to 2.14 47. (Doing the tuple scalar's `MulDiv` in floating point fails one: the scalar is rarely at a half.)
- **A differential fuzz found no difference in 80,000 mutants** (one to four bytes of `fvar`, `avar`, `gvar`, `cvar`, `HVAR` or `MVAR` changed, three locations each, 26 glyphs
  each), after the two corrections above; 600 of them and 82 hand-made faults are the committed reference. About one location in twelve of the mutants is one FreeType refuses to
  set, so refusals are compared as much as points.
- **A table can make FreeType do work no font needs, and the port does not follow it there.** Each of 4,095 tuples may apply to every point of a glyph with data of size 0 (so all
  read the same bytes): 4,095 x 65,535 additions and two arrays of 65,535 for each tuple, gigabytes of transient allocation for one glyph. The port reads into buffers rented once
  per glyph and refuses a glyph, or a set of control values, that would make more than 2^24 delta additions (`TtBlend.MaxDeltaWork`); an `MVAR` value or an `avar` 2 mapping
  is limited to 2^26 region-axis steps (`TtItemVarStore.MaxStoreWork`, an item's cost is its regions times the axes, and a font of a few megabytes can multiply that by thousands
  of values). A real font is orders of magnitude below both.

## Deliberately not done

- The scalar cache of the shared tuples (`blend->tuplescalars`) is not ported: it caches a pure function.
- The 2.14 rounding of the package's own coordinates was not changed: it is what the unhinted outlines are compared with fontTools by. An instance whose normalized vector rounds to 0
  in 2.14 but not in 16.16 (a design coordinate within 2^-15 of the default's range) is the default location to `WithAxes`; recorded in `PORTING-NOTES.md`.
- `FT_Set_Named_Instance` and the named-instance data of `fvar` are not read: nothing in a load depends on them.

## Evidence

`PeachDrawing.Text.Tests`: `HintingVariableGoldenTests` (engine and API, 24 locations of 14 fonts, two modes, three sizes: the normalized vector and `FT_Get_Gasp` exactly, and every
glyph point for point), `HintingVariableVariantTests` (82 faults and 600 mutants against FreeType), `HintingVariableRobustnessTests` (byte-flip sweeps of every variation table,
random damage to four fonts, truncations, work-amplifying tables, threads, the caches of two locations); the existing TrueType and CFF2 goldens unchanged. Suite in Debug and
Release, `PeachPDF.Tests`, and the diff coverage gate: see the pull request.
