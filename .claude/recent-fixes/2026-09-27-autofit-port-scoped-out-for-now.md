# Autofit port (#1434) scoped out of this pass, with numbers to back it

Attempted to close #1434 (FreeType auto-hinter port for instruction-less TrueType and hint-less CFF fonts).
No autofit code was written; the accepted-gap file was rewritten instead, with the reasoning below, and the
issue stays open.

## What was actually done

- Confirmed the FreeType source tree already checked out in a prior session's scratchpad
  (`.../scratchpad/ft`) is genuinely at `VER-2-14-3` (`0a0221a1347e2f1e07c395263540026e9a0aa7c7`, verified
  with `git log`/`git describe`), the same tag `PORTING-NOTES.md` already pins the TrueType/CFF port to — so
  no re-fetch or re-pin was needed.
- Read every file under `src/autofit` at that tag. Confirmed by reading headers directly (not assumed) that
  none carries an Adobe-style patent header — all are plain FreeType Project License, some under a named
  individual contributor's copyright (`afadjust.c`: Craig White; `ft-hb.c`: Behdad Esfahbod), the same
  multiple-copyright-holder pattern `fttrigon.c` already has. No new licence plumbing is needed unless/until
  code actually lands.
- Read `src/base/ftobjs.c`'s `FT_Load_Glyph` auto-hint decision directly to answer the plan's open question:
  FreeType auto-hints an instruction-less TrueType font **automatically**, gated on `fpgm`/`cvt` table size
  (`font_program_size == 0 && cvt_program_size <= 7`, alongside the `num_locations` check that means this
  branch is TrueType/`glyf`-specific), not on a caller-visible flag. This confirms no new public API is
  needed on `GridFitting`/`OutlineRequest` when the port eventually lands — the existing `fpgm`/`prep`-size
  reads `TtFace`/`TtSize` already do for the gasp decision are the same signal to reuse.
- Measured `src/autofit`'s real size instead of guessing: **~16,600 lines of `.c`** across the module.
  `FT_CONFIG_OPTION_USE_HARFBUZZ` is off in FreeType's own default config (confirmed in `ftoption.h`), which
  is the config any goldens would be generated from, so the HarfBuzz-dependent files (`afshaper.c`,
  `afgsub.c`, `ft-hb*.c`) reduce to their fallback branches for a first cut. Even after that reduction, a
  **Latin-only** port (aflatin.c 5,102 + afhints.c 1,811 + afadjust.c 1,621 + afranges.c 1,101 + afblue
  1,900 + afloader.c 708 + afglobal.c 537 + trimmed afmodule.c ≈ 300) is **~13,000 lines** of dense,
  heuristic (not deterministic-interpreter) C to port exactly — comparable in size to the *entire*
  currently-shipped TrueType+CFF+variable-font hinting port (17,978 lines of C#, landed across several PRs
  over the two calendar days the 2026-09-26/27 `recent-fixes` entries cover).

## Why this wasn't landed as code

The plan explicitly allows scoping down to Latin+CJK and landing the rest as a tracked follow-up if
`src/autofit`'s size/risk, once actually read, turns out far beyond the interpreter/CFF ports. Having now
read and measured it, Latin *alone* is already on the order of the whole prior hinting effort, and this
repo's own bar for a hinting change (goldens generated from a real FreeType build, compared exactly in 26.6,
hostile-font/fuzz coverage with a bounded-work fallback, wiring, a showcase, 90% diff coverage) cannot be met
for ~13,000 lines of heuristic, easy-to-subtly-mismatch code inside one pass without either running far past
a reasonable single-PR scope or shipping something not actually proven exact — precisely the failure mode
the repo's own testing conventions (`CLAUDE.md`, "A passing test... is not proof") and this port's own
"Exactness" section exist to prevent. So nothing shipped; the accepted-gap file
(`text-hinting-fonts-without-instructions-are-not-auto-hinted.md`) was rewritten with the measurements above
and a concrete phased plan (shared infra + Latin first, CJK next, Indic/dummy cheap and along for the ride)
so a future attempt can start from real numbers instead of redoing this assessment.

## What a future attempt needs that doesn't exist yet

- New fixtures: `assets/fonts/` has no instruction-less TrueType font or hint-less CFF font today (every
  bundled font that has hints, has them). A synthetic one, generated the way the existing hinting-fixture
  generators are, is needed either way.
- A new golden-generation script following `assets/fonts/generate_hinting_*.py`'s pattern, driving the real
  FreeType build with the auto-hint path actually exercised (an instruction-less fixture reaches it without
  `FT_LOAD_FORCE_AUTOHINT`, matching FreeType's own default trigger read above, which is the more faithful
  thing to test against than forcing it).
- This should be its own multi-PR project the way the original TrueType/CFF/variable-font port was, not one
  PR — see the accepted-gap file's phased breakdown.
