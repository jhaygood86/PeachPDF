# Non-square-pixel hinting (#1433)

Closed #1433: `OutlineRequest.PixelsPerEm` was a single number, so `x_ppem == y_ppem` always and a device whose
pixels are not square (a non-uniform CTM, or a different horizontal and vertical output resolution) could not
be hinted at all — `RasterGraphics.HintingRequest` refused it outright. Both engines now carry the two axes
independently end to end, and the raster backend reaches the real-world trigger on its own.

## What changed, and where

- **Public API.** `OutlineRequest` gained `PixelsPerEmX`/`PixelsPerEmY`; `PixelsPerEm` became a convenience
  whose `init` sets both to the same value and whose getter reads `PixelsPerEmX` (an init accessor calling
  another init accessor of the same type during construction is legal C#, the same pattern the BCL's own
  "FullName sets FirstName and LastName" example uses). `GlyphOutline` gained matching read-only
  `PixelsPerEmX`/`PixelsPerEmY`; its existing `PixelsPerEm` property was left untouched so every existing
  reader keeps seeing exactly what it always did. Both additions are additive-only in `PublicApi.txt`.
- **`TtSize`/`CffSize`** (`Internal/Hinting/FreeType/`) each gained a two-axis `Create`/constructor overload;
  the existing single-ppem one now delegates to it with both axes equal, so every existing call site (33 of
  them in the test suite alone) needed no changes. `TtSize.Reset` ports `tt_size_reset`'s non-square branch
  exactly: `x_scale`/`y_scale` computed per axis, the CVT scale taken from the larger axis, and
  `TtSizeMetrics.XRatio`/`YRatio` (FreeType's `x_ratio`/`y_ratio`) computed the same way. `CffSize` gained
  independent `XScale`/`YScale`; `Ppem` (used for hint decisions) stays the *vertical* axis only, matching
  `cf2_getPpemY` — confirmed from `psfont.c` that Adobe's engine reads no other axis for blue zones, stem
  widths or darkening, so a non-square CFF size hints exactly as the same vertical ppem alone would; only the
  final per-axis point scale differs (`cff_size_request`'s own `x_scale`/`y_scale`, independent of each
  other, already the real behaviour it derives from).
- **`TtExecContext`** (`TtInterp.cs`/`TtInterpInstructions.cs`) gained `_isStretched` (set once per glyph run,
  `TT_RunIns`'s own `x_ppem != y_ppem` check) and a cached `CurrentRatio()` (`Current_Ratio`, reset by
  `ComputeFuncs` exactly where FreeType resets it — every `SVTCA`/`SPVTL`/`SDPVTL` family instruction, not
  just once per glyph). `ReadCvt`/`WriteCvt`/`MoveCvt`/`CurrentPpem` dispatch on it (`Read_CVT_Stretched` etc.);
  `WCVTF` keeps its own always-unstretched raw write (`WriteCvtRaw`), matching that FreeType's `Ins_WCVTF`
  bypasses `func_write_cvt` entirely. `MD`/`MDRP`/`IP` gained the non-square branch (scale each axis of the
  vector by its own `x_scale`/`y_scale` before the dual projection, instead of one `DUALPROJ` plus a single
  `FT_MulFix`), reusing the interpreter's *existing* `DualProject` for the actual projection — `FAST_DUALPROJ`
  is exactly that function called with an already-scaled vector, so no new projection primitive was needed.
- **What did *not* need porting, and why**: `FT_Vector_Length`/`FT_Hypot` turned out to already be the
  *general* CORDIC algorithm in `FtTrigon.Hypot` — the "ported only for the composite-offset case" note in
  `PORTING-NOTES.md` described what it was *used for* before this change, not a restriction in the algorithm
  itself, so `Current_Ratio`'s `FT_Hypot(x, y)` call needed nothing new. `TtGload.cs`/`CffGload.cs`'s actual
  point scaling was already per-axis (`Metrics.XScale`/`YScale` were already two separate fields, just always
  equal) — the "square pixels" gap was specifically the *hinting*-decision paths, not the base geometry.
  `TtGlyphLoader.Load<TState, TResult>`/`CffGlyphLoader.Load<TState, TResult>` needed no signature change at
  all: `TState` was already generic, so passing `(int X, int Y)` through it instead of `int` was enough.
- **`HintingEngine`**: `SizeKey` carries both axes (was one `Ppem26Dot6` field); the front-cache hash
  (`FrontSlot`) folds in the second axis too. `EffectivePpem` (the TrueType integer-ppem rounding) and the
  `gasp` lookup (which axis FreeType itself has no opinion on, since it never calls `FT_Get_Gasp` internally)
  both now take/report two axes — the gasp check uses the larger axis, consistent with the interpreter's own
  `ttmetrics.ppem`.
- **`RasterGraphics.HintingRequest`** dropped the `scaleX == scaleY` refusal (kept the "no rotation, no skew,
  no mirroring" one) and now asks for `PixelsPerEmX`/`PixelsPerEmY` from the device transform's two diagonal
  entries independently. Worth being explicit about: the dropped check was a *tolerance* comparison
  (`Math.Abs(scaleX - scaleY) > 1e-6 * ...`), not an exact one — it existed to treat a "nominally square" scale
  whose two axes only disagree by floating-point noise (from a matrix decomposition upstream) as square, not
  to refuse a genuinely non-square one. Removing it means that noise no longer gets snapped back to equal
  before `TtSize.Reset`/`CffSize` round each axis to its own whole ppem, so the only real behaviour change is
  the narrow case of a nominally-square scale sitting close enough to a half-pixel boundary that per-axis
  floating-point divergence makes the two axes round to *different* whole ppems: before, that text was refused
  (treated as unhintable); now it takes the stretched path and is hinted for the (by one pixel) different
  ppems it actually rounds to. For the common case (a TrueType font whose `head` flags ask for integer ppems,
  which is nearly every font) this is additionally absorbed by `EffectivePpem`'s own whole-pixel rounding. No
  epsilon re-snap was added back: previously-refused near-square text now being hinted is the more correct
  outcome, not a regression, so this is recorded here rather than undone.

## Exactness

`assets/fonts/generate_hinting_anisotropic_fixtures.py` (new) drives a from-source FreeType 2.14.3 build
(`VER-2-14-3`, `0a0221a1347e2f1e07c395263540026e9a0aa7c7` — the same commit `PORTING-NOTES.md` pins, built with
CMake + MSVC as a shared library; no prebuilt DLL was found in the scratchpad this session, so it was rebuilt
from a fresh shallow clone) with `FT_Set_Char_Size` given unequal width/height at a common resolution for most
cases, and one case that instead keeps the size equal and varies the horizontal/vertical *resolution*
arguments — both reach the same `FT_Request_Metrics` computation in `ftobjs.c`, confirmed by reading it. Two
TrueType fonts (a real hinted one, the synthetic opcode-exercising fixture) and two CFF fonts (a real one, the
synthetic hint-exercising fixture) are recorded at seven size pairs each, including one exactly square pair
recorded through the *same* anisotropic code path (the regression guard) — `HintingAnisotropicGoldenTests.cs`
compares every glyph's hinted points exactly in 26.6, refusals included, and asserts the reference file
actually contains both a square and a non-square run (so the guard isn't vacuous). All 45 cases pass.

The full existing hinting suite (4,653 tests before this change touched anything) passed unchanged throughout
— confirmed early, before writing any new tests, since `TtSize`/`CffSize`'s equal-axis arithmetic is
byte-for-bit identical to the code it replaced (the same `FtCalc.DivFix` call made twice with the same
arguments cannot differ). `GridFittingApiTests.cs` adds a public-API-level guard
(`EqualAxesGiveTheSameOutlineHoweverTheRequestSpelledThem`) and non-square coverage
(`NonSquarePixelsFitTheOutlineToAStretchedGrid`) alongside the internal golden comparison.

## A bug found and fixed in the test-writing process, not in the port

The anisotropic golden generator's first draft created one FreeType library/face per *size* and looped
glyphs inside it, instead of one per *glyph* the way `generate_hinting_cff_fixtures.py` already does (its
own docstring explains why: the Type 2 `random` operator's state leaks from one glyph to the next within a
face, and the C# port deliberately starts every glyph fresh from the font's `initialRandomSeed`, matching
FreeType only when FreeType's own reference is also generated fresh per glyph). This produced reference data
that disagreed with the port for exactly the handful of glyphs in the synthetic CFF fixture that use
`random` — a bug in the new script, not the CFF engine. Caught by comparing the *same* size both ways
(isolating a single glyph across several sizes) before concluding anything about the engine; worth recording
because it is the kind of golden-generator mistake that would otherwise look exactly like a real regression.

## Benchmark

`dotnet run -c Release --project PeachDrawing.Text.Benchmarks -- --filter "*HintingHot*"` (cached-lookup
throughput), before (`c100b04f`, via a temporary WIP commit and `git checkout` in this worktree, then
`git reset --soft` back) and after:

| Font | Threads | Before | After |
|---|---|---|---|
| HintingCff | 1 | 10.97 ns | 12.64 ns |
| HintingCff | 8 | 20.46 ns | 26.25 ns |
| LiberationSans | 1 | 12.57 ns | 14.52 ns |
| LiberationSans | 8 | 19.21 ns | 30.94 ns |

A real, if small (single-digit-nanosecond), increase on this specific micro-benchmark: it measures
`Typeface.TryGetOutline` end to end from a warm cache, and the request-validation/cache-key path
(`ValidateRequest`, `EffectivePpem`, `FrontSlot`) now does twice the rounding/hashing work it used to (one
axis, now two) on every call, warm or not. Not chased further: the absolute cost is a few nanoseconds against
tens of nanoseconds already spent per lookup, dwarfed in a real render by outline construction and
rasterization, and the two-axis validation is exactly the work the feature requires — a fast path that special-cased
equal axes to skip it would add real complexity to save noise-level time.

## Migration note

Filed a migration note (`.claude/migration-notes/2026-09-27-text-under-a-non-uniform-scale-is-now-hinted.md`):
existing PDFs drawn through the raster backend under a non-uniform scale (a CSS 3D transform, or a
non-square rasterization DPI) used to get the plain scaled design outline; they now get a grid-fitted one,
which is a genuine pixel-level rendering change for that specific, narrow combination.
