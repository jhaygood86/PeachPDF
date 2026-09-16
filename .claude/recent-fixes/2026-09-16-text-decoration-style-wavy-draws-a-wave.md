# `text-decoration-style: wavy` draws an actual wave

`TextDecorationStyleMapper` mapped `wavy` to `RDashStyle.Solid` because no dash pattern can express a
wave — a decoration was stroked with `RGraphics.DrawLine`, which takes only a pen and two endpoints —
so `wavy` painted identically to `solid`. Tracked as issue #1114, with
`.claude/accepted-gaps/text-decoration-style-wavy-paints-solid.md` (now deleted) pointing here.
css-text-decor-3 §2.2 defines `wavy` in its own words ("Draw a wavy line") rather than by
cross-reference to `border-style` the way every other style is, so this was an unambiguous spec
deviation, not a discretionary gap like `double`'s geometry.

## The load-bearing idea

A wave is built as an `RGraphicsPath` of chained cubic Béziers — one full period per curve, both
control points at the period's horizontal midpoint, offset ± a control-point distance from the
centerline (the same shape Blink's `DecorationLinePainter::MakeWave` and WebKit's
`wavyStrokeParameters` use; Gecko instead draws an angular straight-line zigzag, a worse fit for PDF
vector output). It is stroked with `DrawPath(RPen, RGraphicsPath)` instead of `DrawLine`, in a new
shared `WavyDecorationRenderer` (`Html/Core/Utils`) — shared because `Svg/SvgRenderer.cs`'s
`DrawDecorationSpan` consults the same `TextDecorationStyleMapper` and the issue named it directly as
"a second caller to consider"; building a second wave-path implementation there would have been exactly
the kind of same-geometry-across-layers duplication this repo's architecture conventions rule out.

## The coordinate-space question the accepted-gap doc flagged

`PaintDecoration`/`StrokeDecorationSegment` work in raw, undivided layout-space pixels; `DrawLine`'s own
backend implementation divides by `PixelsPerPoint` before reaching PdfSharp, so callers never divide
themselves. `DrawPath`/`RGraphicsPath` never do that automatically — confirmed by reading
`GraphicsAdapter.DrawPath` (passes path coordinates straight through) and by `RenderUtils.GetRoundRect`'s
own remarks (issue #812): a box-geometry path has no ambient transform to divide it back down. So
`WavyDecorationRenderer` divides every path coordinate, and the stroking pen's own width, by
`g.PixelsPerPoint` itself — the same convention `GetRoundRect`/`FragmentPainter.BuildRingPath` already
use — rather than reusing the shared decoration `pen`, which stays in undivided units for the other
styles' `DrawLine` calls.

Verified with a dedicated test that runs layout and paint at a non-default `PixelsPerInch` (mirroring
`RoundedBorderStrokePixelsPerPointIntegrationTests`): the stroke width comes out as the declared `4pt`,
not `4pt × PixelsPerPoint`, and every recorded path point lands within the word's own (divided) rectangle
plus one wavelength of the clip-trimmed overshoot.

## Phase: per-segment, deliberately not per-line

Blink and Gecko anchor wave phase to an absolute line/block origin so segments split by an unrelated
inline span stay in phase; WebKit resets phase per segment, a documented interop bug there. This
implementation resets phase per segment on purpose, matching what the issue text itself specified:
`DecorationSegments.Subtract` already produces independent segments for `text-decoration-skip-ink` gaps
and atomic-inline exclusions, each already visually separated by a real gap, so restarting phase at each
segment's own start keeps every segment's wave beginning and ending at a zero-crossing rather than
stranding an arbitrary partial wave at a gap's edge. Verified by rendering `"Typography judging a quick
pig"` with `text-decoration-skip-ink: auto` (the default) — the wave visibly breaks clean around every
descender in both a PDFium and a MuPDF rasterization.

## Geometry constants — chosen, not measured bit-for-bit

For a curve built this way the actual peak amplitude works out to
`3 · controlPointDistance · t(1−t)(1−2t)`, maximized at `t = ½ − √3⁄6 ≈ 0.211`, giving peak ≈
`0.289 × controlPointDistance`. Chosen: `controlPointDistance = 4 × thickness` (peak ≈ `1.15 ×
thickness`, total vertical span ≈ `2.3 × thickness`, close to the issue's own Chrome 141 reference of
"~2.5× the resolved thickness"), `wavelength = 4.5 × thickness`, centerline offset `1 × thickness` in the
same away-from-text direction `double`'s second stroke already grows. All relative to thickness, not a
physical-pixel constant the way Blink's own `+1` terms are, since `PixelsPerPoint` can differ from 1.
No test pins an exact pixel amplitude — browsers do not render an identical wave either, and
css-text-decor-3 leaves the shape entirely UA-defined.

## Found by review, not by reading: a shared cached pen corrupted a later keyword's thickness

`RAdapter.GetPen(RColor)` returns a **cached, mutable** `RPen` keyed only by color, reset to defaults
and reconfigured by whichever caller asks for it next. `PaintDecoration` gets the pen once per box, sets
its `Width` to the resolved thickness, then reads `pen.Width` back per keyword/segment (for
`ResolveAutomaticUnderlineClearance`, `AddInkExclusions`, and — the new one — the wavy stroke's own
thickness argument). `WavyDecorationRenderer.StrokeWavyLine` itself calls `g.GetPen(color)` for the
*same* color to get a pen to `DrawPath` with — the *same cached instance* — and sets its `Width` to the
already-divided `thickness / PixelsPerPoint`. For a single-keyword, single-segment wavy decoration this
is invisible (the pen is never read again). It stopped being invisible for `text-decoration: underline
overline wavy`: the underline keyword's `StrokeWavyLine` call divided the pen down, and the overline
keyword then read that already-divided value back out as *its own* thickness — reproduced with a
two-keyword wavy decoration at `PixelsPerPoint = 2.0`: first stroke came out at the correct `4`, second
at `2`.

The fix is not "restore the pen afterward" (fragile, and there could always be another mutator later) —
it's to stop treating the mutable pen as a source of truth for a value already known as a plain number.
`PaintDecoration` now resolves `thickness` into a local `double` once, uses that everywhere a keyword's
computation previously read `pen.Width`, and threads it through `StrokeDecorationSegment` explicitly.
The pen itself is now used for exactly what it's for: carrying color/dash-style into `DrawLine`/`DrawPath`,
never as a smuggled-thickness channel.

Whether this same class of bug could recur for `double` specifically: no — `text-decoration-style`
is one value for the whole box, so a box can never mix `wavy` (the only style that re-enters `GetPen`
mid-loop) with `double`/solid/dotted/dashed (which only read the pen, never re-fetch it) in the same
`PaintDecoration` call. The corruption is only reachable through a *second wavy call* sharing the first's
color, i.e. two-or-more wavy keywords/segments on one box — exactly the skip-ink and multi-keyword
cases this feature is otherwise built to handle correctly.

## Also found by review: the geometry floor was leaking into the stroke width

`MinimumThickness` floors the *wave's own proportions* (wavelength, control-point distance, centerline
offset) so a declared `text-decoration-thickness: 0` can't zero the wavelength and hang the per-period
loop (`x += wavelength` never advancing). The first draft applied that same floored value to the
stroking pen's `Width` too, so `text-decoration-thickness: 0.1pt` rendered a stroke five times thicker
than declared — not what `solid`/`double` do with the same declaration. Fixed by keeping the floor for
geometry only; the pen always strokes at the true, un-clamped thickness. A `0.1pt` case now asserts the
recorded stroke width is exactly `0.1`, and a `0` case is its own regression guard: it would never
complete if the floor were removed.

## Deliberately not done

- SVG's `double` remains a single stroke — a separate, pre-existing, already-documented gap this change
  did not touch. `wavy` in SVG uses the same `WavyDecorationRenderer` HTML does.

## Evidence

- `TextDecorationWavyStyleTests`, 10 cases: strokes a path not a line, one per decoration-line keyword,
  the `PixelsPerPoint` division (path and pen), growth direction, skip-ink/atomic-inline segmentation,
  the shared-pen-corruption regression (two wavy keywords at non-default `PixelsPerInch`), and the
  zero/near-zero thickness cases.
- `SvgTextDecorationTests.WavyStyle_StrokesAPath_NotALine`.
- `TextDecorationDoubleStyleTests.OtherStyles_AreStillASingleStroke` no longer pins `wavy → Solid`
  (removed, since it is no longer true).
- Rendered a real PDF (`underline`/`overline`/`line-through` wavy, a heavier thickness, and a
  skip-ink case) and rasterized it with both PDFium and MuPDF at 200dpi — both renderers agree
  pixel-for-pixel (a plain vector stroke has no transparency/soft-mask conformance gap between them to
  disagree over), and the wave breaks around descenders as intended. Re-rendered and re-rasterized after
  both review fixes above: byte-for-byte identical output at the default `PixelsPerInch`, confirming
  neither fix touched the common case.
- Full suite on net8.0: all passing, twice (before and after the review fixes). `dotnet build
  -t:Rebuild`: 0 warnings. Diff coverage: 100%.
