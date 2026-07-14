# Supported SVG Features

PeachPDF renders SVG — both inline `<svg>` elements in HTML and standalone SVG (`<img src="x.svg">` / `data:image/svg+xml`) — as real vector PDF content. Shapes become native PDF path-construction operators, gradients become native PDF shadings, and clips/masks/patterns become native PDF constructs. SVG is never rasterized to a bitmap: this preserves scalability and keeps the PDF's file size small regardless of the output resolution. This page documents exactly what is and is not supported. Where a feature is only partially supported, the specific gaps are noted.

---

## SVG Root & Document Structure

| Element | MDN Reference | Notes |
|---------|--------------|-------|
| `svg` | [svg](https://developer.mozilla.org/en-US/docs/Web/SVG/Element/svg) | Both inline (`<svg>` inside HTML) and standalone (`<img src="x.svg">`, `data:image/svg+xml`) are supported. `viewBox`, `width`, `height`, and `preserveAspectRatio` are all honored. A nested `<svg>` establishes its own viewport, coordinate system, and clip |
| `g` | [g](https://developer.mozilla.org/en-US/docs/Web/SVG/Element/g) | Full support, including presentation-attribute inheritance |
| `defs` | [defs](https://developer.mozilla.org/en-US/docs/Web/SVG/Element/defs) | Content is never painted directly, only through a reference (`<use>`, `fill="url(#id)"`, `clip-path`, `mask`, marker properties) |
| `symbol` | [symbol](https://developer.mozilla.org/en-US/docs/Web/SVG/Element/symbol) | Never painted directly; establishes its own viewport (`viewBox`/`preserveAspectRatio`) only when referenced via `<use>`, sized by the `<use>` element's `width`/`height` (defaulting to the current viewport's size, per spec) |
| `use` | [use](https://developer.mozilla.org/en-US/docs/Web/SVG/Element/use) | Resolves `href`/`xlink:href` references, including to `symbol`, nested `svg`, and ordinary shapes. `x`/`y` offset and (for `symbol`/nested-`svg` targets) `width`/`height` overrides are supported. The `<use>` element's own resolved paint becomes the inherited default for an otherwise-unstyled target. A reference cycle is guarded against (max depth 8) |
| `switch` | [switch](https://developer.mozilla.org/en-US/docs/Web/SVG/Element/switch) | Renders only the first buildable child. PeachPDF has no `requiredFeatures`/`requiredExtensions`/`systemLanguage` conditional-support axis to evaluate, so this is equivalent to every other candidate always failing its (nonexistent) test — a deliberate v1 simplification |
| `a` | [a](https://developer.mozilla.org/en-US/docs/Web/SVG/Element/a) | Groups children like `<g>`, and additionally becomes a real PDF link annotation (same-page `#id` fragments become `/GoTo` links, other `href` values become `/URI` links) covering the transformed content's bounding box. A rotated/skewed anchor's link region is approximated by its axis-aligned bounding box, since PDF link annotations are themselves always axis-aligned rectangles |

## Shapes

| Element | MDN Reference | Notes |
|---------|--------------|-------|
| `path` | [path](https://developer.mozilla.org/en-US/docs/Web/SVG/Element/path) | Full support for the `d` attribute grammar: move/line/cubic-bezier/quadratic-bezier/elliptical-arc, absolute and relative, including all shorthand/smooth variants |
| `rect` | [rect](https://developer.mozilla.org/en-US/docs/Web/SVG/Element/rect) | Full support, including independently-rounded corners via `rx`/`ry` (each defaults to the other when only one is specified, per spec) |
| `circle` | [circle](https://developer.mozilla.org/en-US/docs/Web/SVG/Element/circle) | Full support |
| `ellipse` | [ellipse](https://developer.mozilla.org/en-US/docs/Web/SVG/Element/ellipse) | Full support |
| `line` | [line](https://developer.mozilla.org/en-US/docs/Web/SVG/Element/line) | Full support. Per spec, `fill` never applies to a `<line>` (it has no interior) — no fill operator is emitted regardless of the element's fill paint |
| `polyline` | [polyline](https://developer.mozilla.org/en-US/docs/Web/SVG/Element/polyline) | Stroke never closes back to the first point, matching spec. A filled (rather than the far more common `fill="none"`) polyline is a known simplification: fill uses the same unclosed geometry as the stroke rather than spec's implicit closing segment for fill purposes only |
| `polygon` | [polygon](https://developer.mozilla.org/en-US/docs/Web/SVG/Element/polygon) | Full support |

## Painting: Fill, Stroke, Color

| Feature | MDN Reference | Notes |
|---------|--------------|-------|
| `fill` | [fill](https://developer.mozilla.org/en-US/docs/Web/SVG/Attribute/fill) | Solid colors (named, hex, `rgb()`), gradient references (`url(#id)`), pattern references (`url(#id)`), `none`, and `currentColor` |
| `fill-rule` | [fill-rule](https://developer.mozilla.org/en-US/docs/Web/SVG/Attribute/fill-rule) | `nonzero` and `evenodd`, inherited |
| `fill-opacity` | [fill-opacity](https://developer.mozilla.org/en-US/docs/Web/SVG/Attribute/fill-opacity) | Full support, inherited, independent of `opacity` and `stroke-opacity` |
| `stroke` | [stroke](https://developer.mozilla.org/en-US/docs/Web/SVG/Attribute/stroke) | Solid colors and gradient references paint a real stroke (including a real gradient-filled stroke, not an approximation). Pattern-filled strokes are not supported — see [Unsupported SVG Features](#unsupported-svg-features) |
| `stroke-width` | [stroke-width](https://developer.mozilla.org/en-US/docs/Web/SVG/Attribute/stroke-width) | Full support, inherited |
| `stroke-opacity` | [stroke-opacity](https://developer.mozilla.org/en-US/docs/Web/SVG/Attribute/stroke-opacity) | Full support, inherited |
| `stroke-linecap` | [stroke-linecap](https://developer.mozilla.org/en-US/docs/Web/SVG/Attribute/stroke-linecap) | `butt`, `round`, `square`, inherited |
| `stroke-linejoin` | [stroke-linejoin](https://developer.mozilla.org/en-US/docs/Web/SVG/Attribute/stroke-linejoin) | `miter`, `round`, `bevel`, inherited |
| `stroke-miterlimit` | [stroke-miterlimit](https://developer.mozilla.org/en-US/docs/Web/SVG/Attribute/stroke-miterlimit) | Full support, inherited |
| `stroke-dasharray` | [stroke-dasharray](https://developer.mozilla.org/en-US/docs/Web/SVG/Attribute/stroke-dasharray) | Full support, inherited |
| `stroke-dashoffset` | [stroke-dashoffset](https://developer.mozilla.org/en-US/docs/Web/SVG/Attribute/stroke-dashoffset) | Full support, inherited |
| `opacity` | [opacity](https://developer.mozilla.org/en-US/docs/Web/SVG/Attribute/opacity) | Not inherited as a property — it composites instead, per spec. On a leaf shape (no children), applied as a simple alpha multiply on the shape's own fill/stroke/text color. On a container (`<g>`, `<a>`, nested `<svg>`) it's rendered as a genuine, isolated PDF transparency group — the container's children are painted into an offscreen Form XObject and flattened, then that single flattened result is composited once at the container's own opacity, so overlapping children no longer double-blend where they overlap. (A `<use>` referencing a container indirectly still uses the simpler per-shape multiply for that target's children — a narrower residual v1 gap.) |
| `currentColor` | [currentColor](https://developer.mozilla.org/en-US/docs/Web/CSS/color_value/currentcolor) | Resolves to the CSS `color` property of the inline `<svg>`'s HTML ancestor; for standalone SVG (no CSS context), resolves to black |
| `color-interpolation` / `color-interpolation-filters` | — | Not supported — see [Unsupported SVG Features](#unsupported-svg-features) |

## Gradients

| Element | MDN Reference | Notes |
|---------|--------------|-------|
| `linearGradient` | [linearGradient](https://developer.mozilla.org/en-US/docs/Web/SVG/Element/linearGradient) | `x1`/`y1`/`x2`/`y2`, `gradientUnits` (`objectBoundingBox` — the spec default — and `userSpaceOnUse`, both fully resolved against the referencing shape's bounding box or the user-space coordinate system respectively), `gradientTransform` (translate/scale; a rotated/skewed `gradientTransform` on a *radial* gradient can't turn the circle into a rotated ellipse — see below), `spreadMethod` |
| `radialGradient` | [radialGradient](https://developer.mozilla.org/en-US/docs/Web/SVG/Element/radialGradient) | `cx`/`cy`/`r`, plus focal point `fx`/`fy` (independent of the center, per spec), same `gradientUnits`/`gradientTransform`/`spreadMethod` support as `linearGradient`. A rotated `gradientTransform` renders as an axis-aligned ellipse (the closest 2D approximation) rather than a true rotated ellipse |
| `stop` | [stop](https://developer.mozilla.org/en-US/docs/Web/SVG/Element/stop) | `offset`, `stop-color`, `stop-opacity` (as both presentation attributes and via `style=`) |
| `spreadMethod` | [spreadMethod](https://developer.mozilla.org/en-US/docs/Web/SVG/Attribute/spreadMethod) | `pad`, `reflect`, `repeat`. `reflect`/`repeat` are implemented by tiling the gradient's own stop list across an axis (or radius) extended to cover the filled shape's bounding box — mirroring alternate cycles for `reflect` so adjacent cycles share a color with no seam — capped at 500 tiled cycles as a safety guard against pathological short-axis/large-shape combinations |

Gradients render as native PDF shadings (`/ShadingType`), not rasterized bitmaps.

## Clipping, Masking, Patterns

| Element | MDN Reference | Notes |
|---------|--------------|-------|
| `clipPath` | [clipPath](https://developer.mozilla.org/en-US/docs/Web/SVG/Element/clipPath) | Full support; `clip-rule` (`nonzero`/`evenodd`) controls the clip region's fill rule. Renders as a real PDF clipping path (`W n`), not a mask |
| `mask` | [mask](https://developer.mozilla.org/en-US/docs/Web/SVG/Element/mask) | Renders as a real PDF luminosity soft mask (`/SMask` via `/ExtGState`) — mask content is painted (not just used for geometry, unlike `clipPath`) into an offscreen form, and its luminance becomes the mask's alpha. `maskUnits`/`maskContentUnits` (`objectBoundingBox`/`userSpaceOnUse`) are both supported, including the spec's `-10%`/`-10%`/`120%`/`120%` default region |
| `pattern` | [pattern](https://developer.mozilla.org/en-US/docs/Web/SVG/Element/pattern) | `patternUnits`/`patternContentUnits` (`objectBoundingBox`/`userSpaceOnUse`), `patternTransform`, `viewBox`/`preserveAspectRatio`. Implemented by rendering the pattern's content once into a PDF Form XObject "tile," then drawing that same tile repeatedly (clipped to the filled shape's geometry) across the fill region — every repeat is a reference to the same underlying vector content, never a rasterized bitmap, but this is not a native PDF `/PatternType 1` tiling pattern object. Capped at 10,000 tile placements per fill as a safety guard against pathological pattern/shape size ratios |

## Markers

| Element | MDN Reference | Notes |
|---------|--------------|-------|
| `marker` | [marker](https://developer.mozilla.org/en-US/docs/Web/SVG/Element/marker) | `refX`/`refY`, `markerWidth`/`markerHeight`, `viewBox`/`preserveAspectRatio`, `markerUnits` (`strokeWidth`/`userSpaceOnUse`), `orient` (`auto`, `auto-start-reverse`, and a fixed angle) |
| `marker-start` / `marker-mid` / `marker-end` | [marker-start](https://developer.mozilla.org/en-US/docs/Web/SVG/Attribute/marker-start) | Full support, plus the `marker` shorthand (sets all three; an individually-specified property overrides just that one). Inherited. Per spec, markers only attach to `path`/`line`/`polyline`/`polygon` — not basic shapes like `rect`/`circle`/`ellipse`, which have no defined vertex sequence |
| Tangent/orientation math | — | Exact for straight-line and cubic-bezier path segments (computed from the segment's own control-point vectors); chord-approximated for elliptical arc segments (a documented v1 simplification — the tangent is estimated from the arc's endpoints rather than its true derivative) |

## Links

See `a` in [SVG Root & Document Structure](#svg-root--document-structure) above.

## Raster & Nested Content

| Element | MDN Reference | Notes |
|---------|--------------|-------|
| `image` | [image](https://developer.mozilla.org/en-US/docs/Web/SVG/Element/image) | Only a `data:` URI `href` is resolved. A raster payload (`data:image/png`, `data:image/jpeg`, etc.) embeds as a real PDF image XObject; a `data:image/svg+xml` payload renders as its own real vector scene graph (never rasterized), with its own `url(#id)` references resolving against its own definitions. `x`/`y`/`width`/`height`/`preserveAspectRatio` are all honored — including for raster images, which are fit into their box per `preserveAspectRatio`'s alignment/meet/slice rules, the same math used for viewports elsewhere in this renderer. A network URL or local file path `href` is a documented v1 gap (see below) — the element still builds but renders nothing |
| Nested `svg` | see `svg` above | Establishes its own viewport, coordinate system, and clip |

**Why `<image>` only supports `data:` URIs:** SVG document parsing (`SvgTreeBuilder.Build`) is synchronous, while resolving a network or file-path image reference needs this library's general asynchronous resource-loading pipeline. `data:` URI resolution needs no actual I/O (it's pure in-memory base64 decoding), so it's resolved synchronously inline without that pipeline. This matches PeachPDF's own general guidance that "all images and assets must be local to the file system or in `data:` URIs" for predictable, self-contained rendering.

## Text

| Element | MDN Reference | Notes |
|---------|--------------|-------|
| `text` | [text](https://developer.mozilla.org/en-US/docs/Web/SVG/Element/text) | `x`/`y`/`dx`/`dy`, a single leading `rotate` value, `text-anchor` (`start`/`middle`/`end`), `font-family`/`font-size`/`font-weight`/`font-style` (as both presentation attributes and `style=`). Bridges directly into PeachPDF's existing font/text-measurement pipeline — the same font resolution ordinary HTML text uses |
| `tspan` | [tspan](https://developer.mozilla.org/en-US/docs/Web/SVG/Element/tspan) | A `tspan` without its own `x`/`y` flows immediately after its previous sibling's rendered width (ordinary SVG text flow), at whole-run granularity. A `tspan` with its own `x` and/or `y` starts a new absolute position. Font properties and paint (`fill`/`stroke`/etc.) are inherited from the enclosing `text`/`tspan` when not overridden |
| `tref` | [tref](https://developer.mozilla.org/en-US/docs/Web/SVG/Element/tref) *(deprecated in SVG2, still supported here)* | `href`/`xlink:href` resolves to another element's own text content, reused as this run's text |
| `textPath` | [textPath](https://developer.mozilla.org/en-US/docs/Web/SVG/Element/textPath) | Not supported — see [Unsupported SVG Features](#unsupported-svg-features) |

**Known text simplifications** (all documented in code as deliberate v1 scope reductions, the same category as `<switch>`'s first-child-only behavior above):

- Per-character `x`/`y`/`dx`/`dy`/`rotate` arrays (SVG 1.1's full per-glyph positioning model) are not supported — only a single leading value applies to a whole `text`/`tspan`/`tref` run.
- `font-family`/`font-size`/`font-weight`/`font-style` inherit only within a `text` element's own subtree (from `<text>` down through its `<tspan>`s) — not from an ancestor `<g>`/`<svg>` the way real SVG/CSS inheritance works.
- Whitespace collapsing (`xml:space="default"`'s "runs of whitespace become one space, trim the ends") is applied independently per run, not across a whole `<text>` subtree as one unit as the specification technically requires.
- Only a solid `fill` paints text — a gradient- or pattern-referencing `fill` on text renders nothing, since PDF text-showing operators take a single color, not an arbitrary paint. `stroke` on text is not supported at all. Both would require treating each glyph's outline as a fillable/strokeable path, the same underlying capability `textPath` needs (see below).

## Coordinate Systems & Units

| Feature | Notes |
|---------|-------|
| `viewBox` | Full support, including all 9 `preserveAspectRatio` alignment keywords (`xMinYMin`, `xMidYMid`, `xMaxYMax`, etc.) and both `meet` and `slice`, plus `none` (independent-axis stretch) |
| Length units | Unitless numbers, `%` (resolved against the viewport width/height/diagonal per the axis, per spec), `px`, `pt`, `pc`, `in`, `cm`, `mm`, `em`, `rem` (the latter two approximate CSS's 16px initial font-size, since no live font-size cascade context reaches arbitrary SVG geometry attributes) |
| `transform` | `matrix()`, `translate()`, `scale()`, `rotate()` (including the 3-argument `rotate(angle, cx, cy)` form), `skewX()`, `skewY()`. Multiple functions may be chained; they compose per spec |

## CSS Integration

| Feature | Notes |
|---------|-------|
| Presentation attributes | Every property above is readable as a plain XML attribute (e.g. `fill="red"`) |
| `style="..."` | Inline `style=` is supported on every element, taking precedence over presentation attributes, per CSS cascade rules |
| `<style>` element | Supported, with a deliberately minimal selector matcher scoped to type (`rect`), class (`.foo`), ID (`#foo`), and compound selectors (`rect.foo`), plus comma-separated selector lists. Specificity follows standard CSS rules. Descendant/child/sibling combinators, attribute selectors, and pseudo-classes are **not** supported in an SVG `<style>` block — a rule using one of those is silently skipped, and does not apply. **Known gap:** for an inline `<svg>` inside HTML (not standalone `<img>`/`data:` SVG), a `<style>` element nested inside the `<svg>` is not reliably associated with it by PeachPDF's underlying HTML tokenizer, and may fail to apply — this is a pre-existing HTML-parsing limitation, not specific to SVG `<style>` handling itself. Standalone SVG is not affected |
| `class` / `id` | Read for `<style>` selector matching, same as HTML |

---

## Unsupported SVG Features

The following SVG 1.0/1.1 features are not supported, each for the reason given:

- **SMIL animation** (`animate`, `animateColor`, `animateMotion`, `animateTransform`, `set`) — a static PDF page has no animation/timeline model to target.
- **Scripting** (`script`, event-handler attributes like `onclick`) — no script execution context exists in a static PDF page.
- **`cursor`** — no pointer/cursor model exists for embedded vector content in a PDF.
- **`view`** — fragment-based view navigation has no equivalent in static PDF page content.
- **Legacy SVG glyph-outline fonts** (`font`, `glyph`, `missing-glyph`, `hkern`, `vkern`, `font-face` and its children) — this would duplicate PeachPDF's existing full TrueType/CFF/WOFF/WOFF2 font pipeline for a mechanism SVG2 itself deprecated in favor of ordinary web fonts; use real fonts via `font-family`/`@font-face` instead.
- **`filter` and all `fe*` filter-primitive elements** — applying a filter requires rasterizing the affected vector content to a bitmap and re-embedding it as an image, which abandons the vector fidelity, scalability, and small file size this renderer otherwise deliberately preserves. (Contrast with `mask` and `pattern`, which map onto native, non-rasterizing PDF constructs and are therefore fully supported.)
- **`foreignObject`** — embedding arbitrary nested HTML/XHTML with its own independent layout context inside SVG coordinate space is architecturally a large, separate feature, not a core SVG capability; a candidate for a future, standalone initiative.
- **`icc-color`** / ICC color profiles — a rarely-authored prepress-workflow feature, low value relative to its implementation cost.
- **`textPath`** — placing text along arbitrary path geometry needs true per-glyph placement (splitting the string into individual characters, measuring each, walking the path's arc-length parameterization) — a substantially larger feature than the rest of text support, deliberately cut from this pass. `text`/`tspan`/`tref` (ordinary positioned text) are fully supported — see [Text](#text) above.
- **Gradient/pattern fill or any stroke on `<text>`** — see the [Text](#text) section's known-simplifications list above.
- **Group `opacity` on a `<use>`-referenced container** — the `<use>` element's resolved target still uses the simpler per-shape alpha multiply rather than the isolated transparency-group compositing `<g>`/`<a>`/nested `<svg>` get (see the `opacity` row under [Painting: Fill, Stroke, Color](#painting-fill-stroke-color)) — a narrower residual v1 gap, not the general double-blend limitation this used to be (see [HTML & CSS Support](html-css-support.md#opacity) for the equivalent CSS `opacity` support, which shares the same isolated-group fix).
