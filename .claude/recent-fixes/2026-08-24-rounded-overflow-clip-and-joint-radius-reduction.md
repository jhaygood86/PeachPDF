# Rounded overflow clip + joint radius reduction (issue #812)

## What was reported

[#812](https://github.com/jhaygood86/PeachPDF/issues/812) described two symptoms from `overflow: hidden`
+ `border-radius`: (A) a pill-shaped progress-bar track (`border-radius: 999px`, 6px tall) whose
green `.fill` child rendered as a solid rectangle covering the whole bar, with an extra "ghost"
peach shape reported elsewhere on the page; (B) a rounded bordered card whose own left border
allegedly "leaked" as a stray vertical line down through unrelated content below it.

## What was actually wrong

Static reading (see the two Explore-agent passes referenced in this fix's PR) correctly identified
a real, confirmed gap: `BoxFragment.OverflowClip` is a plain `RRect`, and every place that pushes it
(`RenderUtils.ClipGraphicsByOverflow`/`TryPushOverflowClip`) pushes only a rectangular clip, never a
rounded one — so a rounded `overflow: hidden` box never actually clips descendant content to its
curve. That gap is real and is fixed here (`BoxFragment.OverflowClipCurve`, populated in
`FragmentEmitter.OverflowClipOf`/`ClipOf`, pushed as an additional `RGraphicsPath` clip in
`RenderUtils.ClipGraphicsByOverflow`/`TryPushOverflowClip`).

But actually rendering the issue's exact repro (Step 0 of the plan, done after exiting plan mode)
showed that gap was NOT the primary driver of symptom A's dramatic visual wrongness. The real
primary cause was in `DerivedStyle.ComputeRadii`: when a `border-radius` overlaps on both axes of a
box (999px radius on a 6px-tall, ~50pt-wide track), the horizontal and vertical reduction factors
were computed and applied **independently** — `fX` from the width-driven edges, `fY` from the
height-driven edges — rather than as one joint factor per
[CSS Backgrounds and Borders Level 3's corner-overlap algorithm](https://www.w3.org/TR/css-backgrounds-3/#corner-overlap).
For the track, that meant the X radius clamped only to half the box's *width* (~25pt) while the Y
radius clamped to half its *height* (~2.25pt) — an extremely flat quarter-ellipse per corner,
which is what rendered as a pointed cusp instead of a semicircular pill cap. Fixed by computing one
`f = min` across all four edges and applying it to every radius component, matching the spec exactly.

The rectangular-clip gap was still real and visible once the radius bug was fixed alone: with
correct (small, circular) radii, the `.fill` child's own square corners became visibly wrong against
the now-correctly-rounded track. Both fixes together reproduce the issue's exact "Expected" output
(confirmed by re-rendering and rasterizing with both PDFium and MuPDF).

## What could not be reproduced

Symptom B (the "leaking border") did not reproduce with the issue's exact minimal repro on this
branch's base commit, in either renderer, as a single-page document. The repro's total content
height fits on one page, so no page-break/fragmentation code path is exercised — and this repo's
`FragmentEmitter` displacement/confinement machinery (the other candidate root cause considered) is
specifically about page-break-adjacent displacement, which the repro never reaches. Since the fix
for the confirmed gap (rectangular-only clip) uses the same clip-application code paths symptom B's
report describes, and no independent leak mechanism could be found or reproduced, this fix does not
add a separate change for symptom B. If it resurfaces, it needs a repro that actually spans a page
break to be actionable.

## Evidence

- Full suite: `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0` — 9188 passed, 0
  failed, 9 skipped (pre-existing platform-gated skips).
- Diff coverage vs. `main`: 100% (59/59 changed lines).
- `dotnet build PeachPDF.slnx -t:Rebuild` — 0 warnings.
- TestHarness `border_radius` showcase extended with a "Clipping to the Rounded Curve" section
  mirroring both of the issue's repro shapes; rasterized and visually confirmed correct.
- Both issue repros re-rendered via `PeachPDF.Cli` and rasterized with PDFium and MuPDF before and
  after the fix; symptom A's output now matches the issue's stated "Expected" exactly in both
  renderers.

## Traps for a future change

- `RenderUtils.GetRoundRect` never called `path.CloseFigure()` — harmless for the fills it was
  originally used for (PDF implicitly closes a subpath before filling), but now also feeds a clip
  push, where an unclosed subpath is a correctness risk depending on the backend. Fixed alongside;
  any future path builder reused for clipping should close explicitly rather than relying on a
  fill-only guarantee.
- `RenderUtils.TryPushOverflowClip` still reads live `CssBox` geometry rather than the fragment tree
  (a pre-existing, separately documented gap — see its own remarks) — this fix extends it
  consistently with the same rounded-clip treatment rather than also fixing that, to keep this
  change scoped to the reported issue.
- A post-change review pass turned up a real, pre-existing spec deviation this fix's new clip curve
  deliberately inherited rather than fixed: the padding-edge curve should use `border-radius -
  border-width` (CSS Backgrounds and Borders Level 3 §5.5), not the raw border-box radius applied to
  the smaller inset rect — `background-clip: padding-box`/`content-box` already had this gap before
  this change; fixing only the new overflow-clip curve would make a box's background and its content
  clip visibly disagree with each other. Tracked as issue #817 and closed by
  [2026-08-25-padding-content-edge-border-radius-inner-reduction.md](2026-08-25-padding-content-edge-border-radius-inner-reduction.md).
- The review pass also found (and this fix applied) one duplication worth knowing before touching
  `RenderUtils.cs` again: `ClipGraphicsByOverflow` and `TryPushOverflowClip` both need to "build a
  rounded path from `BorderRadii`, push it, count it" — factored into a shared private
  `PushRoundedClipIfRounded(g, rect, radii)` rather than writing the same five lines twice.
