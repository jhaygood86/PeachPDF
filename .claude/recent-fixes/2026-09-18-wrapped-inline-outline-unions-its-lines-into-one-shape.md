# A wrapped inline's outline is one unioned shape, not a ring per line (issue #1206 context)

`.claude/recent-fixes/2026-09-17-wrapped-inline-outline-closes-at-line-wraps.md` closed each line into
its own ring, and its migration note recorded that as "Chromium's own closed-rect-per-line shape".
**That premise was wrong**, and this change replaces the geometry it produced. The mechanism that
note describes — forcing `HasLeftEdge`/`HasRightEdge` true for outline paint entries only — is still
live and still correct; it is what makes each line a complete rectangle, which is exactly what the
union now consumes. Only the claim about the resulting *shape* was wrong.

## The load-bearing correction

Chromium does not special-case anything about borders, and does not draw a ring per line. It unions
whenever there is more than one outline rectangle:

```cpp
if (*united_outline_rect == pixel_snapped_outline_rects[0]) { /* simple per-rect path */ }
else ComplexOutlinePainter(...).Paint();
```

The user's screenshot read as "the border changes the outline" because it does — but only
*indirectly*. A border makes each line's border box a few px taller, so consecutive line rectangles
start **overlapping vertically**, and the union of overlapping rectangles is one connected contour.
Remove the border and the same union yields three *disjoint* contours, which is visually
indistinguishable from the old per-line rings. **Same algorithm, different topology.** Anyone tempted
to add a "does this box have a border" branch here is re-deriving the wrong model — the border must
not be consulted at all.

## Shape of the change

- `RectilinearRegion` (new, `Html/Core/Utils/`) — `Union` builds a coordinate grid from the
  rectangles' own distinct edge positions (epsilon-merged at `1e-6`), marks covered cells, emits
  boundary edges **directed so the covered side is always on the right of travel**, and chains them
  into closed contours. `Shrink` insets a contour by moving each corner along the sum of its two
  adjacent edges' inward normals, which is correct for convex, reflex and hole corners alike.
- `OutlineRegionPainter` (new, `Html/Core/Handlers/`) — turns contours into filled bands. `solid` is
  one band `[0, width]`; `double` is `[0, width/3]` and `[2*width/3, width]`.
- `OutlineDrawHandler.DrawRegionOutline` / `SupportsRegionOutline`; `FragmentPainter`'s existing
  deferred `outlinePaints` drain gained the union branch. The drain site already existed — that seam
  is why this was a contained change rather than a paint-walk rewrite.

Chromium's integer pixel-snapping was deliberately skipped: there is no device-pixel grid here, and
using the rectangles' own edge positions as grid lines is exact in point space.

**Nonzero winding, not even-odd** — `BoxEdgesDrawHandler.DrawRectangularRing` uses even-odd for
simple rings, but an inset contour can self-cross where a neck is narrower than `2 × width`. Under
nonzero a collapsed neck fills solid (correct); even-odd would punch a hole through it (wrong).

## Found by running it, not by reading it

- **The test double was collapsing subpaths.** `TestGraphicsPath` flattened every subpath into one
  point list, so `DrawPathCall` could not distinguish one connected shape from three disjoint ones —
  the exact property under test. Three tests "failed" in a way that looked like algorithm bugs and
  were pure assertion blindness; the union had been correct the whole time (36 points = 3 contours,
  20 = 1 merged). Fixed by recording `SubpathStarts`, with `Subpath(i)`/`SubpathBounds(i)` accessors.
  **Each contour contributes exactly 2 subpaths** to a band (itself plus its inset copy), so one
  shape = 2 subpaths and three shapes = 6.
- **A line box's rect is the inline's own box, not the line-height slot.** So `line-height: 40pt`
  genuinely does leave vertical gaps between lines and genuinely does yield disjoint contours. The
  opposite instinct — that lines abut regardless of `line-height` — is wrong and cost a test rewrite.
- **Corner radii are clamped to the line-box height**, so a `20pt` radius on an ~11.5pt-tall line box
  does not survive at 20pt.
- **The union's top-right corner belongs to the *first* line, not the widest.** The widest line's
  right edge is further right but lower, and is a concave step corner. Getting this right is what
  finally verified `DescribeCorner`'s radius-to-corner mapping, which had never been covered before.
- **A translucent outline is the sharpest visual proof.** Stacked per-line rings double-darken where
  lines overlap; the union shows a perfectly even band. Both renderers agree on this.

## The showcase had to teach the rule, not just show the feature

The first version of the `outline` showcase's wrapped-inline row was four swatches: outline alone,
border alone, border+outline, dashed. Rendered, it was actively misleading — the swatch captioned
"outline (one connected shape)" visibly drew **three separate rings** (its `line-height: 2` left real
gaps), and the only merged sample was the one with a border. A reader draws exactly the wrong
conclusion from that: *borders make outlines merge*. They don't — which is the same misconception
this whole change exists to correct.

A probe fixture confirmed it: at `line-height: 1.2`, `1.0`, or with `outline-offset: 8px`, or with
padding, the lines merge with **no border at all**. Only a wide `line-height` with zero offset stays
separate. So the row was rebuilt as a progression where each sample is falsifiable — lines apart →
closer lines merging with no border present → the *same* wide spacing merged by offset alone →
border+outline → dashed fallback. The second and third samples are the load-bearing ones; drop them
and the set silently teaches the border myth again.

Two layout traps while building it, neither a bug in this change: a flex row is one line, so
`break-inside: avoid` on it has nothing to act on (css-break-3 §3.1 puts a flex container's break
points *between* lines), and the fix for the row landing across a page boundary is to make the
section shorter, not to annotate it. And the demo `line-height` inherits into the caption `div`s and
collapses them, so the captions need their own.

## Performance

A 4000-word stress fixture measured 1.99s with the union and 1.99s without an outline at all — no
measurable overhead. But the first coverage fill was O(rects³) (a per-cell midpoint scan against
every rectangle); it was replaced with direct block marking, where each rectangle resolves its own
edges to grid indices via an `IndexOf` binary search and marks only its own cells.

## Deliberately not done

The patterned/bevelled styles and the atomic-inline-descendant rectangles — both are accepted gaps
with their own files and issues #1206/#1207. Chromium's cosmetic style degradations (`double` at
`width ≤ 2` → `solid`; `groove`/`ridge` at 1px → `solid` blended 50% toward `color.Dark()`) are not
ported. The `!IsVerticalDecorationGeometry(box)` guard was kept on the union path, preserving the
vertical-writing-mode exclusion from #769.

## Evidence

222 outline tests pass; full suite 12521 passed with one pre-existing unrelated failure
(`TableLayout_AsymmetricWrappableHeaders_...`, confirmed by `git stash` + rerun on clean `main`).
Diff coverage: `OutlineRegionPainter.cs` 100%, `RectilinearRegion.cs` 97.7%, `OutlineDrawHandler.cs`
97.5%. Thirteen hand-built scenarios plus the regenerated `outline` showcase were rasterized through
**both PDFium and MuPDF** and agree. `dotnet build PeachPDF.slnx -t:Rebuild` is clean.
