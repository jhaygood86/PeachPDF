# CFF hinting by Adobe's engine

`Typeface.TryGetOutline(glyph, OutlineRequest, ...)` and `PdfGenerateConfig.TextHinting` now fit fonts with CFF (PostScript) outlines too: the
stem hints of a charstring and the blue zones of the font's Private DICT place stems and flat edges on whole pixels. The engine is a C# port of
Adobe's, the `psaux` module of FreeType 2.14.3 (`Internal/Hinting/FreeType/Ps*.cs`, with the CFF loader around it in `Cff*.cs`); `PORTING-NOTES.md`
there lists every file and difference. The unhinted `Type2CharstringInterpreter` is untouched and is still the `GridFitting.None` path and the fallback.

## What the load-bearing idea was

- **Exact against FreeType, on everything an engine reads.** `HintingCff.golden.json.gz` holds what FreeType 2.14.3 makes of every glyph of two real CFF
  fonts and five synthetic ones (random Type 2 charstrings with hints near the blue zones, hint masks, flex, subroutines, the arithmetic operators,
  accent composition, family blues, `LanguageGroup 1`, a sheared font matrix, a CID-keyed font with three Font DICTs, and hostile charstrings),
  in three modes, and the port matches all of it point by point in 26.6, refusals included. The real fonts matched at the first run; the fixtures
  found only the `random` operator and the outline size limit.
- **`random` is per glyph.** FreeType keeps the state of the `random` operator in the subfont and seeds it from the address of an object unless a seed is
  set, so a glyph's numbers depend on the glyphs loaded before it (and on the run). The port starts every glyph from the Private DICT's own seed, which is
  what FreeType does when its `random-seed` property is 0, and the reference data loads each glyph from a face of its own with that property. The
  first version of the port kept the state in the shared subfont and disagreed with the goldens on every glyph that used the operator.
- **`FT_OUTLINE_POINTS_MAX` is 65,535, not 32,767.** The first hostile fixture glyph with 40,000 points was refused by the port and loaded by FreeType. The
  goldens' first reading of it was wrong too: `freetype-py` declares `n_points`, `n_contours` and the contour ends as signed 16-bit numbers, so the
  generators mask them with 0xFFFF.
- **A CFF contour can end on two control points.** FreeType's contour close drops a last on-curve point that lies on the first, so the last cubic curve of
  such a contour has no end point and ends at the contour's start. The public outline had to be built with that in mind (the API test on a real font is
  what found it: every glyph fell back to the unhinted outline until it was).
- **Points are not scaled again after hinting.** In FreeType the outline of a hinted CFF glyph is not scaled by `cff_slot_load` when the pshinter module
  is present (`hints_funcs`); only the advance is, and it is rounded to a whole pixel by `ft_glyphslot_grid_fit_metrics`. That depends on the reference
  build having the standard module list.
- **Modes do not matter.** Adobe's engine has one behaviour; `Standard` and `Monochrome` share cache entries and the goldens show them equal.

## Deliberately not done

CFF2 and Type 1 (the branches of the interpreter for them, tracked as an accepted gap, `text-hinting-cff2-outlines-are-not-hinted.md`), stem darkening
being reachable from the public API (`text-hinting-cff-stem-darkening-is-not-reachable.md`), the encoding and name tables of the CFF font, and hinting a
glyph above 2000 ppem (FreeType retries those unhinted; here the caller gets the unhinted outline).
