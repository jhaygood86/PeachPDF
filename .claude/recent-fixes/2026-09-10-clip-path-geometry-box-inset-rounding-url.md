# `clip-path`: `<geometry-box>`, `inset()` rounding/validation/clamping, and `url(#id)`

Closes [issue #217](https://github.com/jhaygood86/PeachPDF/issues/217) - the last three of its four
tracked gaps (`path()` and `calc()` in a shape component were already closed on `main`).

**inset() rounding, validation, clamping (`BasicShapeGrammar.ParseInset`/`CssClipPathResolver.BuildInset`):**
the round-radius tokens are now parsed as a real `<border-radius>` (1–4 values, optional `/`-split
vertical half, non-negative literals) instead of only checking they're non-empty - an invalid one
(`inset(10px round banana)`) now invalidates the whole declaration, per spec, instead of silently
drawing a sharp rectangle. Rendering reuses `RenderUtils.GetRoundRect` (already existed, built for
`border-radius` itself) and `DerivedStyle.ApplyCornerOverlap` (changed from `private` to `internal`
so the resolver can call the exact same CSS Backgrounds 3 §4 reduction algorithm rather than
re-deriving it) for the corner-overlap reduction - one algorithm, two callers, per this repo's
"one grammar" convention. Over-constrained edges (`left+right > width`, or the vertical equivalent)
are now proportionally reduced per CSS Shapes 1 §3.1, in the same `BuildInset` pass.

**`<geometry-box>` (`BasicShapeGrammar.TryParse`/`CssClipPathResolver.ReferenceBoxFor`):** the
grammar dispatch now scans for a standalone geometry-box keyword anywhere among the top-level
tokens (before or after the shape function, or alone with no shape at all), leaving the rest to
dispatch exactly as before. `fill-box`/`stroke-box`/`view-box` deliberately alias border-box - not a
narrowing, but CSS Masking 1 §7's own specified fallback for an element with no SVG bounding box,
which every `CssBox` is.

**`url(#id)` (new `SvgClipPathRegistry`, `SvgTreeBuilder`, `SvgRenderer.BuildClipPath`):** the
load-bearing finding here was that `SvgDocument.ClipPaths` was populated *lazily* - only when
something inside the *same* `<svg>` subtree referenced a `<clipPath>` via its own
`clip-path: url(#id)` (`SvgTreeBuilder.ApplyCommon` → `ResolveClipPath`). An HTML element's
`clip-path: url(#id)` needs to find a `<clipPath>` defined in *any* `<svg>` in the document,
including one that never paints at all (the common `<svg style="display:none">` defs-only
resource pattern) - which had never triggered that lazy resolution and came back with zero
registered clip-paths, confirmed by a scratch diagnostic test before the fix (0 registered vs. 1
expected) and not obvious from reading the code alone. Fixed by having `SvgTreeBuilder.BuildDocument`
eagerly resolve every `clipPath`-tagged id right after `CollectDefinitions` completes (mirroring how
gradients/markers/patterns/masks are already built eagerly there) - `ResolveClipPath` is memoized
and doesn't depend on the root font context being ready yet, so this is a pure addition with no
effect on an already-referenced clipPath's behavior.

A new `SvgClipPathRegistry` (owned lazily by `HtmlContainerInt`, built once on first use during
paint) walks the *whole* box tree unfiltered by `display:none` and force-calls
`CssBoxSvg.EnsureDocument()` on every inline `<svg>` to collect every clipPath id document-wide.
A `display:none` SVG never runs the async `<image>`-prefetch step that normally precedes
`EnsureDocument()`, so a raster `<image>` nested inside such a hidden SVG's `<clipPath>` never gets
its bytes fetched - initially filed as [issue #999](https://github.com/jhaygood86/PeachPDF/issues/999),
but investigation found this isn't an actual gap: `SvgRenderer.AppendClipShapeGeometry` has no case
for `SvgImageElement` at all, so an `<image>` inside *any* `<clipPath>` - hidden SVG or visible one,
prefetched or not - already contributes zero clip geometry, matching the spec (`clip-path`/`<clipPath>`
is geometry-only; raster/luminance-driven clipping is `mask`'s job, already implemented separately).
Closed as not-planned; no accepted-gap file needed since there's no observable gap to track.

`CssClipPathResolver.BuildUrl` reuses `SvgRenderer.BuildClipPath`/`AppendClipShapeGeometry`
(changed from `private` to `internal`) directly rather than writing a second shape-to-path
converter: those methods bake every coordinate straight into the returned path via whatever matrix
they're given, with no dependency on an ambient `RGraphics` transform still being pushed - so the
HTML side can hand them a synthetic matrix (translate + `px`-to-`pt` scale onto its own resolved
reference box, already divided by `PixelsPerPoint`) in place of the SVG-internal
`objectBoundingBox`/`userSpaceOnUse` mapping `SvgRenderer.RenderElement` builds for its own case.

**Caught by a multi-angle review pass after the initial implementation, not by the test suite:**
(1) the eager `<clipPath>`-resolution loop above was originally placed right after
`CollectDefinitions`, *before* `SvgTreeBuilder.BuildDocument` sets `_rootFontSize` from the root
`<svg>`'s own font-size a few lines later - since `ResolveClipPath`'s own memoization makes a second
call a no-op, resolving too early would have permanently locked in the wrong root size for any
`rem`-relative value inside a clipPath shape, for the lifetime of the document. Moved to after
`_rootFontSize` is set. (2) `HtmlContainerInt.Clear()` reset every other per-document cache keyed on
the old `Root` tree but missed the new `_svgClipPathRegistry` field, so reusing one container across
two `SetHtml` calls (the `@container` convergence loop and `PdfGenerator`'s shrink-to-fit re-parse
both do this) would have resolved a second document's `clip-path: url(#id)` against the first
document's clipPath ids. Added to the reset list. (3) a bare `<geometry-box>` clip-path (no shape
function, e.g. `clip-path: border-box`) was drawn as a sharp rectangle even when the element had its
own `border-radius` - a real CSS Backgrounds/Masking corner-clipping deviation, not just the
documented `inset()`-only gap. Fixed to honor the box's own (overlap-reduced) radius for
border-box/padding-box/content-box, via the same `CssBox.ComputeRadii`/`ComputeInnerRadii`
`background-clip` already uses; `margin-box` has no CSS-defined outward radius growth and stays a
plain rectangle. The review also flagged, and this change fixed, two small duplications this
diff introduced: `BuildNone`'s and `BuildInset`'s plain-rectangle drawing (now a shared `DrawRect`),
`BuildInset`'s two overconstraint-reduction blocks (now a shared `ReduceOverconstrained`), and
`ParseInset`'s hand-rolled `round`-keyword scan (now reuses the existing `SplitAtKeyword` helper
`ParseCircle`/`ParseEllipse` already use for `at`).

**Deliberately not done, flagged by the same review:** `ParseBorderRadius`'s 1-4-value/`/`-split
`<border-radius>` grammar is a second, independent implementation of the same algorithm
`BorderRadiusConverter`/`PeriodicValueConverter` already encode for the real `border-radius`
property (this file's other shapes already accept this tradeoff - see its own class doc comment on
deliberately bypassing the full CSS-OM `IPropertyValue` pipeline for raw-token grammar work);
`CssClipPathResolver.ReferenceBoxFor`'s content-box/padding-box arithmetic duplicates
`FragmentPainter.Decorations.BoxModelRect`'s; `GeometryBoxKind` duplicates the 3-value `BoxModel`
enum `background-clip`/`-origin` use (extending `BoxModel` to 4 more members would touch that
unrelated feature's exhaustive switches); and `SvgClipPathRegistry.Walk`'s tree walk duplicates the
shape (not the code) of `DomUtils`'s several existing "walk once, index by id" helpers. Each is a
real, cite-able duplication, but unifying them means touching wider-blast-radius shared
infrastructure (`BoxModel`, `DomUtils`, the CSS-OM converter pipeline) this change doesn't otherwise
need to touch - left as documented follow-up work rather than risked this late in the change.

**Evidence:** `BasicShapeGrammarTests`/`ClipPathResolverIntegrationTests`/`ClipPathPaintIntegrationTests`
cover the new grammar surface, the corner-overlap/over-constraint math, every geometry-box keyword
(including before/after ordering) against a box with real margin/border/padding, a bare-geometry-box
border-radius case (and the margin-box exception), and an end-to-end `url(#id)` reference into a
hidden defs-only `<svg>`. Full suite green on net8.0 (10684/10693, the same one pre-existing flaky
allocation-benchmark test aside - unrelated to this change, not touching any file it modifies).
Solution rebuild clean at 0 warnings. Local diff-coverage measurement hit a persistent Windows-only
coverlet file-lock failure in this environment (`coverlet.core`'s post-instrumentation module-restore
copy step, repeatable across three attempts even with no other test process running) unrelated to
this change's correctness; CI's own Linux-based coverage gate is authoritative here. The showcase
was rasterized through both PDFium and MuPDF (rounded inset, `padding-box`-shrunk circle, and the
`url()` star all agree between renderers).
