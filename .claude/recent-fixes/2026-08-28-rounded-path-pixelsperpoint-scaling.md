# Box-geometry paths (rounded corners, clip-path, box-shadow rings) ignore non-default PixelsPerInch (issue #812, reopened)

## What was reported

The original [#812 fix](2026-08-24-rounded-overflow-clip-and-joint-radius-reduction.md) (`b41a6671`,
`425247bd`) addressed two symptoms - a rounded progress-bar's clipped child rendering solid with a
duplicated "ghost" shape, and a rounded card's own border "leaking" past its box - and was verified
extensively (unit tests, integration tests, both PDFium and MuPDF rasterization) at the library's default
`PdfGenerateConfig.PixelsPerInch` of 72. The reporter re-tested the released fix using their production
config, `PixelsPerInch = 96`, and both symptoms were back, plus a new one: text inside a box with both
`border-radius` and `overflow: hidden` could vanish entirely.

## What was actually wrong

`PdfSharpAdapter.PixelsPerPoint = PixelsPerInch / 72` inflates the entire internal CSS layout coordinate
space relative to true PDF points, and every `GraphicsAdapter` draw primitive (`DrawLine`, `DrawRectangle`,
`PushClip(RRect)`) is individually responsible for dividing its own raw coordinates back down before
calling into the PdfSharpCore/XGraphics backend. At `PixelsPerInch = 72` (`PixelsPerPoint = 1.0`) a missed
division is invisible - exactly why the original fix's own thorough (both renderers, both repro shapes)
verification never caught this, since it predates the very commit that made absolute lengths (like
`border-radius: 999px`) correctly `PixelsPerPoint`-scaled in the first place
([2026-08-24-pixelsperinch-absolute-length-scaling.md](2026-08-24-pixelsperinch-absolute-length-scaling.md),
landed later the same day).

**Four independent code paths** build `RGraphicsPath` box geometry directly from raw, un-divided
layout-space coordinates, and neither the path-building code nor its consumers
(`GraphicsAdapter.PushClip(RGraphicsPath)`/`DrawPath`) ever divided by `PixelsPerPoint`:

1. `RenderUtils.GetRoundRect` - shared by the `overflow: hidden` descendant clip curve, rounded
   background-color/image fills (including `background-clip: padding-box`/`content-box` curves and
   box-shadow's rounded layer/clip helper), and rounded form-field/list-marker chrome.
2. `BordersDrawHandler.GetRoundedBorderPath` - a **separate, independent** per-side path builder for the
   rounded border **stroke** itself, sharing no code with `GetRoundRect`. This is what let symptom B (the
   border overshoot) reproduce from `border-radius` alone, with no `overflow: hidden` at all - a
   minimal repro built from the reporter's own follow-up comments (a rounded card containing a header and
   a 3-column CSS Grid) confirmed this: removing just `overflow: hidden` and keeping `border-radius` still
   showed the border overshooting past the card's actual content, into unrelated text below.
3. `CssClipPathResolver.TryBuildClipPath` (and its `BuildPolygon`/`BuildInset`/`BuildCircle`/`BuildEllipse`
   helpers) - resolves `clip-path: polygon()/inset()/circle()/ellipse()` against the element's border-box,
   pushed via the same un-dividing `PushClip(RGraphicsPath)`. Found during this fix's own post-change
   review (three independent review passes converged on it), not in the original report; confirmed with a
   `clip-path: circle(50%)` repro that rendered as a mangled quarter-shape at `PixelsPerInch = 96` instead
   of a full circle.
4. `FragmentPainter.Decorations.BuildRingPath` - the concentric even-odd ring fills that approximate a
   *blurred inset* `box-shadow`'s falloff (drawn via `DrawPath`, not clipped). Also found during post-change
   review; confirmed with an `inset 0 0 20px` shadow repro whose ring geometry visibly detached from the
   box at `PixelsPerInch = 96` instead of framing it symmetrically. Note `BuildLayerRoundRect` - the
   *outset*-shadow and inset-shadow-*clip* path builder a few lines away in the same file - was already
   correct, since it's a thin wrapper around the now-fixed `RenderUtils.GetRoundRect` (item 1); only the
   ring-fill geometry was its own, separately un-divided code.

`RGraphicsPath` has a second, legitimate un-divided-by-design consumer - SVG shape rendering and
glyph/COLR outline painting - which always paints under an active `g.PushTransform(...)` whose own linear
part performs the layout→point conversion (the same pattern the absolute-length fix established for SVG's
viewport transform). Dividing inside the shared `GraphicsPathAdapter`/`PushClip`/`DrawPath` would have
double-scaled every SVG shape and glyph outline while fixing the four box-geometry consumers above; fixing
at each path-*building* call site was the correct, fully-scoped fix. A full sweep of every
`GetGraphicsPath()` call site in the codebase (`grep -rn "\.GetGraphicsPath()"`) after the fact confirmed
no fifth consumer was missed.

## What was actually done

All four builders above now read `g.PixelsPerPoint` once and divide their rect/radii/length/border-width
inputs by it before building the path - mirroring exactly how every other `GraphicsAdapter` draw primitive
divides its own raw coordinates at its own call boundary:

- `RenderUtils.GetRoundRect` and `BordersDrawHandler.GetRoundedBorderPath`: divide the rect, radii, and
  (for the latter) border widths directly. `GetRoundedBorderPath`'s fix is a mechanical rename-and-divide
  of the existing per-side arc/line arithmetic; the corner-subset/mitre logic itself (the
  `noTop`/`noBottom` bevel-avoidance branches) is unchanged.
- `CssClipPathResolver`: divides only the *final*, fully-resolved coordinate at each `path.Start`/`LineTo`/
  `AddMove`/`AppendEllipse` call, leaving every upstream `CssValueParser.ParseLength` call (and the
  `referenceBox` it resolves percentages against) completely untouched. This is deliberate, not just
  simpler: `ParseLength`'s absolute-length branch already applies its own `PixelsPerPoint` catch-up
  multiply (per issue #814) independent of the percentage basis passed in, so pre-dividing
  `referenceBox.Width`/`.Height` before resolution would correctly scale a *percentage* clip-path point but
  leave an *absolute-length* one (`polygon(10px 10px, ...)`) still un-divided - verified by hand-deriving
  both cases algebraically before choosing this approach.
- `FragmentPainter.Decorations.BuildRingPath`: divides `outer`/`hole`'s four edges directly.

## What was deliberately not done

- **Border stroke *width* (not position)** - `BordersDrawHandler.GetWidth`/`GetPen` sets a pen's `Width`
  from an equally un-divided value, a real, pre-existing, separate bug (wrong stroke *thickness*, not
  position/shape, and not specific to rounded corners - confirmed visually in the `border_radius_96dpi`
  TestHarness showcase this fix adds, where every swatch's border renders visibly bolder at
  `PixelsPerInch = 96` even though every curve's position/size is now correct). Tracked as
  [issue #851](https://github.com/jhaygood86/PeachPDF/issues/851); see
  [.claude/accepted-gaps/rounded-border-stroke-width-pixelsperpoint.md](../accepted-gaps/rounded-border-stroke-width-pixelsperpoint.md).
- **Box-shadow blur approximation band *count*** - `FragmentPainter.Decorations.BlurSteps` reads the
  still-layout-space `blur` value directly, so the number of concentric ring/rect fills approximating a
  blur's falloff scales with `PixelsPerInch` (20 rings at 72, 40 at 144, for the same declared blur radius)
  even though - after this fix - each ring's own position/size is correct. A rendering-*quality* difference,
  not a wrong-position one; found while writing this fix's own `BuildRingPath` regression test (an earlier
  draft compared ring counts across two `PixelsPerInch` values and spuriously failed on this unrelated
  difference before being redesigned to assert bounds directly). Tracked as
  [issue #852](https://github.com/jhaygood86/PeachPDF/issues/852); see
  [.claude/accepted-gaps/box-shadow-blur-steps-pixelsperpoint.md](../accepted-gaps/box-shadow-blur-steps-pixelsperpoint.md).
- **A pre-existing, independent typo** in `GetRoundedBorderPath`'s `Border.Right` case: its top-right arc
  endpoint offsets by `ActualBorderLeftWidth` where the surrounding, symmetric code all uses the edge's own
  width (`ActualBorderRightWidth`) - would misplace the top-right corner whenever left and right border
  widths differ. Unrelated to `PixelsPerPoint`, kept exactly as-is (just correctly scaled) rather than
  fixed alongside, to keep this change to one concern. Tracked as
  [issue #853](https://github.com/jhaygood86/PeachPDF/issues/853).
- The reporter's third, no-minimal-repro-found symptom ("`<dt>` labels in the first metrics row disappear")
  was not independently reproduced. Plausible under this same root cause (a sufficiently mis-scaled clip
  path can fail to overlap real content at all, matching the vanished-text symptom that *was* reproduced),
  but not chased as a separate fix.

## Evidence

- Full suite: `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0` - 9286 passed, 0
  failed, 9 skipped (pre-existing platform-gated skips).
- New tests verified to fail against the pre-fix source (stashed each fix's files individually, reran,
  every discriminating fixture failed with values matching exactly the predicted un-divided-by-`ppp`
  magnitude, then restored): `RenderUtilsTests.GetRoundRect_DividesRectAndRadiiByPixelsPerPoint`/
  `_IsInvariantUnderPixelsPerPoint`, `RoundedBorderStrokePixelsPerPointIntegrationTests`'s two facts,
  `PaddingContentEdgeRadiusPaintIntegrationTests`'s three new facts (background-clip curve invariance,
  overflow-clip curve invariance, and the issue's own pill-shape repro staying within bounds),
  `ClipPathResolverIntegrationTests`'s three new facts (polygon/inset/circle), and
  `BoxShadowPaintIntegrationTests.BlurredInsetShadow_RingBounds_StayWithinPaddingBoxUnderNonDefaultPixelsPerInch`.
- A full `grep -rn "\.GetGraphicsPath()"` sweep across `src/PeachPDF` after all four fixes confirmed every
  box-geometry consumer is now covered and every SVG/glyph-outline consumer is correctly left untouched.
- Diff coverage vs. `main`: 100% on changed source lines.
- `dotnet build PeachPDF.slnx -t:Rebuild` - 0 warnings.
- The issue's own two repros, a grid-card variant built from the reporter's follow-up comments, a
  `clip-path: circle(50%)` repro, and an `inset 0 0 20px` box-shadow repro, all re-rendered via a direct
  `PdfGenerator.GeneratePdf` call (not the CLI, which never sets `PixelsPerInch` and defaults to 72 - part
  of why the original fix's own CLI-based verification never caught this) at `PixelsPerInch = 96`, then
  rasterized with both PDFium and MuPDF and pixel-diffed against the `PixelsPerInch = 72` render of the
  same HTML: all five showed real, visible differences before their respective fix (vanished fill, border
  overshoot into unrelated content, a mangled clip shape, a detached shadow ring) and were pixel-identical
  (modulo sub-pixel anti-aliasing noise) after.
- New `border_radius_96dpi` TestHarness showcase, rendered and rasterized with both PDFium and MuPDF:
  every rounded curve's position/size and every overflow-clip boundary match the default-72 showcase
  exactly; only the (separately tracked, #851) border stroke thickness differs.

## Traps for a future change

- **This bug class needed re-finding twice within one fix.** The first pass (items 1-2 above) fixed the
  two builders the original report actually exercised; a post-change review pass (three independent review
  agents, each searching the codebase from a different angle) found two more sibling instances (items 3-4)
  that the same root-cause description already implied but the fix hadn't reached. Before declaring a
  future `PixelsPerPoint`-scaling fix complete, grep every `GetGraphicsPath()` call site in the codebase
  (not just the ones a specific bug report's repro happens to exercise) and verify each one either divides
  by `PixelsPerPoint` itself or paints under an ambient `PushTransform` that already does.
- `RenderUtils.GetRoundRect`, `BordersDrawHandler.GetRoundedBorderPath`, `CssClipPathResolver`, and
  `FragmentPainter.Decorations.BuildRingPath` are four independent path builders that happen to solve
  variations of the same problem - a future change to one's `PixelsPerPoint` handling (or any other
  coordinate-space concern) does **not** automatically apply to the others.
- `RGraphicsPath`/`GraphicsPathAdapter` deliberately performs no `PixelsPerPoint` scaling anywhere - it's a
  dumb coordinate carrier, and the *caller* is always responsible for pre-dividing before building a path,
  exactly like every other `RGraphics` draw primitive divides at its own call boundary. A future new
  consumer of `GetGraphicsPath()` needs to make its own call on which space its coordinates start in (SVG
  and glyph-outline paths are pre-divided a different way, via an ambient `PushTransform`) and divide
  accordingly - there is no single correct default to fall back on.
- `CssClipPathResolver`'s approach (divide only the final resolved coordinate, never the inputs to
  `CssValueParser.ParseLength`) is the correct pattern for any similarly-structured future fix: `ParseLength`
  already applies its own `PixelsPerPoint` catch-up for absolute lengths regardless of what percentage
  basis it's given, so pre-dividing a shared "reference" value used for *both* percentage resolution and
  absolute-length composition silently miscales the absolute-length case. Divide once, at the very end.
