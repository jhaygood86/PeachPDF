# Gradient em/rem/ex/ch/viewport length resolution (#823/#824)

Fixes issues #823 and #824, the two adjacent follow-ups #821 left as tracked gaps (see
`.claude/recent-fixes/2026-08-24-gradient-pixelsperpoint.md`, since deleted). `CssImagePainter`'s gradient
stop-position/hint-position resolution (`ConvertLength`) used a hard-coded `emPx = 16.0` for `em`-unit
stop positions instead of the painting box's real font-size (#824), and `GetRadialGradientBrush`'s
explicit-radius branch called `Length.ToPixel()` unconditionally for any non-percent radius, which throws
`InvalidOperationException` for every relative unit except `em`/absolute ones weren't affected, so
`radial-gradient(2em at center, ...)` crashed instead of rendering (#823).

## Load-bearing idea

Both bugs share one root cause: neither code path had a `CssBox` in scope, so neither could consult the
box's real font-size or the shared `CssValueParser.ParseLength(Length,double,CssBox)` machinery. The fix
threads `CssBox box` through `GetLinearGradientBrush`/`GetRadialGradientBrush`/`ConvertLength`/
`NormalizeGradientStops` (all callers already have `box` in scope — it's a parameter of `Paint` and
`PaintGradientLayer`, just not previously passed down), and adds one new `ResolveGradientLength(Length,
CssBox, double pixelsPerPoint)` helper that every non-percent length (stop position, hint position,
explicit radius) now routes through.

The one genuinely subtle piece: `ResolveGradientLength` does **not** call
`CssValueParser.ParseLength(Length,double,CssBox)` for `em`/`rem`/`ex`/`ch` — despite that being the
existing, seemingly-obvious shared helper for "resolve a `Length` against a `CssBox`". Verified
empirically (a minimal box-tree harness, and rendering an explicit `2rem` radius at 72 vs. 96
`PixelsPerInch`) that `ParseLength(Length,...)`'s em/rem/ex/ch handling is itself wrong under a
non-default `PixelsPerInch` — `CssBox.GetEmHeight()`/`GetRemHeight()` live in the adapter's device-scaled
*font-measurement* space (`trueFontSizePt / PixelsPerPoint`, per `CssBox.NoEms`'s doc comment and issue
#631), the opposite direction from the box's internal, `PixelsPerPoint`-*inflated* layout space
`gradientLength`/`originRect` live in (issue #814's convention) — so `ParseLength(Length,...)`'s
zero-correction assumption for relative units, correct for viewport/container-relative units (confirmed:
an explicit `10vw` radius *is* DPI-invariant through plain `ParseLength`), is wrong by a full
`PixelsPerPoint²` for em/rem/ex/ch specifically. `ResolveGradientLength` undoes the device-scaling
(`box.GetEmHeight() * pixelsPerPoint`, mirroring `NoEms`) before handing it to `Length.ToPixels` as the
em/rem basis, then applies the usual absolute-length catch-up multiply on the result — landing on
`* pixelsPerPoint²` overall. Viewport/container-relative units and absolute units still route through the
already-correct existing paths (`CssValueParser.ParseLength`/`len.ToPixel() * pixelsPerPoint`
respectively).

## Deliberately not done

- **Not fixing `CssValueParser.ParseLength(Length,double,CssBox)`'s own em/rem/ex/ch bug.** That shared
  method is what `padding`/`width`/`border-radius`/margins/flex-grid gaps/etc. all resolve through — a
  correct fix belongs there so every caller benefits, but it's a much wider-reaching change (every one of
  those call sites needs its own regression coverage) than this gradient-scoped fix. Filed as
  [#826](https://github.com/jhaygood86/PeachPDF/issues/826); see
  `.claude/accepted-gaps/parselength-em-rem-ex-ch-scaling-under-non-default-pixelsperinch.md`.
- **Not fixing `@page` margin-box gradient `content`'s em/rem basis.** `MarginBoxRenderer.PaintImage`
  passes the document root box as `CssImagePainter.Paint`'s `box` parameter (there's no real `CssBox` for
  a margin box), previously inert for gradients since `em`/`rem` weren't consulted at all; this fix makes
  it live, so a margin box with its own `font-size` and an `em`-unit gradient stop/radius resolves against
  the *root's* font-size instead. Narrow in practice (needs that specific combination) and a real fix needs
  `CssImagePainter` to accept an already-resolved font-size directly for that one caller rather than a real
  `CssBox` it doesn't have. Filed as [#827](https://github.com/jhaygood86/PeachPDF/issues/827); see
  `.claude/accepted-gaps/margin-box-gradient-content-em-rem-basis.md`.
- `GetConicGradientBrush`/`NormalizeConicStops` untouched — conic stops resolve from `PositionRad` (an
  angle), never through `ConvertLength` or any `Length` value, so there's nothing to fix there (same
  conclusion the #821 fix note already reached).

## Evidence

- `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0` — full suite green (9209 tests).
- Diff coverage vs. `main`: 100% (`diff-cover`).
- `dotnet build PeachPDF.slnx -t:Rebuild` — 0 warnings.
- New `GradientRelativeLengthResolutionIntegrationTests.cs`: both issues' own repros verified against exact
  expected magnitudes (not just "doesn't throw"/"renders something"), plus DPI-invariance (72 vs. 96
  `PixelsPerInch`) for the newly-supported `em`/`rem`/`vw` radius and stop-position paths — the same
  convention `GradientPixelsPerInchIntegrationTests.cs` established for #821.
- Both issues' exact repro HTML rendered end-to-end via the `peachpdf` CLI and rasterized with both PDFium
  and MuPDF (this repo's two-renderer paint-verification convention) — #823's previously-crashing
  `radial-gradient(2em at center, red, blue)` renders a correctly-centered, correctly-sized circle in both
  renderers; #824's `linear-gradient(to right, red 0, blue 2em, green)` at `font-size:24px` shows the
  `blue` stop at the expected 24% position, not the pre-fix 16%.
