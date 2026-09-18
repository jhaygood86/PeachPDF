# RGraphicsPath.ClipToRect and closing the upright-run/real-vertical-metrics background-clip:text gap

Closes issue #1194 - the accepted gap recorded during #1123's own review (see
`2026-09-18-vertical-writing-mode-text-clip-skip-ink-and-underline-side.md`).

## The load-bearing idea

`RGraphicsPath` needed a way to intersect an arbitrary (line- and cubic-Bézier-segment) path with an
axis-aligned rectangle so `FragmentPainter.Decorations.cs`'s `CollectUprightWord` could confine a real-
vertical-metrics character's *clip-path outline* to the exact same cell `PaintUprightVerticalRun` already
confines that character's *paint* to (`PushClip`/`PopClip` around each `DrawString`). A full polygon-
boolean primitive was never needed - only "clip this path to this rectangle" - so the new
`RGraphicsPath.ClipToRect(RRect)` is implemented as: flatten every subpath's curves to line segments
(`BezierFlattener`, recursive de Casteljau subdivision with a 0.1pt flatness tolerance), then clip each
flattened contour independently against the rectangle's four half-planes via Sutherland-Hodgman
(`SutherlandHodgman.ClipToRect`). Sutherland-Hodgman is normally described for convex *subject* polygons,
but the property actually being relied on is that the *clip window* is convex - clipping any contour
(convex, concave, or a glyph's self-intersecting outline) against a convex rectangle can't introduce a new
self-intersection the algorithm isn't equipped for, so each contour of a multi-contour, nonzero-wound
glyph outline can be clipped independently and the results concatenated as disjoint subpaths under the
same fill mode - no general two-arbitrary-polygon boolean required. The geometry lives in
`CoreGraphicsPath`/`BezierFlattener`/`SutherlandHodgman` (`PdfSharpCore/Drawing`); `GraphicsPathAdapter`
exposes it through the abstract `RGraphicsPath.ClipToRect`.

With the primitive in place, `CollectUprightWord`'s `HasVerticalMetrics || HasVerticalOrigin` branch no
longer sets `anyUnsupportedRun` - it now clips the character's `GetTextOutline` result to
`new RRect(rect.X, placement.CellTop, rect.Width, placement.Advance)` (the identical rect
`PaintUprightVerticalRun` clips *paint* to) before unioning it in.

## What was found by running it, not by reading it

A degenerate case the point-count check alone doesn't catch: clipping against a zero-width (or
zero-height) rectangle, or a subject edge that runs exactly along a clip boundary, can leave 3+ points
that are all collinear - still zero area, but past the naive "`Count < 3`" filter. Added a shoelace-
formula area check (`CoreGraphicsPath.IsNegligibleArea`) alongside the point-count check so a degenerate
clip rectangle can never fabricate a visible sliver of geometry from nothing. Caught by a synthetic
`ZeroWidthRect_ProducesNoUsableGeometry` test, not by reasoning about the algorithm - the real call sites
(a glyph's own advance/cell width) never hit this in practice, but the primitive itself needed to be
correct independent of how narrowly it happens to be used today.

A post-change review pass (this repo's own convention) also caught a real, if currently dormant, bug:
`GraphicsPathAdapter`'s private wrapping constructor (the one `ClipToRect` uses to hand back its result)
never initialized `_lastPoint`, leaving it at the implicit `(0, 0)` instead of the clipped path's actual
last vertex. `CollectUprightWord`'s own usage never triggers it (it only ever calls `AddPath` on a clipped
result, which merges point arrays directly and never reads `_lastPoint`), but `RGraphicsPath.ClipToRect`
is a public adapter-boundary member - any future caller extending the returned path with `LineTo`/
`ArcTo`/`AddBezierTo` would have silently drawn from the origin instead of the path's real endpoint.
Fixed by seeding `_lastPoint` from the wrapped `XGraphicsPath`'s own last point in that constructor, with
a regression test (`ClippedPath_ContinuingToBuildOntoIt_StartsFromItsOwnLastPoint`) that fails against the
unfixed constructor.

## What was deliberately not done, and why

- **No curve-preserving clip.** `ClipToRect` flattens every curve before clipping - an axis-aligned
  rectangle clip of a cubic Bézier isn't itself expressible as a cubic Bézier in general, and sub-point
  flattening deviation is invisible for a filled text-clip shape. Not intended as a general-purpose
  curve-preserving path clip for other callers.
- **No general polygon-boolean intersection between two arbitrary paths.** Only one side of the operation
  needs to be a rectangle for every current and foreseeable caller (a reserved glyph cell); building a
  general two-polygon boolean would be substantially more code and risk for a capability nothing needs
  yet.

## Evidence

`PeachPDF.Tests` full suite (net8.0) green. New `GraphicsPathRectClipTests` (8 cases: fully inside, fully
outside, straddling a boundary, clip-rect-fully-inside-subject, a curved contour actually crossing the
boundary, multi-contour independence, an empty path, and the zero-width-rect degenerate case) exercise the
real `GraphicsPathAdapter`/`CoreGraphicsPath` implementation directly, not a mock. Rewrote
`BackgroundClipTextIntegrationTests.UprightRun_WithRealVerticalMetrics_ClipsUnionToReservedCell` (previously
the fallback-to-border-box regression test for this exact case) to assert the clipped union's geometry
never exceeds the character's own word rect, using `RecordingGraphicsPath`'s own `ClipToRect` override -
which now runs the real `SutherlandHodgman` algorithm against its recorded points rather than being a
no-op double, so the test exercises genuine clip geometry. The pre-existing outline-less-font and
sideways-writing-mode fallback tests are unchanged and still pass. PDFium and MuPDF rasterization of the
real end-to-end pipeline agree pixel-for-pixel that the background now clips tightly to glyph ink under
`vertical-rl`/`text-orientation: upright` with a real-vmtx CJK font, rather than filling the whole
border-box.
