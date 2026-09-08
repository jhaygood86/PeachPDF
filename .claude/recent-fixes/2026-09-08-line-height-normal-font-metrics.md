# `line-height: normal` resolves from the font's own metrics, not a flat 1.2x (#956)

## Load-bearing idea

CSS 2.1 §10.8.1 makes `line-height: normal` "based on the font of the element" - every real engine
(Chromium, Firefox, Safari, confirmed by reading Mozilla's `gfxFT2FontBase.cpp` metrics code, WebKit's
`FontMetrics.h`, and cross-referenced against the well-known browser-vertical-metrics literature) resolves
it the same way: the raw `hhea.ascender`/`|hhea.descender|`/`max(hhea.lineGap, 0)` triple, or - only when
`OS/2.fsSelection`'s `USE_TYPO_METRICS` bit (0x80) is set and the OS/2 typo fields aren't all-zero -
`OS/2.sTypoAscender`/`-sTypoDescender`/`max(sTypoLineGap, 0)` instead. Each component is scaled to the
font-size, rounded to a whole CSS pixel *independently* (not the summed total), then added together.
Crucially, none of the three engines substitute `OS/2.usWinAscent`/`usWinDescent` for `hhea` in the
non-typo case - that's a legacy Windows-GDI/old-IE convention.

This codebase already had an `Ascender`/`Descender`/`LineSpacing` triple on `FontDescriptor`
(`src/PeachPDF/Fonts/OpenType/OpenTypeDescriptor.cs`) - but its own comment says it's a direct port of
*WPF's* `FontDriver.ReadBasicMetrics`, which does exactly that legacy GDI substitution, and backs PDF
`/FontDescriptor` metrics plus baseline positioning (`RFont.Ascent`/`Height`). Reusing it for
`line-height: normal` would have been wrong twice over - wrong algorithm, and coupling an unrelated
concern to something correctness-critical for glyph placement. Instead, three new raw fields
(`FontDescriptor.NormalLineHeightAscent`/`Descent`/`Gap`, populated in `OpenTypeDescriptor.Initialize()`
via the *same* `os2SeemsToBeEmpty`/`dontUseWinLineMetrics` gate the existing block already computes, but
with the non-typo branch always reading `hhea`) and a new `RFont.NormalLineHeight` virtual (default `1.2 *
Size`, reproducing the old approximation, overridden by `FontAdapter` - mirroring the `HasVerticalMetrics`/
`GetVerticalAdvance` capability-gated-accessor pattern from the vertical-metrics work) keep the two
concerns separate. `DerivedStyle.ActualLineHeight`'s `normal` branch now reads `ActualFont.NormalLineHeight`
directly, in the same already-`PixelsPerPoint`-scaled space `ActualFont.Ascent`/`.Height` are consumed in
elsewhere (confirmed via `BaselineAlignment.cs`, `CssBoxMarker.cs`), rather than the old `GetEmHeight() *
PixelsPerPoint` correction the flat multiplier needed.

## What running it (not reading it) found

- The pinned regression test asserting the exact `1.2×` output
  (`LineHeightTypedStorageTests.NormalKeyword_ActualLineHeight_ResolvesToOnePointTwoTimesFontSize`) is
  gone by design - it pinned the behavior this issue argues is wrong. Its replacement embeds two bundled
  fonts (`BundledFonts.Ttf`/`Otf`) via `@font-face`/data-URL rather than relying on whatever "no
  font-family" resolves to (platform-dependent), and asserts against values hand-computed from each font's
  *own* real `hhea` table via `fontTools` - independent of the implementation under test - then confirmed
  those hand-computed numbers (27.00pt and 24.75pt at a 20pt font-size) matched the running code exactly on
  the first try, which is good evidence the rounding-order (`round each of ascent/descent/gap to a whole
  CSS px, then sum` - not `round the sum`) was implemented correctly the first time, not tuned to pass.
- A quick throwaway probe (not committed) loading eight real Windows system fonts at the same font-size
  confirmed the fix is genuinely font-dependent end-to-end, not just at the descriptor layer: Georgia 27pt,
  Arial/Trebuchet MS/Courier New 27.75pt (a real coincidence between three otherwise-unrelated fonts, not a
  bug - confirmed by checking their distinct `RFont.FaceKey`s), Verdana/Tahoma 29.25pt, Segoe UI 32.25pt,
  Comic Sans MS 33pt, all at a 24pt font-size that would have flatly produced 28.8pt under the old code.
- The full `PeachPDF.Tests` suite (10,378 tests) passed with **zero** regressions from this change - the
  flat `1.2×` value was apparently never depended on for exact pixel-position assertions elsewhere in the
  suite, only in the one test written specifically to pin it.
- **A multi-agent review pass caught a real bug the test suite above didn't**: `FontAdapter.NormalLineHeight`
  multiplied each ascent/descent/gap component by `PixelsPerPoint` *before* rounding it to a whole CSS
  pixel, instead of after. At the default `PixelsPerPoint = 1.0` - what every test in this repo's suite
  uses, including the ones written for this very fix - the bug is a no-op, since multiplying by `1.0` before
  or after a fixed-ratio rounding step gives the same answer. At any other `PixelsPerPoint` (e.g.
  `PixelsPerInch = 144`, issue #814's family), it rounded to the wrong granularity, since `Length.PointsPerPx`
  is a fixed pt-per-CSS-px ratio, not a pt-per-internal-layout-unit one. Two independent review angles
  (simplification/reuse) also independently converged on the same second finding: `OpenTypeDescriptor`'s new
  code re-tested the `os2SeemsToBeEmpty`/`dontUseWinLineMetrics` gate a second time instead of extending the
  branches that already test it, risking the two blocks silently diverging on a future edit to that gate.
  Both fixed: the rounding now happens once, per-component, in real-point space, with the single
  `* PixelsPerPoint` conversion applied only to the already-rounded sum (mirroring how `Ascent`'s own
  `Math.Round(_ascent * PixelsPerPoint)` keeps rounding and device-scaling as one ordered step, just at CSS-
  pixel granularity instead of whole-point); `NormalLineHeightAscent`/`Descent`/`Gap` assignments now live
  directly inside the existing typo/hhea branches, reusing their already-computed locals instead of re-
  reading the OpenType tables a second time. A new precise regression test
  (`NormalLineHeightMetricsTests.NormalLineHeight_ScalesExactlyLinearlyWithPixelsPerPoint`) wraps the *same*
  `XFont` in two `FontAdapter`s differing only in `PixelsPerPoint` and asserts the ratio is exactly the
  `PixelsPerPoint` ratio - which the pre-fix code failed by a real, non-rounding-error margin (a 20pt-ascent
  component alone was off by 0.75pt at `PixelsPerPoint = 2.0`).

## Deliberately not done

- No Windows-GDI-style `usWinAscent`/`usWinDescent` fallback path, matching all three modern engines - see
  above.
- No attempt to replicate the exact rounding function each engine uses internally (Gecko: `floor(x + 0.5)`;
  WebKit: `lroundf`) beyond "round to nearest, ties handled by `Math.Round`'s default banker's rounding" -
  the three differ only at an exact `.5`-CSS-px boundary, which no bundled or tested font hit.

## Evidence

- New/changed tests: `LineHeightTypedStorageTests.cs` (two new `ActualLineHeight` cases, replacing the
  pinned-flat one), `NormalLineHeightMetricsTests.cs` (new - direct `OpenTypeDescriptor` coverage of the
  hhea/typo-metrics branch selection including the `os2SeemsToBeEmpty` fallback edge case, plus the
  `PixelsPerPoint`-scaling regression test described above), `RFontVerticalMetricsDefaultsTests.cs` (one new
  case for the `RFont.NormalLineHeight` default).
- Full suite: `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0` - 10,379 passed, 0
  failed, 9 skipped (pre-existing platform skips).
- Diff coverage: 100% on the changed/added lines (`diff-cover` against `origin/main`).
- `dotnet build PeachPDF.slnx -t:Rebuild` - zero warnings.
- Visual verification: a new `line_height_normal` showcase (`PeachPDF.TestHarness/Program.cs`) renders
  `font-size: 24pt; line-height: normal` in three generic font families with a shaded one-line-box-tall
  background band behind each; rendered via `peachpdf` CLI and rasterized with PyMuPDF - the three bands
  are visibly different heights relative to their glyphs, and none of the text clips or overflows its band.
