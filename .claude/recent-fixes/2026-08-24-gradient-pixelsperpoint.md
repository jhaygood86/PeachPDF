# Gradient absolute/em-unit radii and stop positions now scale with PixelsPerPoint (#821)

Fixes issue #821, the tracked follow-up left by the #814 fix (see the now-deleted
`.claude/accepted-gaps/gradient-absolute-radius-pixelsperpoint.md`). `CssImagePainter`'s gradient brush
construction resolved an explicit `radial-gradient(50px at ..., ...)` radius and absolute/em-unit gradient
stop positions (`red 10px, blue 50px` / `blue 2em`) via the bare `Length.ToPixel()` or a hard-coded `emPx`,
neither of which knew about `PixelsPerPoint` (`PdfGenerateConfig.PixelsPerInch / 72`) - so under a
non-default `PixelsPerInch` these values resolved too small relative to the (correctly DPI-scaled) box they
paint into.

## Load-bearing idea

`RGraphics` already grew a `PixelsPerPoint` virtual property in the #814 fix (`GraphicsAdapter` overrides
it with the real, adapter-driven value; the base default `1.0` is correct for every test-only mock) -
specifically so paint-time code with an `RGraphics g` in scope, but no `CssBox`, could read the ambient
scale without new parameter plumbing. `GetLinearGradientBrush`/`GetRadialGradientBrush` already take
`RGraphics g` as their first parameter, so the fix is `g.PixelsPerPoint` read once per brush build and
threaded as a `double` into `NormalizeGradientStops`/`ConvertLength` - no `CssBox` parameter, no adapter
cast, no new `using`. An earlier draft of this fix instead threaded a `CssBox box` through and duplicated
`CssValueParser`'s private `PixelsPerPointOf(CssBox box)` helper (reaching into `PeachPDF.Adapters`
directly from `Html/Core/Handlers`); a review pass caught that this crossed the
`Html/Adapters` abstraction boundary CLAUDE.md's architecture section names explicitly, and that
`RGraphics.PixelsPerPoint` already existed for exactly this situation - `g.PixelsPerPoint` is both simpler
and the architecturally-correct call.

`ConvertLength`'s `Em` branch had the identical bug (its `emPx`-based numerator is also an unscaled pixel
value being divided by a `gradientLength` that's already in the DPI-scaled internal space) but wasn't named
in the original issue text - caught by the review pass, fixed the same way (`* pixelsPerPoint`), and
covered by its own regression test.

## Deliberately not done

- `GetConicGradientBrush`/`NormalizeConicStops` are untouched: conic gradient stops resolve from
  `PositionRad` (an angle, parsed only from `<angle-percentage>`), never through `ConvertLength` or any
  `Length` value - there is no absolute-unit conic stop position to fix. (The deleted accepted-gap file's
  claim that the bug reached "conic ... gradient stop positions via `ConvertLength`" was itself inaccurate;
  confirmed by reading `ParsedConicGradient`/`TryParseConicAngle`.)
- Two adjacent, pre-existing (not introduced by this fix) bugs surfaced during review and were filed as
  separate follow-ups rather than folded in, mirroring how #814 itself scoped down and filed #820/#821
  rather than fixing everything it touched: an explicit gradient radius in `em`/`rem`/viewport units throws
  `InvalidOperationException` from `Length.ToPixel()` (the non-percent branch calls `ToPixel()`
  unconditionally, never checking `IsAbsolute` first) - see #823; and `ConvertLength`'s `Em` branch uses a
  hard-coded `emPx = 16.0` instead of the box's real font size, unlike `CssValueParser.ParseLength`'s
  box-aware resolution - see #824. Both would be fixed as a byproduct of routing gradient length resolution
  through `CssValueParser.ParseLength` instead of `ConvertLength`'s hand-rolled per-unit branches, which is
  a larger, deeper change than #821's literal scope.

## Evidence

- `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0` - full suite green (9200+ tests).
- Diff coverage vs. `main`: 100% (`diff-cover` against `coverage.cobertura.xml`).
- `dotnet build PeachPDF.slnx -t:Rebuild` - 0 warnings.
- Every new/changed assertion was verified to actually fail against the pre-fix code (temporarily reverted
  each of the `* pixelsPerPoint` multiplies in turn and re-ran the affected test, confirming a real,
  DPI-proportional discrepancy rather than a vacuously-passing assertion) - see
  `src/PeachPDF.Tests/Integration/GradientPixelsPerInchIntegrationTests.cs`.
