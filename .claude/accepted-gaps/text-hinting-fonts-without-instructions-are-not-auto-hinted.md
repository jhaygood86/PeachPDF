# Text hinting: fonts without hints are not auto-hinted

A TrueType font with no instructions, and a CFF font whose charstrings carry no hints, is answered with the
scaled design outline (a CFF glyph without hints is only rounded to 1/64 pixel). FreeType's auto-hinter
(`src/autofit`), which derives hints from the outline's own shape instead of running a program the font
carries, was deliberately not ported. Tracked in [#1434](https://github.com/jhaygood86/PeachPDF/issues/1434),
which stays open.

## What this pass confirmed (measured, not assumed)

The gap was re-examined against FreeType **VER-2-14-3** (`0a0221a1347e2f1e07c395263540026e9a0aa7c7`), the exact
tag `PORTING-NOTES.md` already pins the TrueType/CFF port to, so any future work starts from the same source
tree rather than a drifted one.

- **Licence: no new obligation.** Every file in `src/autofit` carries only the plain FreeType Project License
  header (`Copyright (C) ... David Turner, Robert Wilhelm, and Werner Lemberg`, or, for `afadjust.c` and the
  `ft-hb*.c` bridge, a named individual contributor under the same FTL terms — the same pattern `fttrigon.c`
  already has multiple copyright holders under). None carries the Adobe patent-grant header `cf2*`/`ps*.c`
  have. If an auto-hinter port lands, `THIRD-PARTY-LICENSES.md`'s existing FreeType entry only needs its file
  list extended (a new `### FreeType's auto-hinter` subsection, mirroring the CFF one) — no new credit-line
  plumbing, CLI output, or release-archive change is required beyond that.
- **FreeType auto-hints automatically; no opt-in flag is needed.** Read from `src/base/ftobjs.c`
  (`FT_Load_Glyph`, the `autohint` decision around line 955): FreeType auto-hints without
  `FT_LOAD_FORCE_AUTOHINT` whenever the font is scalable, not "tricky", and — for an SFNT with TrueType
  (`glyf`) outlines specifically — `ttface->num_locations` is nonzero while `font_program_size == 0` and
  `cvt_program_size <= 7` (its comment: "there don't exist real TTFs where both `fpgm` and `prep` are
  missing"). That is exactly the `fpgm`/`prep` presence this repo's `TtFace`/`TtSize` already read for the
  gasp/hinting-mode decision, so **no new public API or caller-visible choice is needed** on `GridFitting`/
  `OutlineRequest` — confirming the plan's expectation. A hint-less CFF-outline OpenType font does not take
  this branch at all (`num_locations` is a `glyf`/`loca` concept); FreeType's own default behaviour is to
  leave such a font on the native (Adobe) engine's un-hinted charstring path unless the caller asks for LIGHT
  mode and the driver doesn't already hint lightly. A future port needs to reproduce that distinction, not
  invent its own trigger.

## Why this is still out of scope for one PR, with numbers

`src/autofit` at this tag is **~21,100 lines** across `.c`/`.h` (excluding the generated `.dat`/`.cin`
sources), of which **~16,600 lines are `.c`** bodies. `FT_CONFIG_OPTION_USE_HARFBUZZ` is off in FreeType's
own shipped default config, which is what the existing port's reference builds use — so `afshaper.c`'s
HarfBuzz-feature-lookup path, `afgsub.c` and the whole `ft-hb*.c` bridge collapse to their no-op fallback
branches in the build any goldens would be generated from, and don't need porting for a first cut. Even after
removing those, a **Latin-only** port (the smaller of the two writing systems the plan names as an acceptable
floor) still needs:

| File | Lines (`.c`) | Role |
|---|---:|---|
| `aflatin.c` | 5,102 | segment/edge detection from the outline, blue-zone flattening, stem width + light/normal hinting + darkening, alignment — the actual algorithm |
| `afhints.c` | 1,811 | shared `AF_GlyphHints`: points, segments, edges, contours every writing system builds on |
| `afadjust.c` | 1,621 | per-character-code adjustments (default-config path only, no HarfBuzz) |
| `afranges.c` | 1,101 | Unicode range-to-script tables that drive writing-system detection |
| `afblue.c` + `afblue.dat` | 1,900 | blue-zone reference-character strings per script |
| `afloader.c` | 708 | orchestrates outline load → hint → write-back per glyph |
| `afglobal.c` | 537 | per-face style/script metrics cache and detection |
| `afmodule.c` (trimmed) | ~300 | the module-registration slice actually needed once FreeType's own module system is dropped |

That is **≈13,000 lines of dense, numerically fiddly C** — segment/edge/blue-zone detection is heuristic
code, not a deterministic bytecode/charstring interpreter, so it is substantially harder to port line-for-line
without a subtle mismatch than the already-ported TrueType/CFF engines were. For comparison, the *entire*
currently-shipped port (TrueType interpreter, CFF/CFF2 engine, and variable-font support, across both) is
**17,978 lines of C#** total, landed as several PRs over multiple sessions (see the dated entries under
`.claude/recent-fixes/` from 2026-09-26 and 2026-09-27). Latin alone is already on that order, before CJK
(`afcjk.c` adds another 2,370 lines and its own vertical/horizontal metrics and blue-zone logic reusing but
extending `aflatin`'s machinery) and before the exactness bar this repo holds every hinting port to: goldens
generated from a real compiled FreeType build and compared point-for-point in 26.6, plus hostile-font/fuzz
coverage with a bounded-work fallback, plus wiring, a showcase, and 90% diff coverage on the new code. Given
that, landing even the plan's suggested floor (Latin + CJK) as a single, verified, production PR is not a
responsible scope for one pass — it would either take materially longer than this session reasonably allows,
or ship code that has not actually been proven byte-exact against FreeType, which is exactly the failure mode
this repo's painting/hinting testing conventions exist to catch (see `CLAUDE.md`'s "Testing conventions" and
this port's own "Exactness" bar). No autofit code was written or landed in this pass; the gap is unchanged in
practice, only in how precisely it is now understood.

## A phased plan for whoever picks this up

In roughly the order `af_writing_system`'s own registration list already implies (dummy, then latin, cjk,
indic):

1. **Shared infrastructure first, on its own**: `AF_GlyphHints` (`afhints.c`), the per-face globals/detection
   cache (`afglobal.c`), the Unicode range tables (`afranges.c`), and the blue-string data (`afblue.c`/
   `afblue.dat`) as a literal data-table port. None of this is independently useful or testable end-to-end
   without a writing system built on it, so it should land in the same PR as the first writing system, not
   as a standalone PR with nothing to verify it against.
2. **Latin** (`aflatin.c`, `afadjust.c`'s default-config path, `afloader.c`, the module glue actually needed):
   the writing system that also backs Cyrillic and Greek in FreeType (they reuse the Latin submodule), so it
   is the highest-value single script.
3. **CJK** (`afcjk.c`) next, reusing the shared infrastructure from (1).
4. **Indic and the dummy fallback** (`afindic.c`, 157 lines; `afdummy.c`, 77 lines) are cheap and can ride
   along with either PR above once the shared pieces exist.
5. Wiring (`RasterGraphics`/`HintingEngine` picking the auto-hinter path automatically for a font whose own
   instructions are empty, per the `ftobjs.c` decision above) and the showcase land with whichever PR first
   makes a writing system's output visible.

Each phase needs its own golden-generation script (following `assets/fonts/generate_hinting_*.py`'s pattern)
driving the real FreeType DLL with `FT_LOAD_FORCE_AUTOHINT` (or, to match FreeType's own default trigger
exactly, an instruction-less fixture that reaches the auto-hint branch without forcing it), and new
instruction-less TrueType / hint-less CFF fixtures — `assets/fonts/` does not currently have either.
