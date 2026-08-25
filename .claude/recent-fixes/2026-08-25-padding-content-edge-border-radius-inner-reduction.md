# Padding-/content-edge radius now subtracts border width and padding (issue #817)

## What was reported

[#817](https://github.com/jhaygood86/PeachPDF/issues/817) is the gap the
[#812 overflow-clip fix](2026-08-24-rounded-overflow-clip-and-joint-radius-reduction.md) deliberately
deferred: `background-clip: padding-box`/`content-box`, and the `overflow: hidden` descendant clip
curve it added, both curve their smaller (padding- or content-edge) rectangle using the box's raw,
declared `border-radius` — the same radius the border-box curve itself uses — instead of first
reducing it per [CSS Backgrounds and Borders Module Level 3 §5.5](https://www.w3.org/TR/css-backgrounds-3/#corner-clipping):
"the padding edge (inner border) radius is the outer border radius minus the corresponding border
thickness. In the case where this results in a negative value, the inner radius is zero." For a
thick border relative to its radius (`border: 6px solid; border-radius: 14px`), the inset curve
bulges past the border's own inner edge.

## What was actually done

`DerivedStyle.ComputeRadii(rect)` — the existing single-rect joint-overlap-reduction entry point —
was split into two pieces: the corner-overlap algorithm itself (extracted into a private static
`ApplyCornerOverlap`, unchanged in behavior) and a new `ComputeInnerRadii(borderBoxRect, innerRect,
insetLeft, insetTop, insetRight, insetBottom)` that composes with it rather than duplicating it:

1. Resolve the box's own declared radii against `borderBoxRect` via the existing `ComputeRadii` — the
   "outer" (used, border-box) radius, exactly what a border-box caller already gets.
2. Reduce each corner's X/Y component by the inset on its own adjacent edges — `insetLeft`/`insetRight`
   for a corner's X, `insetTop`/`insetBottom` for its Y (asymmetric border widths, e.g.
   `border-width: 2px 4px 6px 8px`, reduce each corner independently, not by one shared value) —
   clamped to zero with `Math.Max`.
3. Run `ApplyCornerOverlap` a *second* time against `innerRect`'s own (smaller) dimensions, since
   subtracting a constant width can still leave two adjacent reduced radii summing to more than a
   short inner edge (verified directly: `ComputeInnerRadii_ReducedRadiusStillOverlapping_AppliesCornerOverlapAgain`
   in `BorderRadiusIntegrationTests.cs`, a 100×20pt box whose 40pt declared radius is first
   overlap-clamped to 10pt against the border box, then reduced to 6pt by a 4pt border — small enough
   relative to the padding rect that this second pass is a no-op, proving the composition doesn't
   over-reduce when it doesn't need to).

Applied at all three call sites the accepted-gap note and #812's fix identified:
`FragmentPainter.Decorations.cs`'s `PaintBackground` (both the solid-`background-color` clip and the
per-layer image/gradient clip — refactored into one `ClipRadii(clipValue, clipRect)` local function so
`border-box` still calls the unmodified `ComputeRadii`, while `padding-box`/`content-box` route through
`ComputeInnerRadii` with the right inset — border widths alone for padding-box, border widths *plus*
padding for content-box), `RenderUtils.TryPushOverflowClip`, and `FragmentEmitter.OverflowClipOf`. The
last of these also let a now-redundant private `PaddingEdgeOf(CssBox, BoxGeometrySnapshot?)` helper be
deleted — `BoundsOf` and `RenderUtils.PaddingEdgeOf` are called directly instead, since the border-box
rect is now needed as its own value (for `ComputeInnerRadii`'s first argument), not just as an
intermediate on the way to the padding rect.

## What running it revealed vs. just reading it

Rendering the issue's own repro (`border: 6px solid; border-radius: 14px` with a padding-box
background, and separately with `overflow: hidden`) before this fix showed the bug much more starkly
than the written description suggests: a clearly visible white notch/gap at every corner, between the
border stroke's inner edge and the fill or clipped content it encloses — not a subtle rounding
difference. After the fix, the fill's curve sits flush against the border's own inner edge in both
cases, confirmed in both PDFium and MuPDF.

## What was deliberately not done

- No change to the border-box curve itself (`background-clip: border-box`, the border painter in
  `BordersDrawHandler.cs`) — CSS Backgrounds §5.5 applies only to the padding and content edges; a
  regression test (`BackgroundClip_BorderBox_CurveRadiusIsUnaffected`) guards against a future change
  to `ClipRadii` accidentally routing `border-box` through the inner-reduction path.
- No attempt to pre-derive the border-box's own overlap-reduced radius once and thread it through to
  avoid `ComputeInnerRadii` re-deriving it via `ComputeRadii(borderBoxRect)` — the per-call cost is one
  small arithmetic pass over eight cached `Actual*Radius*` properties, not worth the extra parameter
  plumbing across three call sites for a box that is rounded (the common case is unrounded, where none
  of this runs at all).

## Evidence

- Full suite: `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0` — 9233 passed, 0
  failed, 9 skipped (pre-existing platform-gated skips). No existing test's expected geometry needed
  updating — the suite's only border-radius-plus-`overflow:hidden` fixtures (`OverflowClipIntegrationTests.cs`)
  use boxes with no border, so the inset reduction is zero for all of them; `BorderRadiusIntegrationTests.cs`'s
  existing `ComputeRadii` tests call it directly against the border-box rect, a path this change leaves
  untouched.
- New tests: five direct `ComputeInnerRadii` cases in `BorderRadiusIntegrationTests.cs` (padding-edge
  subtraction, clamp-to-zero, content-edge subtracting border *and* padding, composition with a second
  corner-overlap pass, and asymmetric per-side border widths reducing each corner independently) plus a
  new `PaddingContentEdgeRadiusPaintIntegrationTests.cs` asserting the *actual painted* per-corner
  radii — extending the shared `RecordingGraphics`/`RecordingGraphicsPath` test double
  (`PeachPDF.Tests/TestSupport/RecordingGraphics.cs`) to capture `ArcTo`'s radius arguments and the
  `RGraphicsPath` instances passed to `DrawPath`/`PushClip(RGraphicsPath)`, so a test reads back the
  real curve a `background-clip`/`overflow:hidden` clip was built with rather than only checking that a
  path clip happened (this repo's own documented pitfall for anything touching PDF graphics state).
- Diff coverage vs. `main`: see the PR — reproduced locally with
  `dotnet test --collect:"XPlat Code Coverage" --settings PeachPDF.Tests/coverlet.runsettings --results-directory coverage`.
- `dotnet build PeachPDF.slnx -t:Rebuild` — 0 warnings.
- TestHarness `border_radius` showcase extended with an "8 — Padding/Content-Edge Radius Reduction
  (thick border)" section (`border: 6px solid; border-radius: 14px`) demonstrating both
  `background-clip: padding-box` and `overflow: hidden`; rasterized with both PDFium and MuPDF and
  visually confirmed the inset curve now sits at the reduced (8px) radius in both.
- The issue's own repro (both the `background-clip: padding-box` case and the `overflow: hidden` case)
  was rendered via `PeachPDF.Cli` before and after the fix and rasterized with both PDFium and MuPDF:
  all four "before" renders show the white corner notch, all four "after" renders show the fill/clipped
  content flush against the border's inner edge, with both renderers agreeing at each step.

## Traps for a future change

- `ComputeInnerRadii` calls `ComputeRadii(borderBoxRect)` internally to get the "outer" (used)
  radius before subtracting the inset — a future change to `ComputeRadii`'s own algorithm
  automatically flows through here too, which is the point of composing rather than duplicating; don't
  reintroduce a second, independent radius derivation for the inner case.
- The inset passed to `ComputeInnerRadii` is a *pair* of per-axis values reused across two adjacent
  corners each (`insetLeft` feeds both TLX and BLX, `insetTop` feeds both TLY and TRY, etc.), not a
  single scalar — a caller with genuinely asymmetric border widths (`border-width: 2px 4px 6px 8px`)
  must pass all four, not the box's uniform `ActualBorderTopWidth` alone; `RenderUtils.TryPushOverflowClip`
  and `FragmentEmitter.OverflowClipOf` both already do this correctly (all four `Actual*Width` values),
  as does content-box's inset in `FragmentPainter.Decorations.cs` (`ActualBorder*Width +
  ActualPadding*`, per side).
