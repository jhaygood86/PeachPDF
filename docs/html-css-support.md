# HTML & CSS Support

PeachPDF renders a subset of the HTML and CSS specifications. This page documents exactly what is and is not supported. Where a feature is only partially supported, the specific gaps are noted.

## Length units

All CSS length units resolve through one shared conversion, at their spec-defined physical ratios ([CSS Values & Units §6.2](https://www.w3.org/TR/css-values-3/#absolute-lengths)) — everywhere: body layout, fonts, borders, backgrounds, images, `@page` geometry, and SVG.

| Unit | Physical size | In PDF points |
|------|---------------|---------------|
| `px` | 1/96 in | 0.75pt |
| `pt` | 1/72 in | 1pt |
| `in` | 1 in | 72pt |
| `cm` / `mm` | metric | 28.35pt / 2.835pt |
| `pc` | 1/6 in | 12pt |

In particular, `px` is spec-correct CSS pixels: a `96px`-wide element is exactly one inch wide in the output PDF, and a 96×96-pixel image renders at its true CSS size of one square inch — the same as every browser's print output and other spec-conformant paged renderers. Relative units (`em`/`rem`/`ex`/`%`) resolve against their usual per-property bases; viewport units (`vw`/`vh`/`vmin`/`vmax`) and `ch` are not supported.

> **Migration note (breaking change):** versions prior to this convention treated `1px` as `1pt` for non-font layout lengths, rendering px-sized content 33% larger than its true CSS size. Documents authored against that behavior will see px-derived lengths shrink by ×0.75 to their spec-correct physical size. Absolute units (`pt`/`mm`/`cm`/`in`/`pc`) are unaffected, and font sizes in px already used the spec-correct ratio, so text set in px is also unaffected.

---

## HTML Elements

### Document Structure

| Element | MDN Reference | Notes |
|---------|--------------|-------|
| `html` | [html](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/html) | Full support |
| `head` | [head](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/head) | Processed for `<style>` and `<link rel="stylesheet">` children; other children are ignored |
| `body` | [body](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/body) | Full support |

### Metadata

| Element | MDN Reference | Notes |
|---------|--------------|-------|
| `style` | [style](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/style) | Inline stylesheets are applied |
| `link` | [link](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/link) | Only `rel="stylesheet"` is processed; other link types are ignored |
| `meta` | [meta](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/meta) | `name="author"`, `name="subject"`, `name="keywords"`, `name="date"`, and `name="generator"` are extracted and written to the PDF info dictionary; all other meta names are ignored |
| `title` | [title](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/title) | Text content is written to the PDF document title in the info dictionary |

#### PDF metadata extraction

PeachPDF automatically extracts standard HTML metadata elements and writes them to the PDF info dictionary. No additional configuration is required — just include the elements in your HTML `<head>`.

| HTML source | PDF info field |
|---|---|
| `<title>` inner text | Title |
| `<meta name="author" content="...">` | Author |
| `<meta name="subject" content="...">` | Subject |
| `<meta name="keywords" content="...">` | Keywords |
| `<meta name="date" content="...">` | Creation date (parsed via `DateTime.TryParse`) |
| `<meta name="generator" content="...">` | Creator |

The **Producer** and **Creator** fields both default to `PeachPDF {version}` when no `<meta name="generator">` is present. The Producer field always identifies PeachPDF as the PDF converter regardless of any generator meta tag.

Example:

```html
<!DOCTYPE html>
<html>
<head>
  <title>Quarterly Report</title>
  <meta name="author" content="Finance Team">
  <meta name="subject" content="Q1 2025 Results">
  <meta name="keywords" content="finance, quarterly, report">
  <meta name="date" content="2025-04-01">
</head>
<body>
  <!-- document content -->
</body>
</html>
```

### Sections

| Element | MDN Reference | Notes |
|---------|--------------|-------|
| `article` | [article](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/article) | Rendered as a block |
| `aside` | [aside](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/aside) | Rendered as a block |
| `details` | [details](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/details) | Rendered as a block; the open/close toggle is not supported — content is always visible |
| `dialog` | [dialog](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/dialog) | Rendered as a block; open/close behavior is not supported |
| `figure` | [figure](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/figure) | Rendered as a block |
| `figcaption` | [figcaption](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/figcaption) | Rendered as a block |
| `footer` | [footer](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/footer) | Rendered as a block |
| `header` | [header](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/header) | Rendered as a block |
| `hgroup` | [hgroup](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/hgroup) | Rendered as a block |
| `main` | [main](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/main) | Rendered as a block |
| `nav` | [nav](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/nav) | Rendered as a block |
| `search` | [search](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/search) | Rendered as a block |
| `section` | [section](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/section) | Rendered as a block |
| `summary` | [summary](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/summary) | Rendered as inline text; the disclosure triangle is not rendered |

### Content Grouping

| Element | MDN Reference | Notes |
|---------|--------------|-------|
| `address` | [address](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/address) | Rendered as a block |
| `blockquote` | [blockquote](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/blockquote) | Rendered as a block with default margin |
| `center` | [center](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/center) | Deprecated element; rendered with `text-align: center` |
| `dd` | [dd](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/dd) | Full support |
| `dir` | [dir](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/dir) | Deprecated element; rendered as an unordered list |
| `div` | [div](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/div) | Full support |
| `dl` | [dl](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/dl) | Full support |
| `dt` | [dt](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/dt) | Full support |
| `fieldset` | [fieldset](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/fieldset) | Rendered as a block with a border; no interactive behavior |
| `form` | [form](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/form) | Rendered as a block; form submission is not supported |
| `hr` | [hr](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/hr) | Full support |
| `li` | [li](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/li) | Full support |
| `menu` | [menu](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/menu) | Deprecated element; rendered as an unordered list |
| `ol` | [ol](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/ol) | Full support |
| `p` | [p](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/p) | Full support |
| `pre` | [pre](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/pre) | Full support |
| `ul` | [ul](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/ul) | Full support |

### Headings

| Element | MDN Reference | Notes |
|---------|--------------|-------|
| `h1` | [h1](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/Heading_Elements) | Full support |
| `h2` | [h2](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/Heading_Elements) | Full support |
| `h3` | [h3](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/Heading_Elements) | Full support |
| `h4` | [h4](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/Heading_Elements) | Full support |
| `h5` | [h5](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/Heading_Elements) | Full support |
| `h6` | [h6](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/Heading_Elements) | Full support |

### Inline Text

| Element | MDN Reference | Notes |
|---------|--------------|-------|
| `a` | [a](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/a) | `href` links are embedded as clickable PDF hyperlinks, regardless of whether the `<a>` also carries an `id`/`name` (both a link source and a fragment target, e.g. `<a id="toc-1" href="#ch1">`, is a common and fully-supported pattern). Anchor links (`href="#id"`) for in-document navigation are also supported. Any element (not just `<a>`) with an `id` or `name` attribute can serve as a fragment-link target |
| `b` | [b](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/b) | Full support |
| `bdo` | [bdo](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/bdo) | Full support |
| `big` | [big](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/big) | Deprecated element; rendered with a larger font size |
| `br` | [br](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/br) | Full support |
| `cite` | [cite](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/cite) | Rendered as italic |
| `code` | [code](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/code) | Rendered in a monospace font |
| `del` | [del](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/del) | Rendered with strikethrough |
| `em` | [em](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/em) | Rendered as italic |
| `i` | [i](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/i) | Rendered as italic |
| `ins` | [ins](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/ins) | Rendered with underline |
| `kbd` | [kbd](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/kbd) | Rendered in a monospace font |
| `s` | [s](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/s) | Rendered with strikethrough |
| `samp` | [samp](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/samp) | Rendered in a monospace font |
| `small` | [small](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/small) | Rendered with a smaller font size |
| `span` | [span](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/span) | Full support |
| `strike` | [strike](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/strike) | Deprecated element; rendered with strikethrough |
| `strong` | [strong](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/strong) | Rendered as bold |
| `sub` | [sub](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/sub) | Full support |
| `sup` | [sup](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/sup) | Full support |
| `tt` | [tt](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/tt) | Deprecated element; rendered in a monospace font |
| `u` | [u](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/u) | Rendered with underline |
| `var` | [var](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/var) | Rendered as italic |

### Tables

| Element | MDN Reference | Notes |
|---------|--------------|-------|
| `table` | [table](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/table) | Full support |
| `caption` | [caption](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/caption) | Full support |
| `col` | [col](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/col) | Width attribute is applied |
| `colgroup` | [colgroup](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/colgroup) | Full support |
| `tbody` | [tbody](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/tbody) | Full support |
| `td` | [td](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/td) | `colspan` and `rowspan` are fully supported |
| `tfoot` | [tfoot](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/tfoot) | Full support |
| `th` | [th](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/th) | `colspan` and `rowspan` are fully supported; rendered as bold and centered by default |
| `thead` | [thead](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/thead) | Full support |
| `tr` | [tr](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/tr) | Full support |

### Embedded Content

| Element | MDN Reference | Notes |
|---------|--------------|-------|
| `img` | [img](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/img) | Full support; images are loaded via the configured network loader. `data:` URIs and local `file:` URIs (including relative paths, which resolve against the document base — see [Usage Examples](usage-examples.md#rendering-a-local-html-file)) are always supported regardless of the configured loader. An SVG source (`.svg` file, `data:image/svg+xml`) renders as real vector PDF content — see [Supported SVG Features](supported-svg-features.md) |
| `object` | [object](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/object) | Implements the HTML "replacement algorithm" for a static renderer: if `data` resolves to a supported image (including through the `type` attribute or a `data:` URI's own MIME header — checked without a network fetch when the declared type is already known not to be an image), the element renders exactly like `img`. Otherwise it falls back to its DOM children, which may themselves be nested `object`/`img` elements and are resolved the same way recursively — matching how browsers fall through a chain of nested `<object>` fallbacks |
| `iframe` | [iframe](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/iframe) | Rendered as a placeholder box with a gray border. For YouTube and Vimeo embed URLs, a video thumbnail image is displayed |
| `video` | [video](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/video) | Video playback isn't possible in a static PDF, so the element renders its `poster` image (the frame browsers show before playback) as a replaced element — exactly like `img`, honoring [`object-fit`/`object-position`](#box-model). With no `poster` (or if it fails to load), the element falls back to being an ordinary container laying out any fallback DOM content |
| `svg` | [svg](https://developer.mozilla.org/en-US/docs/Web/SVG/Element/svg) | Inline SVG renders as real vector PDF content, not a rasterized bitmap — see [Supported SVG Features](supported-svg-features.md) for the full SVG compatibility matrix |

### Forms

Form elements are rendered as static boxes. There is no interactive behavior — inputs cannot be focused or edited, and forms cannot be submitted.

| Element | MDN Reference | Notes |
|---------|--------------|-------|
| `button` | [button](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/button) | Rendered as a static `inline-block` box |
| `input` | [input](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/input) | Rendered as a static `inline-block` box; no interactivity |
| `select` | [select](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/select) | Rendered as a static `inline-block` box |
| `textarea` | [textarea](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/textarea) | Rendered as a static `inline-block` box |

### Scripting

| Element | MDN Reference | Notes |
|---------|--------------|-------|
| `script` | [script](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/script) | Completely ignored; JavaScript is not executed |
| `noscript` | [noscript](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/noscript) | Content is always rendered, because JavaScript is never executed |

### Legacy Frames

| Element | MDN Reference | Notes |
|---------|--------------|-------|
| `frame` | [frame](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/frame) | Deprecated element; no frame content is loaded |
| `frameset` | [frameset](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/frameset) | Deprecated element; rendered as a block |
| `noframes` | [noframes](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/noframes) | Deprecated element; content is rendered |

---

## CSS Properties

### Box Model

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `width` | [width](https://developer.mozilla.org/en-US/docs/Web/CSS/width) | Full support |
| `height` | [height](https://developer.mozilla.org/en-US/docs/Web/CSS/height) | Full support. When content is taller than an explicit `height`, the box does not grow to fit it — content overflows past the box's bottom edge (same behavior as `max-height`/`overflow: hidden` elsewhere in this engine) |
| `max-width` | [max-width](https://developer.mozilla.org/en-US/docs/Web/CSS/max-width) | Full support, including replaced elements (images), absolutely/fixed positioned boxes, flex items, and table cells (a cell's `max-width` caps its column) |
| `min-width` | [min-width](https://developer.mozilla.org/en-US/docs/Web/CSS/min-width) | Full support, including replaced elements (images), absolutely/fixed positioned boxes, flex items, and table cells (a cell's `min-width` widens its column) |
| `max-height` | [max-height](https://developer.mozilla.org/en-US/docs/Web/CSS/max-height) | Full support, including replaced elements (images), absolutely/fixed positioned boxes, and flex items. When content is taller than `max-height`, the box does not grow to fit it — content overflows past the box's bottom edge (same behavior as `overflow: hidden` elsewhere in this engine) |
| `min-height` | [min-height](https://developer.mozilla.org/en-US/docs/Web/CSS/min-height) | Full support, including replaced elements (images), absolutely/fixed positioned boxes, and flex items |
| `margin` | [margin](https://developer.mozilla.org/en-US/docs/Web/CSS/margin) | Shorthand and all four longhands (`margin-top`, `margin-right`, `margin-bottom`, `margin-left`) are supported |
| `padding` | [padding](https://developer.mozilla.org/en-US/docs/Web/CSS/padding) | Shorthand and all four longhands (`padding-top`, `padding-right`, `padding-bottom`, `padding-left`) are supported |
| `box-sizing` | [box-sizing](https://developer.mozilla.org/en-US/docs/Web/CSS/box-sizing) | `content-box` and `border-box` are supported |
| `aspect-ratio` | [aspect-ratio](https://developer.mozilla.org/en-US/docs/Web/CSS/aspect-ratio) | Supported in both directions (`aspect-ratio: <number> [ / <number> ]?`, `auto`, or `auto <ratio>`). A box with a **definite width** and an **auto height** takes its height from the width via the ratio; a **definite height** with an **auto width** derives the width from the height, where the width would otherwise be shrink-to-fit (an absolutely-positioned auto-width box, and replaced elements) — a normal-flow block's auto width stays stretch-fit, which wins over the ratio, matching browsers. The ratio applies to the box-sizing box, and a ratio-derived height is definite, so a percentage-height descendant resolves against it (this is what makes a pure-CSS chart's bars take their height from a ratio-sized container); an **indefinite** percentage height is treated as automatic, so the ratio sizes it too. The ratio-derived dimension is clamped by `min-*`/`max-*`. A definite length in the other axis overrides the ratio. On **replaced elements** (images, `<svg>`, `<object>`, `<video>` poster): a bare `<ratio>` overrides the element's natural ratio, `auto <ratio>` prefers the natural ratio and falls back to the specified one, and `auto`/no value uses the natural ratio. Not yet applied: the CSS Sizing §5.1 *transferred* automatic-minimum-size for flex/grid items (the min-content-through-the-ratio min size), and height→width on non-replaced inline-block/float boxes. |
| `object-fit` | [object-fit](https://developer.mozilla.org/en-US/docs/Web/CSS/object-fit) | Supported on every replaced element that renders content — `<img>` (raster and SVG source), `<object>` resolving to an image, `<video>` (its `poster` image, which PeachPDF renders since it can't play video), and inline `<svg>`: `fill` (default — stretch to the content box), `contain` (scale to fit, preserving aspect ratio, letterboxing the remainder), `cover` (scale to cover, preserving aspect ratio, cropping the overflow to the content box), `none` (the content's intrinsic size), and `scale-down` (the smaller of `none` and `contain`). |
| `object-position` | [object-position](https://developer.mozilla.org/en-US/docs/Web/CSS/object-position) | Supported on the same replaced elements as `object-fit`, sharing the [`background-position`](#backgrounds) grammar (keywords, lengths, percentages, and the edge-offset form). Positions the `object-fit`-sized content within the content box (and selects which part is visible when `cover`/`none` overflows). Defaults to `50% 50%` (center). |

#### Logical box-model properties

The [CSS logical box-model properties](https://developer.mozilla.org/en-US/docs/Web/CSS/CSS_logical_properties_and_values) are supported and resolve to their physical equivalents. PeachPDF lays out as `writing-mode: horizontal-tb` / `direction: ltr`, so the mapping is fixed: **block-start → top, block-end → bottom, inline-start → left, inline-end → right**.

| Logical property | Resolves to | Notes |
|------------------|-------------|-------|
| `margin-block` / `margin-inline` | `margin-top`+`margin-bottom` / `margin-left`+`margin-right` | 1–2 values (1 applies to both edges) |
| `margin-block-start` / `-end`, `margin-inline-start` / `-end` | `margin-top`/`-bottom`/`-left`/`-right` | single edge |
| `padding-block` / `padding-inline` (+ `-start`/`-end` longhands) | the physical `padding-*` edges | same 1–2-value / single-edge model as margin |
| `inset` | `top`+`right`+`bottom`+`left` | 1–4 values (the standard box-edge shorthand) |
| `inset-block` / `inset-inline` (+ `-start`/`-end` longhands) | the physical `top`/`right`/`bottom`/`left` edges | 1–2-value / single-edge |
| `border-block-start` / `-end`, `border-inline-start` / `-end` | the physical `border-top`/`-right`/`-bottom`/`-left` edge shorthands | `<width> <style> <color>` |
| `border-block` / `border-inline` | both block / both inline edges | one `<width> <style> <color>` applied to both edges |
| `border-block-width` / `-style` / `-color` (and `border-inline-*`) | the two physical edge width/style/color longhands | 1–2 values |
| `border-block-start-width` / `-style` / `-color` (all four edges) | the physical per-edge `border-*-width`/`-style`/`-color` longhands | single edge |

Because these map to the physical longhands, a logical and a physical declaration for the same edge cascade together (last-declared wins). When a declaration block is serialized back to CSS, the physical longhands are used — a logical shorthand is never reconstructed from them.

> **Limitation:** the logical→physical mapping is fixed to LTR / horizontal-tb; it does not vary with `direction` or `writing-mode`. Under `direction: rtl` (or a vertical writing mode) the inline/block edges are therefore *not* remapped as the spec requires — `margin-inline-start` always resolves to `margin-left`, never `margin-right`. This is inherent to PeachPDF's fixed layout orientation (see [`direction`](#positioning) support, which is itself partial).

### Borders

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `border` | [border](https://developer.mozilla.org/en-US/docs/Web/CSS/border) | Shorthand supported; also `border-top`, `border-right`, `border-bottom`, `border-left` |
| `border-width` | [border-width](https://developer.mozilla.org/en-US/docs/Web/CSS/border-width) | Shorthand and all four longhands supported |
| `border-style` | [border-style](https://developer.mozilla.org/en-US/docs/Web/CSS/border-style) | Shorthand and all four longhands supported; values: `none`, `solid`, `dashed`, `dotted`, `double`, `inset`, `outset`, `groove`, `ridge` |
| `border-color` | [border-color](https://developer.mozilla.org/en-US/docs/Web/CSS/border-color) | Shorthand and all four longhands supported |
| `border-collapse` | [border-collapse](https://developer.mozilla.org/en-US/docs/Web/CSS/border-collapse) | Full support for tables |
| `border-spacing` | [border-spacing](https://developer.mozilla.org/en-US/docs/Web/CSS/border-spacing) | Full support for tables |

### Border Radius

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `border-radius` | [border-radius](https://developer.mozilla.org/en-US/docs/Web/CSS/border-radius) | Shorthand; supports 1–4 values with optional `/` for elliptical radii (e.g. `10px / 20px`) |
| `border-top-left-radius` | [border-top-left-radius](https://developer.mozilla.org/en-US/docs/Web/CSS/border-top-left-radius) | Accepts `<length>` or `<percentage>`; optional second value sets the vertical radius independently |
| `border-top-right-radius` | [border-top-right-radius](https://developer.mozilla.org/en-US/docs/Web/CSS/border-top-right-radius) | Same as above |
| `border-bottom-right-radius` | [border-bottom-right-radius](https://developer.mozilla.org/en-US/docs/Web/CSS/border-bottom-right-radius) | Same as above |
| `border-bottom-left-radius` | [border-bottom-left-radius](https://developer.mozilla.org/en-US/docs/Web/CSS/border-bottom-left-radius) | Same as above |

Percentages are relative to the border-box width (horizontal radius) and height (vertical radius). Overlapping adjacent radii are automatically reduced proportionally per the CSS spec.

Known limitation: `double`/`groove`/`ridge` combined with `border-radius` on the same edge falls back to a single solid-colored stroke at the full border width — full rounded rendering of these three styles (two concentric arcs, or a two-tone beveled arc) is out of scope for CSS1 compliance.

### Box Shadow

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `box-shadow` | [box-shadow](https://developer.mozilla.org/en-US/docs/Web/CSS/box-shadow) | Comma-separated list of shadow layers, each `inset? <offset-x> <offset-y> <blur-radius>? <spread-radius>? <color>?`. Supports outset (drop) and `inset` shadows, negative offsets, negative spread, multiple layers, and an omitted color (which uses the element's own `color`, i.e. `currentColor`). Blur and spread may use any `<length>` unit (including `em`/`rem`). The first-listed layer paints on top. Honors `border-radius`. **Blur is a vector approximation** (PDF has no native blur filter): the falloff is rendered as concentric overlapping fills ramping opacity over the blur radius, which reads correctly for typical UI shadows but is not a true Gaussian. An outset shadow fills its whole shape behind the box rather than knocking out the box's own area, so it can show through a fully transparent element; a rounded `inset` shadow's inner edge is drawn with square corners. |

The first two lengths are the shadow's horizontal and vertical offset; the optional third is the blur radius (must be non-negative) and the optional fourth is the spread radius. Multiple shadows are layered, the first-listed drawn last (on top). An outset shadow paints behind the element's background; an `inset` shadow paints over the background, clipped to the padding box and fading inward.

Blur rendering: PDF has no native blur filter, so a blurred shadow is approximated with vector geometry — a stack of concentric, overlapping shape fills whose accumulated alpha ramps the shadow color from opaque (the interior) to transparent (the outer blur edge), with corners that round off over the blur radius. This reads correctly for typical drop-shadow and soft/neumorphic-shadow use; it is not a true Gaussian blur, so it will not match a browser pixel-for-pixel. Known limitations: an outset shadow is not knocked out from behind the element's own border-box (it relies on an opaque background to hide the overlap, so it can show through a transparent element), a rounded `inset` shadow's inner (hole) edge is drawn with square corners, and `text-shadow` is separate and still unsupported (see [Unsupported CSS Features](#unsupported-css-features)).

### Transforms

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `transform` | [transform](https://developer.mozilla.org/en-US/docs/Web/CSS/transform) | Supports `matrix()`, `matrix3d()`, `translate()`/`translateX()`/`translateY()`/`translateZ()`/`translate3d()`, `scale()`/`scaleX()`/`scaleY()`/`scaleZ()`/`scale3d()`, `rotate()`/`rotateX()`/`rotateY()`/`rotateZ()`/`rotate3d()`, and `skew()`/`skewX()`/`skewY()`. Multiple functions may be chained in one value; they compose per spec (the last-listed function is applied first, closest to the element). Not inherited. `perspective()` is not supported — see [Unsupported CSS Features](#unsupported-css-features). |
| `transform-origin` | [transform-origin](https://developer.mozilla.org/en-US/docs/Web/CSS/transform-origin) | 1–3 values (`<length>`/`<percentage>`/keyword for X and Y, plain `<length>` for Z). X/Y percentages resolve against the border-box. Defaults to `50% 50% 0`. Not inherited. |

3D transform functions are composed as a genuine 4×4 matrix and projected onto the element's own flat plane for painting into the PDF content stream. This projection is always mathematically exact — `translate3d()`/`scale3d()`/`rotateX()`/`rotateY()`/`rotate3d()`/`matrix3d()` all render as true, lossless 2D transforms of the flattened element.

### Opacity

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `opacity` | [opacity](https://developer.mozilla.org/en-US/docs/Web/CSS/opacity) | Full support; not inherited (it composites the element and its whole subtree as a group, per spec). Rendered as a genuine, isolated PDF transparency group — the element's subtree is painted into an offscreen Form XObject and flattened, then that single flattened result is composited onto the page at the given alpha, so overlapping content within the element (e.g. two overlapping semi-transparent children) doesn't double-darken where it overlaps. |
| `clip-path` | [clip-path](https://developer.mozilla.org/en-US/docs/Web/CSS/clip-path) | Basic shapes are supported: `polygon()` (with an optional `nonzero`/`evenodd` fill rule), `inset()` (1–4 edge offsets), `circle()` and `ellipse()` (with a `<length-percentage>` or `closest-side`/`farthest-side` radius and an optional `at <position>`), and `none`. The shape is resolved against the element's **border-box** and clips the whole element (background, border, content, and descendants); it is transformed together with any `transform` on the element. A `calc()` expression is accepted as a `polygon()` coordinate (resolved against the reference box at layout time). Invalid values are dropped at parse time. Not supported: `inset()`'s `round <border-radius>` corners (the clip is rectangular), a `<geometry-box>` reference-box keyword other than the default border-box, and `path()`/`url()` (SVG) references. |

### Backgrounds

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `background` | [background](https://developer.mozilla.org/en-US/docs/Web/CSS/background) | Shorthand supported; all longhand components are parsed and applied |
| `background-color` | [background-color](https://developer.mozilla.org/en-US/docs/Web/CSS/background-color) | Full support |
| `background-image` | [background-image](https://developer.mozilla.org/en-US/docs/Web/CSS/background-image) | URL, data URI, and all CSS gradient functions: `linear-gradient()`, `radial-gradient()`, `conic-gradient()`, `repeating-linear-gradient()`, `repeating-radial-gradient()`, and `repeating-conic-gradient()`; all accept multi-stop gradients with absolute-length or percentage stop positions, two-position hard-stop shorthand, color hints, and `rgba()`/alpha transparency; radial gradients support `circle`/`ellipse` shape, `at <position>` centering, explicit length radii, and all four size keywords; conic gradients support `from <angle>` and `at <position>`, and a `calc()` expression is accepted as an angular color-stop position (e.g. `calc(1turn * 0.35)`); all gradient functions support CSS Color Level 4 color-space interpolation (`in srgb`, `in srgb-linear`, `in display-p3`, `in lab`, `in oklab`, `in lch`, `in oklch`, `in hsl`, `in hwb`, `in xyz`/`in xyz-d65`, `in xyz-d50`) with polar hue-interpolation methods (`shorter hue`, `longer hue`, `increasing hue`, `decreasing hue`); the `in <color-interpolation-method>` prelude is validated against [CSS Images 4 §3.1](https://drafts.csswg.org/css-images-4/#color-interpolation-method) and an invalid one is dropped at parse time (an unknown color space, a hue method on a rectangular space, or a malformed direction). The wide-gamut RGB interpolation spaces `a98-rgb`, `prophoto-rgb`, and `rec2020` are valid CSS but not supported, and a gradient that requests one is dropped rather than approximated. A url() source that is an SVG (`.svg` file, `data:image/svg+xml`) renders as real vector content — a reusable PDF Form XObject positioned/sized/repeated via `background-position`/`background-size`/`background-repeat` exactly like a raster image — not rasterized; see [Supported SVG Features](supported-svg-features.md) |
| `background-position` | [background-position](https://developer.mozilla.org/en-US/docs/Web/CSS/background-position) | Full support: keywords, lengths, percentages, `calc()`, and the 4-value edge-offset syntax (e.g. `right 10px bottom 20px`); applies to url() images and gradients alike. Comma-separated multi-layer values cycle against the number of `background-image` layers (a single value applies to every layer) |
| `background-size` | [background-size](https://developer.mozilla.org/en-US/docs/Web/CSS/background-size) | Full support: `auto`, `cover`, `contain`, lengths, percentages, and `calc()`, for both url() images and gradients — a gradient with an explicit size smaller/larger than the box is rendered once and then positioned/repeated exactly like an image. Comma-separated multi-layer values cycle against the number of `background-image` layers |
| `background-repeat` | [background-repeat](https://developer.mozilla.org/en-US/docs/Web/CSS/background-repeat) | Full support: all keywords (`repeat`, `no-repeat`, `repeat-x`, `repeat-y`, and the 1/2-value forms). Comma-separated multi-layer values cycle against the number of `background-image` layers |
| `background-origin` | [background-origin](https://developer.mozilla.org/en-US/docs/Web/CSS/background-origin) | Full support; `border-box`, `padding-box`, `content-box`. Comma-separated multi-layer values cycle against the number of `background-image` layers |
| `background-attachment` | [background-attachment](https://developer.mozilla.org/en-US/docs/Web/CSS/background-attachment) | `scroll` (default) and `fixed` are supported. Since a PDF page has no scrolling viewport, `fixed` is given the paginated-media meaning of CSS Paged Media: the background positioning area is the page box rather than the element's own box, and it repeats identically (page-anchored) on every page, mirroring how `position: fixed` already behaves. `background-clip` is unaffected, so the image is still only ever visible within the element's own box. Comma-separated multi-layer values cycle against the number of `background-image` layers |
| `background-clip` | [background-clip](https://developer.mozilla.org/en-US/docs/Web/CSS/background-clip) | Full support; `border-box`, `padding-box`, `content-box`. Comma-separated multi-layer values cycle against the number of `background-image` layers; when there are multiple values, `background-color` uses the last (bottom-most) one, per spec |

#### Canvas background (`<html>`/`<body>`)

Per CSS2.1 §14.2, the root element's background doesn't just paint its own box — it propagates to fill the whole *canvas* (here: every page). PeachPDF resolves this once per document: `<body>`'s own background (`background-color` and/or `background-image` layers) is used if it declares one; otherwise `<html>`'s; otherwise no canvas fill happens at all. Whichever element was used for the canvas fill isn't separately re-painted at its own (possibly much smaller than a page) laid-out rect — the canvas fill already covers it. A non-`<body>`/`<html>` element's own background (e.g. a `<div>`) is unaffected and continues to paint normally. The fill repeats identically on every page the document spans. `@page` background isn't implemented yet, so there's no additional precedence tier there currently.

### Color & Typography

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `color` | [color](https://developer.mozilla.org/en-US/docs/Web/CSS/color) | Full support for named colors, hex, `rgb()`/`rgba()` in both the legacy comma form (`rgb(r, g, b)`, `rgba(r, g, b, a)`) and the CSS Color Level 4 space-separated form with an optional slash alpha (`rgb(r g b)`, `rgb(r g b / a)`, `rgba(r g b / a%)`), `hsl()`/`hsla()`, `hwb()`, and the CSS Color Level 4 wide-gamut functions `oklch()`, `oklab()`, `lab()`, and `lch()` (number or percentage components, an angle hue with any unit for the polar forms, and an optional `/ <alpha>`), all resolved to sRGB. [`color-mix()`](https://developer.mozilla.org/en-US/docs/Web/CSS/color_value/color-mix) is supported (`color-mix(in <space>, <color> <p>?, <color> <p>?)`) — premultiplied-alpha mixing in any of the interpolation spaces above, so the common opacity-modifier shape `color-mix(in oklab, <color> 50%, transparent)` yields that color at half alpha. These color functions work anywhere a `<color>` is accepted (`color`, `background-color`, the `background`/`border` shorthands, gradient color stops). Known gap: a **hex** color used directly as a `color-mix()` operand only resolves if it is *letter-leading* (e.g. `#e11d48`); a digit-leading hex operand (e.g. `#2563eb`) is not recognized inside `color-mix()` — use a named color, `rgb()`, or an `oklch()`/`var()` operand instead. `color()`, and CSS Color 5 relative-color syntax, are not supported |
| `font` | [font](https://developer.mozilla.org/en-US/docs/Web/CSS/font) | Shorthand supported; all components are parsed |
| `font-family` | [font-family](https://developer.mozilla.org/en-US/docs/Web/CSS/font-family) | Full support. Generic families (`serif`, `sans-serif`, `monospace`, `cursive`, `fantasy`) and `system-ui` resolve to a real installed substitute matching actual Chromium behavior per platform (hardcoded specific families on Windows/macOS/Android, delegated to the OS's own fontconfig on Linux) — see [Fonts](usage-examples.md#fonts) for the full per-platform table. Every mapping is verified against the system's actually-installed fonts before use, falling back to the platform default font otherwise, so a requested substitute that isn't present never silently resolves to an arbitrary, unrelated font |
| `font-size` | [font-size](https://developer.mozilla.org/en-US/docs/Web/CSS/font-size) | Full support including absolute sizes (`medium`, `large`, etc.), relative sizes (`smaller`, `larger`), lengths, and percentages |
| `font-stretch` | [font-stretch](https://developer.mozilla.org/en-US/docs/Web/CSS/font-stretch) | The 9 CSS Fonts Level 3 keywords (`ultra-condensed` … `normal` … `ultra-expanded`); inherited and cascaded, and consulted for real face selection when a family has multiple registered faces at different stretch values (e.g. two `@font-face` rules with different `font-stretch` descriptors) — nearest-stretch matching per CSS Fonts Level 4 §5.2. Percentage/range values (the variable-font syntax) are not supported |
| `font-style` | [font-style](https://developer.mozilla.org/en-US/docs/Web/CSS/font-style) | `normal`, `italic`, `oblique`, and CSS Fonts Level 4's `oblique <angle>` (e.g. `oblique 10deg`) — when no real oblique/italic face is available and the renderer has to synthesize one, an explicitly declared angle drives the exact synthesized shear amount instead of a fixed default |
| `font-variant` | [font-variant](https://developer.mozilla.org/en-US/docs/Web/CSS/font-variant) | `normal` and `small-caps` are parsed and actually rendered: since PeachPDF has no OpenType shaping engine for real `smcp`/`c2sc` glyph substitution, `small-caps` is synthesized — originally-lowercase letters are upper-cased and drawn at a reduced size relative to the rest of the run. Non-standard/proprietary function-token values (e.g. Prince's `prince-opentype(...)`) are not recognized and resolve to `normal` |
| `font-weight` | [font-weight](https://developer.mozilla.org/en-US/docs/Web/CSS/font-weight) | Keyword (`bold`, `normal`) and numeric (`1`–`1000`) values. `bolder`/`lighter` step relative to the parent's own resolved weight per the CSS2.1 §15.6 worked table, not a fixed always-bold/always-normal result. Face selection uses real CSS Fonts Level 4 §5.2 nearest-weight matching (not just an exact Regular/Bold pick) among every face registered for a family; when no face close enough to the request exists, a faux-bold is synthesized (fill+stroke render mode) rather than rendering with no visual distinction |
| `line-height` | [line-height](https://developer.mozilla.org/en-US/docs/Web/CSS/line-height) | Full support |
| `vertical-align` | [vertical-align](https://developer.mozilla.org/en-US/docs/Web/CSS/vertical-align) | `baseline`, `sub`, `super`, `top`, `middle`, `bottom`, `text-top`, `text-bottom`, and length/percentage values — full support for any inline-level box relative to its line box, not just table cells (which use a separate, table-specific alignment algorithm — see [Tables](#tables)) |

#### Per-character font matching and coverage fallback

Font matching is per-codepoint, following the [CSS Fonts 4 matching algorithm](https://developer.mozilla.org/en-US/docs/Web/CSS/@font-face/unicode-range): each character is resolved to the first family in the `font-family` list whose face both covers that character (via its `@font-face` [`unicode-range`](https://developer.mozilla.org/en-US/docs/Web/CSS/@font-face/unicode-range), or, for a system/rangeless font, its actual `cmap` coverage) and contains a glyph for it. A run whose primary font lacks a glyph for some character therefore falls back to the next declared family that covers it, and multiple `@font-face` rules sharing one `font-family` but declaring different `unicode-range` subsets each supply their own characters.

Supplementary-plane (astral) characters above U+FFFF — where nearly all emoji live — are supported: they resolve through the font's format-12 `cmap` subtable, both for glyph lookup and for coverage-based fallback, and render the font's monochrome `glyf`/`CFF` outline.

**Color** emoji is supported for **`COLR`/`CPAL`** fonts (both COLR version 0 and version 1), including the COLR&nbsp;v1 build of Noto Color Emoji. Rather than embedding the font program, PeachPDF decodes each color glyph and draws it as native PDF vector content: COLR&nbsp;v0 layers are filled with their `CPAL` palette colors bottom-to-top, and COLR&nbsp;v1 paint graphs are composed — solid fills, linear/radial/conic ([`sweep`](https://learn.microsoft.com/en-us/typography/opentype/spec/colr#format-8-paintsweepgradient)) gradients, affine transforms, glyph clips, and separable/HSL blend-mode compositing. Because color glyphs are painted as vectors, they are not selectable text and carry no `/ToUnicode` mapping.

Still unsupported (a color-emoji font using only these renders blank or monochrome): bitmap color tables **`CBDT`/`CBLC`** (the classic bitmap build of Noto Color Emoji) and **`sbix`** (Apple Color Emoji), **SVG-in-OpenType** (`SVG ` table), and `COLR` glyphs in **CFF**-flavored fonts (the vector path decodes `glyf` outlines only). COLR&nbsp;v1 variable (`PaintVar*`) paints render at their default instance, and Porter-Duff-only composite modes that PDF cannot express fall back to source-over.

Remaining boundaries: fallback walks the declared `font-family` stack (plus the default font) but not a last-resort scan of every installed font, so a character no declared family covers still shows a missing-glyph box; variation-selector sequences (`cmap` format 14) are not applied; and a font offering *only* a format-12 subtable (with no format-4 subtable) is not used.

### Text Layout

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `direction` | [direction](https://developer.mozilla.org/en-US/docs/Web/CSS/direction) | `ltr` and `rtl`. `rtl` mirrors the visual order of whole word boxes on a line; there is no Unicode Bidi Algorithm (no per-character reordering or mixed-direction resolution), and `unicode-bidi` is parsed but has no layout effect. See [Text shaping](#text-shaping) |
| `hyphens` | [hyphens](https://developer.mozilla.org/en-US/docs/Web/CSS/hyphens) | `none`, `manual`, `auto` are parsed, cascaded, and inherited. `manual` and `auto` both honor an explicit soft hyphen (`&shy;`/U+00AD) as a line-break opportunity, rendering a literal `-` glyph only when that break is actually used. `auto` additionally performs real pattern-based automatic hyphenation (Liang's algorithm) for ~73 languages — see the note below the table for language coverage and exclusions |
| `letter-spacing` | [letter-spacing](https://developer.mozilla.org/en-US/docs/Web/CSS/letter-spacing) | Full support, including negative values; spacing is added after every character including the last (realized via the PDF `Tc` character-spacing operator, which applies to every glyph shown), and one letter-spacing unit is folded into the following inter-word gap so adjacent words never collapse together. Per CSS Text Level 3 §7.2, spacing is not suppressed at the start/end of a *word* — only at the start/end of a *line*, which this engine does not special-case, leaving each line's own leading/trailing edge with a sub-pixel, visually negligible extra inset |
| `text-align` | [text-align](https://developer.mozilla.org/en-US/docs/Web/CSS/text-align) | `left`, `right`, `center`, `justify` |
| `text-decoration` | [text-decoration](https://developer.mozilla.org/en-US/docs/Web/CSS/text-decoration) | Shorthand supported |
| `text-decoration-color` | [text-decoration-color](https://developer.mozilla.org/en-US/docs/Web/CSS/text-decoration-color) | Full support |
| `text-decoration-line` | [text-decoration-line](https://developer.mozilla.org/en-US/docs/Web/CSS/text-decoration-line) | `none`, `underline`, `overline`, `line-through`, and any space-separated combination of them (e.g. `underline overline` draws both lines) |
| `text-decoration-style` | [text-decoration-style](https://developer.mozilla.org/en-US/docs/Web/CSS/text-decoration-style) | `solid`, `dashed`, `dotted`, `double`, `wavy` |
| `text-indent` | [text-indent](https://developer.mozilla.org/en-US/docs/Web/CSS/text-indent) | Full support |
| `text-transform` | [text-transform](https://developer.mozilla.org/en-US/docs/Web/CSS/text-transform) | `none`, `uppercase`, `lowercase`, `capitalize`; `full-width`/`full-size-kana` not supported |
| `white-space` | [white-space](https://developer.mozilla.org/en-US/docs/Web/CSS/white-space) | `normal`, `nowrap`, `pre`, `pre-wrap`, `pre-line` |
| `word-break` | [word-break](https://developer.mozilla.org/en-US/docs/Web/CSS/word-break) | `normal`, `break-all`, `keep-all` |
| `word-spacing` | [word-spacing](https://developer.mozilla.org/en-US/docs/Web/CSS/word-spacing) | Full support |

#### Text shaping

PeachPDF does not perform OpenType text shaping. Each character is mapped 1:1 from its Unicode codepoint to a glyph via the font's `cmap`; there is no [OpenType Layout](https://learn.microsoft.com/en-us/typography/opentype/spec/ttochap1) (`GSUB`/`GPOS`) stage. In practice this means:

- **Ligatures** are not formed automatically. A precomposed ligature that has its own codepoint (e.g. `ﬁ`, U+FB01) renders if the source text actually contains that codepoint and the font maps it; but writing `f` + `i` yields two separate glyphs, and designer-defined ligatures that have no codepoint are unreachable. `font-variant-ligatures` is not supported.
- **Complex-script shaping** (Arabic joining and initial/medial/final/isolated contextual forms, Indic reordering, mark positioning) is not performed — each codepoint uses its nominal glyph regardless of its neighbours.
- **Bidirectional text**: `direction: rtl` mirrors the visual order of whole word boxes only; there is no [Unicode Bidi Algorithm](https://www.unicode.org/reports/tr9/) (no per-character reordering, no mixed-direction resolution), and `unicode-bidi` has no layout effect.

Kerning (`GPOS`) is likewise not applied.

#### `hyphens: auto` language coverage

`auto` performs real pattern-based automatic hyphenation only when the text's language is known: PeachPDF reads `<html lang="...">` (`HtmlContainerInt.DocumentLanguage`), and a calling application can supply `PdfGenerateConfig.DefaultLanguage` as a fallback for documents that declare no language of their own. With no language available from either source, `auto` behaves like `manual` (no algorithmic hyphenation) rather than guessing. A declared language resolves to the closest available pattern set — the tag itself, then progressively shorter subtag prefixes (e.g. `de-AT` falls back to `de`'s default variant, `de-1996`) — and a language with no match anywhere in that chain silently falls back to the same no-op rather than erroring.

Pattern data is sourced from CTAN's [hyph-utf8](https://ctan.org/pkg/hyph-utf8) package (see [tools/Update-HyphenationPatterns.ps1](https://github.com/jhaygood86/PeachPDF/blob/main/tools/Update-HyphenationPatterns.ps1) for the reproducible download/build pipeline). PeachPDF ships only permissively licensed pattern sets (MIT/LPPL/BSD-style/public-domain), consistent with the library's own license — **73 languages/scripts** are supported:

Afrikaans (`af`), Albanian (`sq`), Ancient Greek (`grc`), Assamese (`as`), Basque (`eu`), Belarusian (`be`), Bengali (`bn`), British English (`en-GB`), Bulgarian (`bg`), Catalan (`ca`), Chinese pinyin/Mandarin romanization (`zh-Latn-pinyin`), Church Slavonic (`cu`), Coptic (`cop`), Croatian (`hr`, via Serbo-Croatian Latin patterns), Danish (`da`), Dutch (`nl`), Esperanto (`eo`), Estonian (`et`), Finnish (`fi`, plus a `fi-x-school` school-method variant), French (`fr`), Friulan (`fur`), Galician (`gl`), Georgian (`ka`), German — traditional (`de-1901`), reformed/modern (`de-1996`, the default for bare `de`), and Swiss traditional (`de-ch-1901`, the default for `de-CH`), Gujarati (`gu`), Hindi (`hi`), American English (`en-US`, the default for bare `en`), Icelandic (`is`), Interlingua (`ia`), Irish (`ga`), Italian (`it`), Kannada (`kn`), Kazakh (`kk`), Kurmanji/Northern Kurdish (`kmr`, also the default for bare `ku`), Latin — modern/medieval (`la`), classical (`la-x-classic`), and liturgical (`la-x-liturgic`) variants, Lithuanian (`lt`), Malayalam (`ml`), Marathi (`mr`), Modern Greek — monotonic (`el-monoton`, the default for bare `el`) and polytonic (`el-polyton`), Mongolian, Cyrillic script (`mn-Cyrl`, the default for bare `mn`), Norwegian Bokmål (`nb`, also the default for bare `no`), Norwegian Nynorsk (`nn`), Occitan (`oc`), Oriya (`or`), Panjabi (`pa`), Pāli (`pi`), Piedmontese (`pms`), Polish (`pl`), Portuguese (`pt`, shared for `pt-BR`/`pt-PT`), Romansh (`rm`), Russian (`ru`), Sanskrit and Prakrit, Latin transliteration (`sa`), Serbo-Croatian — Cyrillic (`sh-Cyrl`, also the default for bare `sr`) and Latin (`sh-Latn`, also the default for bare `bs`) scripts, Slovak (`sk`), Slovenian (`sl`), Spanish (`es`), Swedish (`sv`), Tamil (`ta`), Telugu (`te`), Thai (`th`), Turkish (`tr`), Turkmen (`tk`), Ukrainian (`uk`), Upper Sorbian (`hsb`), Welsh (`cy`), and languages written in the Ethiopic script (`mul-Ethi`, covering Amharic `am` and Tigrinya `ti`).

**Explicitly excluded** — these languages have hyphenation patterns in the upstream hyph-utf8 package, but PeachPDF does not ship them because the pattern file itself is GPL/LGPL-licensed (copyleft, stronger obligations than PeachPDF's own license) or states no license at all. `hyphens: auto` is a silent no-op for these languages exactly as if no pattern data existed for them at all:

| Language | Tag | Reason |
|---|---|---|
| Armenian | `hy` | LGPL 3.0 |
| Czech | `cs` | GPL 2+ |
| Hungarian | `hu` | LGPL 2.1 |
| Indonesian | `id` | GPL 2 |
| Latvian | `lv` | GPL 2+ |
| Macedonian | `mk` | GPL |
| Romanian | `ro` | No license stated in the source file |
| Serbian, Cyrillic script | `sr-Cyrl` | GPL |

Regenerating the pattern set (`tools/Update-HyphenationPatterns.ps1`) re-checks each language's license against the same permissive-only rule on every run, so a language is only ever added back automatically if upstream re-licenses it.

### Display & Layout

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `display` | [display](https://developer.mozilla.org/en-US/docs/Web/CSS/display) | `block`, `inline`, `inline-block`, `none`, `flex`, `inline-flex`, `grid`, `inline-grid`, `table`, `table-row`, `table-cell`, `table-header-group`, `table-footer-group`, `table-row-group`, `table-column`, `table-column-group`, `table-caption`, `list-item` |
| `position` | [position](https://developer.mozilla.org/en-US/docs/Web/CSS/position) | `static`, `relative`, `absolute`, `fixed` (renders ignoring page margins), `sticky` (treated as `relative` in PDF output since there is no scroll) |
| `float` | [float](https://developer.mozilla.org/en-US/docs/Web/CSS/float) | `left`, `right`, `none` |
| `clear` | [clear](https://developer.mozilla.org/en-US/docs/Web/CSS/clear) | `left`, `right`, `both`, `none` |
| `overflow` | [overflow](https://developer.mozilla.org/en-US/docs/Web/CSS/overflow) | Affects clipping regions; there is no interactive scrolling in PDF output |
| `visibility` | [visibility](https://developer.mozilla.org/en-US/docs/Web/CSS/visibility) | `visible`, `hidden` |
| `z-index` | [z-index](https://developer.mozilla.org/en-US/docs/Web/CSS/z-index) | Full support for positioned elements |

#### Atomic inline-level layout is approximated, not fully atomic

An `inline-block` box's text flows through the surrounding inline formatting context rather
than being laid out as one opaque unit. Its content is correctly inset by its own
border+padding (its label sits inside the padding box, and the line reserves the full padding
box height), but two knock-on gaps remain:

- An explicit `height` on an inline-flowed `inline-block` does not size the line — the line's
  height comes from the flowed content plus padding/border, so
  `<span style="display: inline-block; height: 100px">x</span>` reserves only its natural text
  height, not 100px (CSS2.1 §10.8.1 expects the atomic box's margin box to size the line).
- `display: inline-table` is not atomic in inline flow at all: when it participates in a
  surrounding inline formatting context its content flows like ordinary inline text (the table
  layout algorithm only runs when the box is laid out at block level).

### Stacking Context

Paint order follows the CSS [stacking context](https://developer.mozilla.org/en-US/docs/Web/CSS/Guides/Positioned_layout/Stacking_context) model. A new stacking context is established by:

- the document root
- `position: relative` or `absolute` with a `z-index` other than `auto`
- `position: fixed` or `sticky` (unconditionally, regardless of `z-index`)
- a flex item (a direct child of a `display: flex`/`inline-flex` container) with a `z-index` other than `auto`
- `opacity` less than 1
- a `transform` other than `none`

Elements are painted as one self-contained, atomic unit once they establish a stacking context — for example, everything inside an `opacity: 0.5` box (including any absolutely-positioned descendants) fades together as a single composited group, and a `z-index`-ordered element's own descendants are ordered independently of the rest of the page.

Within one stacking context, normal-flow content paints in CSS2.1 Appendix E order: in-flow block-level descendants first, then non-positioned floats, then in-flow inline-level descendants (text and inline replaced content, e.g. an inline `<img>`/`<object>`), then positioned descendants. A plain block-level box whose entire content is inline (a wrapper `<div>` around nothing but an inline image, for example) is treated as belonging to the inline pass itself, since painting it is what paints that inline content. This local ordering is preserved when a float is hoisted past its immediate container as long as that container is itself positioned (`position: relative`/`absolute`/`fixed`/`sticky`), even without an explicit `z-index` of its own. The one remaining gap: a float whose immediate container is a plain, non-positioned wrapper (no `position` at all) still hoists all the way to the nearest true stacking-context ancestor instead of preserving local order, so it may not paint correctly relative to non-hoisted block/inline siblings at its original nesting level.

The following triggers from the CSS specification are **not** supported, since the underlying properties themselves have no effect in PeachPDF (see [Unsupported CSS Features](#unsupported-css-features)): `isolation`, `will-change`, `mix-blend-mode`, `contain`, and 3D `perspective`.

An element that needs to escape a plain (non-stacking-context) ancestor to compete for z-order at its true enclosing stacking context — for example, a `z-index`-ordered element nested inside a plain `position: absolute` wrapper that has no `z-index` of its own — is still correctly clipped by every `overflow: hidden` ancestor it passes through along the way, including when multiple such ancestors are nested.

### Flexbox

CSS Flexbox Level 1 (`display: flex` / `inline-flex`) is supported, including multi-line wrapping, all alignment properties, and auto margins on the main axis. Replaced elements (`<img>`, inline `<svg>`) work as flex items, including when mixed with block-level siblings — per CSS Flexbox §4, a run of inline-level content sharing a flex container with a block-level sibling is wrapped in an anonymous flex item, which is measured, positioned, and painted the same as any tagged item.

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `flex-direction` | [flex-direction](https://developer.mozilla.org/en-US/docs/Web/CSS/flex-direction) | `row`, `row-reverse`, `column`, `column-reverse` |
| `flex-wrap` | [flex-wrap](https://developer.mozilla.org/en-US/docs/Web/CSS/flex-wrap) | `nowrap`, `wrap`, `wrap-reverse` |
| `flex-flow` | [flex-flow](https://developer.mozilla.org/en-US/docs/Web/CSS/flex-flow) | Shorthand for `flex-direction` + `flex-wrap`, in either order (`wrap row` as well as `row wrap`) |
| `justify-content` | [justify-content](https://developer.mozilla.org/en-US/docs/Web/CSS/justify-content) | `flex-start`, `flex-end`, `center`, `space-between`, `space-around`, `space-evenly` |
| `align-items` | [align-items](https://developer.mozilla.org/en-US/docs/Web/CSS/align-items) | `flex-start`, `flex-end`, `center`, `stretch`, `baseline`. Applies in **both** directions: a non-`stretch` alignment shrink-wraps each item to its fit-content cross size and positions it (a row item vertically, a column item horizontally), while `stretch` (the default) fills the line's cross size. `baseline` aligns items by their first text baseline and is only meaningful for row-direction flex — column-direction flex falls back to `flex-start` |
| `align-content` | [align-content](https://developer.mozilla.org/en-US/docs/Web/CSS/align-content) | `flex-start`, `flex-end`, `center`, `space-between`, `space-around`, `space-evenly`, `stretch` |
| `align-self` | [align-self](https://developer.mozilla.org/en-US/docs/Web/CSS/align-self) | Same values as `align-items`, plus `auto` |
| `order` | [order](https://developer.mozilla.org/en-US/docs/Web/CSS/order) | Full support |
| `flex-grow` | [flex-grow](https://developer.mozilla.org/en-US/docs/Web/CSS/flex-grow) | Full support |
| `flex-shrink` | [flex-shrink](https://developer.mozilla.org/en-US/docs/Web/CSS/flex-shrink) | Full support |
| `flex-basis` | [flex-basis](https://developer.mozilla.org/en-US/docs/Web/CSS/flex-basis) | Length, percentage, `auto`, and `content` values are supported |
| `flex` | [flex](https://developer.mozilla.org/en-US/docs/Web/CSS/flex) | Shorthand for `flex-grow`, `flex-shrink`, `flex-basis`, including the `none` and `auto` keywords |
| `gap` / `row-gap` / `column-gap` | [gap](https://developer.mozilla.org/en-US/docs/Web/CSS/gap) | Full support on flex containers |
| `margin` auto values | [margin](https://developer.mozilla.org/en-US/docs/Web/CSS/margin) | `auto` on a main-axis margin absorbs free space on that line (e.g. `margin-left: auto` to push a flex item to the end) |

### Grid

CSS Grid Layout (`display: grid` / `inline-grid`) is supported, including explicit and implicit track sizing, line-based placement with spans, named grid lines and `grid-template-areas`, auto-placement, box/content alignment (including block-axis `baseline` item alignment), and **subgrid** ([CSS Grid Level 2 §9](https://developer.mozilla.org/en-US/docs/Web/CSS/CSS_grid_layout/Subgrid)). Track sizes may be lengths, percentages, `fr` flexible lengths, `auto`, `min-content`/`max-content`, `minmax()`, `fit-content()`, `repeat()` — including `repeat(auto-fill, …)` / `repeat(auto-fit, …)`, so the common responsive-card idiom `repeat(auto-fill, minmax(200px, 1fr))` works — and a `calc()` (or `min()`/`max()`/`clamp()`) length, both on its own and inside `minmax()`/`fit-content()`/`repeat()`. `fr` tracks resolve via the standard flex-fraction algorithm (honoring `minmax()` floors); intrinsic tracks size to their items' content, distinguishing `min-content` (the longest unbreakable word) from `max-content` (the full unwrapped line), and an item spanning several intrinsic tracks distributes its content contribution across them so a spanned track does not collapse to zero.

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `grid-template-columns` / `grid-template-rows` | [grid-template-columns](https://developer.mozilla.org/en-US/docs/Web/CSS/grid-template-columns) | `<track-list>`: lengths, `%`, `fr`, `auto`, `min-content`, `max-content`, `minmax()`, `fit-content()`, a `calc()`/`min()`/`max()`/`clamp()` length (bare or inside `minmax()`/`fit-content()`/`repeat()`), `repeat(<n>, …)`, `repeat(auto-fill \| auto-fit, …)`, and named lines `[name]` (outside `repeat()`); plus `subgrid` (Level 2 §9) with an optional `[name]` line-name list — the axis adopts the parent grid's spanned tracks. A percentage **row** track against an indefinite container height is treated as `auto` (content-sized) per §7.2.1 |
| `grid-template-columns: subgrid` / `grid-template-rows: subgrid` | [subgrid](https://developer.mozilla.org/en-US/docs/Web/CSS/CSS_grid_layout/Subgrid) | A grid item that is itself a grid adopts its parent's tracks along the subgridded axis (on either or both axes): the item's own children align to the parent's grid lines, and — for auto parent tracks — the subgrid's content grows the shared tracks so multiple subgrids' rows line up. With no parent grid, `subgrid` behaves as `none`. Not honored: subgrid margin/border/padding insetting the adopted tracks, parent line-name inheritance into the subgrid, subgrid content contributing to the parent's auto *column* sizing, and `subgrid` in the `grid`/`grid-template` shorthands |
| `grid-template-areas` | [grid-template-areas](https://developer.mozilla.org/en-US/docs/Web/CSS/grid-template-areas) | The ASCII-art named-area template (each area must be a single filled rectangle; `.`/`...` are empty cells). Each area contributes implicit `name-start`/`name-end` lines on both axes, and establishes the explicit row/column counts |
| `grid-auto-columns` / `grid-auto-rows` | [grid-auto-rows](https://developer.mozilla.org/en-US/docs/Web/CSS/grid-auto-rows) | Sizes implicitly-created tracks; a `<track-size>+` list cycled across the implicit tracks |
| `grid-auto-flow` | [grid-auto-flow](https://developer.mozilla.org/en-US/docs/Web/CSS/grid-auto-flow) | `row`, `column`, and the `dense` packing keyword |
| `grid-column-start` / `grid-column-end` / `grid-row-start` / `grid-row-end` | [grid-row-start](https://developer.mozilla.org/en-US/docs/Web/CSS/grid-row-start) | Line-based placement: `auto`, an integer line (negatives count from the end edge), `span <n>`, a named line (a `[name]` line or an area's `name-start`/`name-end` edge), or `<name> <n>` for the Nth line with that name. An item explicitly placed on a line past the explicit grid (e.g. `grid-column: 5` in a 3-column grid) generates the implicit tracks it needs on that axis rather than being clamped into range. `span <name>` is not supported |
| `grid-column` / `grid-row` / `grid-area` | [grid-area](https://developer.mozilla.org/en-US/docs/Web/CSS/grid-area) | The slash-separated line forms plus the named forms — a bare area name (`grid-area: main`, `grid-column: main`) fills the whole named area via the custom-ident omitted-value copy rule |
| `grid-template` | [grid-template](https://developer.mozilla.org/en-US/docs/Web/CSS/grid-template) | The §7.4 shorthand for `grid-template-rows`/`-columns`/`-areas`: `none`, the `<rows> / <columns>` form, and the ASCII-art areas form `[<line-names>? <string> <track-size>? <line-names>?]+ [/ <columns>]?` (a row with no explicit track size gets `auto`) |
| `grid` | [grid](https://developer.mozilla.org/en-US/docs/Web/CSS/grid) | The §7.8 shorthand: a `<grid-template>`, or an auto-flow form (`<rows> / [auto-flow && dense?] <auto-columns>?` or `[auto-flow && dense?] <auto-rows>? / <columns>`). Every longhand it doesn't set is reset to its initial value |
| `justify-items` / `align-items` | [justify-items](https://developer.mozilla.org/en-US/docs/Web/CSS/justify-items) | Default alignment of items within their cell: `start`, `end`, `center`, `stretch` (default), `normal`. `align-items: baseline` (block axis) aligns the items in a row on a shared first text baseline; `justify-items: baseline` (inline axis) falls back to `start` (baseline is undefined on the inline axis without a vertical writing mode) |
| `justify-self` / `align-self` | [justify-self](https://developer.mozilla.org/en-US/docs/Web/CSS/justify-self) | A grid item's own cell alignment; `auto`/`normal` inherit the container's `*-items` value |
| `justify-content` / `align-content` | [justify-content](https://developer.mozilla.org/en-US/docs/Web/CSS/justify-content) | Distributes leftover container space among the tracks: `start`, `end`, `center`, `space-between`, `space-around`, `space-evenly` |
| `place-items` / `place-content` / `place-self` | [place-items](https://developer.mozilla.org/en-US/docs/Web/CSS/place-items) | Shorthands for the align/justify pair (one value applies to both axes) |
| `gap` / `row-gap` / `column-gap` | [gap](https://developer.mozilla.org/en-US/docs/Web/CSS/gap) | Full support on grid containers (the same property family as flexbox's `gap`) |

The grid engine is fixed to a left-to-right, top-to-bottom writing mode — `direction: rtl` and vertical writing modes are not honored for track order or logical placement. The following are also **not** supported: named lines *inside* `repeat()` and `span <name>` (and a named line declared *after* an `auto-fill`/`auto-fit` repeat is positioned as if the repeat expanded to a single track); masonry; inline-axis (`justify-*`) baseline alignment; and content-distribution `baseline` (`align-content`/`justify-content` do not accept baseline values). Block-axis `baseline` item alignment shifts items to a shared baseline but does not grow the row to the baseline group's extent, so an item with an unusually large descent below its baseline could overflow the row (standard text with a normal line-height always fits). `auto`/`fr` row tracks do not stretch to fill a definite container height, and a `%`-bearing `calc()` track resolves its percentage against 0 in a row or `auto-fill`-count context (as a plain percentage does there). `subgrid` *is* supported (see the row above) with the noted limitations.

### Multi-column Layout

CSS Multi-column Layout (`column-count`/`column-width`/`columns`) is supported with one deliberate simplification: children are fragmented at whole-child granularity, never split partway through — a single paragraph always moves to the next column/page as one atomic unit rather than having some of its lines flow into one column and the rest into the next. For content made of many short block-level children (dictionary entries, list items, cards — the common real-world shape), this produces correct-looking column geometry. True inline-level fragmentation (splitting one element's own line boxes across a column boundary) is not implemented.

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `column-count` | [column-count](https://developer.mozilla.org/en-US/docs/Web/CSS/column-count) | Full support |
| `column-width` | [column-width](https://developer.mozilla.org/en-US/docs/Web/CSS/column-width) | Full support; resolves to as many columns as fit at at-least this width |
| `columns` | [columns](https://developer.mozilla.org/en-US/docs/Web/CSS/columns) | Shorthand for `column-width` and `column-count`, in either order |
| `column-gap` | [column-gap](https://developer.mozilla.org/en-US/docs/Web/CSS/column-gap) | Same underlying property as flexbox/grid's `gap`. When left unset, resolves to `1em` (matching real-world browser behavior for multicol, historically distinct from flex/grid's `0` default) rather than the shared field's own default |
| `column-rule` | [column-rule](https://developer.mozilla.org/en-US/docs/Web/CSS/column-rule) | Shorthand for `column-rule-width`/`column-rule-style`/`column-rule-color`; renders as a real vertical line between columns, one segment per page-row the container spans |
| `column-rule-width` | [column-rule-width](https://developer.mozilla.org/en-US/docs/Web/CSS/column-rule-width) | Full support, including `thin`/`medium`/`thick` |
| `column-rule-style` | [column-rule-style](https://developer.mozilla.org/en-US/docs/Web/CSS/column-rule-style) | `solid`, `dashed`, `dotted`; `double`/`groove`/`ridge`/`inset`/`outset` render as `solid` |
| `column-rule-color` | [column-rule-color](https://developer.mozilla.org/en-US/docs/Web/CSS/column-rule-color) | Full support, including `currentcolor` |
| `column-fill` | [column-fill](https://developer.mozilla.org/en-US/docs/Web/CSS/column-fill) | `balance` (the default) is solved per row via a binary search for the minimum column height that still packs as many children into the row as the full page budget would — tighter than a single closed-form estimate, especially with unevenly-sized children, though still constrained to whole-child (never split) placement. `auto` fills each column to capacity before starting the next |
| `column-span` | [column-span](https://developer.mozilla.org/en-US/docs/Web/CSS/column-span) | Parsed and accepted but has no effect — a `column-span: all` element does not yet break out of the column flow |

### Positioning

Used with `position: relative`, `absolute`, or `fixed`.

An absolutely (or `fixed`) positioned box is **blockified** ([CSS Display 3 §2.7](https://developer.mozilla.org/en-US/docs/Web/CSS/display) / CSS 2.1 §9.7): an inline-level computed display (`inline`, `inline-block`, `inline-flex`, `inline-table`) is coerced to its block-level equivalent, so e.g. a `::before` with no explicit `display` still becomes a real out-of-flow block. Its **containing block** is the padding box of the nearest positioned ancestor ([CSS 2.1 §10.1](https://www.w3.org/TR/CSS21/visudet.html#containing-block-details)), which is what percentage `width`/`height`/`top`/`left` resolve against (not the nearest in-flow block container, when those differ). With `width: auto` and both `left` and `right` set, the box fills the space between them ([§10.3.7](https://www.w3.org/TR/CSS21/visudet.html#abs-non-replaced-width)); likewise `height: auto` with both `top` and `bottom` set ([§10.6.4](https://www.w3.org/TR/CSS21/visudet.html#abs-non-replaced-height)). Out-of-flow children of a **flex or table** container are laid out too (they don't participate in flex/table layout but still get positioned and sized).

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `top` | [top](https://developer.mozilla.org/en-US/docs/Web/CSS/top) | Full support |
| `right` | [right](https://developer.mozilla.org/en-US/docs/Web/CSS/right) | Full support |
| `bottom` | [bottom](https://developer.mozilla.org/en-US/docs/Web/CSS/bottom) | Full support |
| `left` | [left](https://developer.mozilla.org/en-US/docs/Web/CSS/left) | Full support |

### Lists

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `list-style` | [list-style](https://developer.mozilla.org/en-US/docs/Web/CSS/list-style) | Shorthand supported |
| `list-style-type` | [list-style-type](https://developer.mozilla.org/en-US/docs/Web/CSS/list-style-type) | `disc`, `circle`, `square`, `none`; numeric/alphabetic styles `decimal`, `decimal-leading-zero`, `lower-alpha`/`lower-latin`, `upper-alpha`/`upper-latin`, `lower-roman`, `upper-roman`, `lower-greek`, `armenian`, `georgian`, `hebrew`, `hiragana`/`hiragana-iroha`, `katakana`/`katakana-iroha`. An unknown/unsupported style falls back to `decimal` per [CSS Counter Styles Level 3 §2](https://www.w3.org/TR/css-counter-styles-3/) |
| `list-style-position` | [list-style-position](https://developer.mozilla.org/en-US/docs/Web/CSS/list-style-position) | `inside`, `outside` |
| `list-style-image` | [list-style-image](https://developer.mozilla.org/en-US/docs/Web/CSS/list-style-image) | URL, data URI, and all CSS gradient functions supported; same image types as `background-image`, including SVG url() sources rendering as real vector content |

### Page Breaks

These properties control how content breaks across PDF pages. Both the legacy `page-break-*` names and the modern `break-*` names are recognized.

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `break-before` / `page-break-before` | [break-before](https://developer.mozilla.org/en-US/docs/Web/CSS/break-before) | `auto`, `always`, `page`, `avoid` — see the keep-with-next note below the table for how `avoid` behaves |
| `break-after` / `page-break-after` | [break-after](https://developer.mozilla.org/en-US/docs/Web/CSS/break-after) | `auto`, `always`, `page`, `avoid` — see the keep-with-next note below the table for how `avoid` behaves |
| `break-inside` / `page-break-inside` | [break-inside](https://developer.mozilla.org/en-US/docs/Web/CSS/break-inside) | `auto`, `avoid` |
| `orphans` | [orphans](https://developer.mozilla.org/en-US/docs/Web/CSS/orphans) | Enforced in plain (non-multi-column) block flow: if fewer than `orphans` lines of a paragraph-like box would precede a page boundary, the whole box is pushed to the next page — a coarser-than-spec approximation (a spec-conformant UA pulls only the minimum lines needed across the break; PeachPDF moves the entire box, since it has no per-line fragmentation) that's skipped when the box itself is taller than one page (pushing it whole can't help there). Has no effect inside [Multi-column Layout](#multi-column-layout) — its atomic whole-child-only fragmentation already structurally prevents an orphaned/widowed line, since a child is never split across a column in the first place |
| `widows` | [widows](https://developer.mozilla.org/en-US/docs/Web/CSS/widows) | Same mechanism and caveats as `orphans`, for the trailing side of a page break |

**Keep-with-next (`break-after: avoid` / `break-before: avoid`).** Per CSS Fragmentation §3.1, an `avoid` on either side of a sibling break point forbids an unforced break between the two boxes. PeachPDF honors this wherever it relocates content to the next page: when a box is moved wholesale (a table whose body would cross a page boundary, a `break-inside: avoid` box, an `orphans`/`widows` push) or when ordinary word flow pushes a block's *first line* to the next page, the maximal run of preceding siblings chained to it by `avoid` values moves along with it, so a heading is never stranded at the bottom of the page its content just left. The UA default stylesheet applies `h1-h6 { page-break-after: avoid }` (under `@media print`, which PeachPDF always uses), so headings get this behavior out of the box. Chains are transitive (e.g. `h2` + `h3` + paragraph move as a group), a forced break value on either side of a pair takes precedence over `avoid` per §5.2, and an unsatisfiable `avoid` (the run plus its content can't fit on one page) is relaxed per §5.3 — the content moves alone, exactly as it would without the `avoid`.

**Margin truncation at page breaks:** per [CSS Fragmentation Level 3 §5.2](https://www.w3.org/TR/css-break-3/#break-margins), when a vertical margin between two elements is large enough that it alone would push the following element onto a later page (an *unforced* break — no explicit `break-before`/`break-after`/`page` involved), that margin is discarded entirely and the following element starts flush at the top of the very next page, rather than the margin paginating through as literal blank pages. A margin-top large enough to visually separate content across a deliberate page boundary should use an explicit `break-before: page` (or `page-break-before: always`) instead — margins after a *forced* break are preserved, not truncated, per the same spec section. This currently applies to normal block flow only (not flex/table/multi-column item positioning), and only when the affected element has a preceding sibling in the same containing block.

### Tables

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `empty-cells` | [empty-cells](https://developer.mozilla.org/en-US/docs/Web/CSS/empty-cells) | `show`, `hide` |

### Generated Content

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `content` | [content](https://developer.mozilla.org/en-US/docs/Web/CSS/content) | Used with `::before` / `::after` pseudo-elements; string, counter, `attr()`, and `none` values supported; `url()` (including an SVG source, rendered as real vector content) and all CSS gradient functions (`linear-gradient`, `radial-gradient`, `conic-gradient`, and repeating variants) are supported — image values require `display: inline-block` with explicit `width`/`height` on the pseudo-element. `counter()` accepts an optional counter-style argument (`counter(name, <style>)`, e.g. `counter(line, decimal-leading-zero)`) using the same styles as [`list-style-type`](#lists); the style defaults to `decimal`, and an unknown/unsupported style falls back to `decimal` per [CSS Counter Styles Level 3 §2](https://www.w3.org/TR/css-counter-styles-3/) |
| `counter-reset` | [counter-reset](https://developer.mozilla.org/en-US/docs/Web/CSS/counter-reset) | Full support, including the `reversed(<counter-name>)` functional notation — a bare `reversed(name)` with no explicit value starts at the number of elements in scope that will increment it, counting down so the last one lands on 1 |
| `counter-increment` | [counter-increment](https://developer.mozilla.org/en-US/docs/Web/CSS/counter-increment) | Full support. Every element whose *computed* `display` is `list-item` (not just `<li>`) automatically increments the implicit `list-item` counter, per CSS2.1 12.5.1 / CSS Lists Level 3 — the same counter `content: counter(list-item)` and `::marker`'s default numbering both read, so they always agree. `<ol start>`/`<ol reversed>` and `<li value>` are honored as presentational hints (lowest precedence — literal author `counter-reset`/`counter-set` targeting `list-item` still wins) |
| `counter-set` | [counter-set](https://developer.mozilla.org/en-US/docs/Web/CSS/counter-set) | Full support |
| `string-set` | [string-set](https://developer.mozilla.org/en-US/docs/Web/CSS/string-set) | CSS Paged Media property for running headers/footers |
| `page` | [page](https://developer.mozilla.org/en-US/docs/Web/CSS/page) | Activates a named `@page` rule for pages containing the element |

---

## CSS At-Rules

| At-rule | Notes |
|---------|-------|
| `@font-face` | `src` supports `url()` (remote/data-URI, with a comma-separated fallback list — each candidate is tried in order until one loads) and `local()`. The rule's own `font-weight`/`font-style`/`font-stretch` descriptors are authoritative for how that specific resource participates in matching, independent of what the font file's own internal tables say — this is what makes real multi-variant families (e.g. separate rules for weight 400 and 700) work reliably. `unicode-range` is honored: multiple rules sharing a family but declaring different codepoint subsets each supply their own characters, and characters outside every declared subset fall back to the next family in the stack (see [Per-character font matching](#per-character-font-matching-and-coverage-fallback)). When two subset files share one internal font name (a common webfont pattern), each is still treated as its own resource. See [Fonts](usage-examples.md#fonts) |
| `@page` | Full support; see [CSS Paged Media](#css-paged-media) below |
| `@property` | Supported ([CSS Properties & Values API Level 1](https://developer.mozilla.org/en-US/docs/Web/CSS/@property)). Registers a typed custom property with its three descriptors. **`initial-value`** supplies the value a `var()` reference resolves to when the property is otherwise unset (so a palette or default defined only via `@property` works with no author declaration). **`inherits`** governs propagation: `inherits: false` means a descendant that doesn't set the property resolves it to the `initial-value` rather than the parent's value. **`syntax`** is validated at computed-value time — a set value that doesn't match the declared syntax falls back to the `initial-value` — for the single-type grammars `<length>`, `<number>`, `<integer>`, `<percentage>`, `<length-percentage>`, `<color>`, `<angle>`, `<ratio>`, `<image>`, `<url>`, `<time>`, `<resolution>`, `<transform-function>`, `<transform-list>`, `<custom-ident>`, `<string>`, ident literals, the universal `*`, `\|` alternation, and the `+`/`#` list multipliers. A `syntax` naming an unsupported data type is invalid and the whole rule is ignored. `calc()` is accepted in a numeric slot when its resolved type matches (e.g. `calc(50%)` for `<length-percentage>` but not `<length>`); an `initial-value` `calc()` must additionally be [computationally independent](https://developer.mozilla.org/en-US/docs/Web/CSS/@property/initial-value) — it may use absolute lengths, numbers, angles, times, resolutions, and percentages, but not font-relative (`em`/`rem`/`ex`) or viewport lengths. `<image>` accepts `url()`, gradients, and `image-set()`/`cross-fade()`/`element()` (the latter three are validated for registration but not rendered). The registry also applies to SVG styling (see [SVG support](supported-svg-features.md)): an inline `<svg>` uses the host document's registrations, and a standalone SVG uses `@property` rules from its own `<style>` |
| `@media` | Supported; `print` and `all` media types apply, `screen` is ignored, and **feature queries** (`min-width`/`max-width`/range syntax, `orientation`, `resolution`, `prefers-color-scheme`, …) are evaluated against the page box and renderer characteristics; see [CSS Media Queries](#css-media-queries) below |
| `@keyframes` | Not supported |
| `@supports` | **The whole block is ignored** — PeachPDF has no feature-query engine, so it cannot evaluate the `<supports-condition>`, and rather than apply rules guarded by a condition it can't test, it drops them (the inner rules never apply, whether the condition is affirmative or `not (…)`). Put any styles that must always render at the top level, outside `@supports`. (`@media` feature conditions, by contrast, **are** evaluated — see [CSS Media Queries](#css-media-queries).) |
| `@container` | **The whole block is ignored**, for the same reason as `@supports` — the container-size condition cannot be evaluated, so the inner rules never apply. Container-relative units (`cqw`/`cqi`/etc.) are not supported |
| `@layer` | [Cascade layers](https://developer.mozilla.org/en-US/docs/Web/CSS/@layer) are supported. Both the block form (`@layer name { … }`, including anonymous `@layer { … }`) and the statement form (`@layer a, b, c;`, which declares layer order) are parsed and honored. Layer precedence follows CSS Cascade 5: for **normal** declarations an unlayered rule beats any layered rule, and among layers a later-declared layer beats an earlier one — this ordering is applied **ahead of specificity**, so a low-specificity rule in a later layer wins over a high-specificity rule in an earlier layer. Specificity then source order break ties within a single layer. For **`!important`** declarations the layer order **reverses**: an earlier layer beats a later one, and a layered `!important` rule beats an unlayered one. **Nested layers** are ordered as a tree — a parent layer's whole subtree is contiguous and its sub-layers are ordered by first appearance within that parent (so `@layer a.b; @layer c; @layer a.d;` ranks `a.b`, `a.d`, then `c`). An `@font-face`/`@property`/`@page` rule nested inside an `@layer` block (or an `@media`/`@supports` within one) is collected. Remaining simplification: when a single layer mixes direct rules with its own nested sub-layers, the parent's direct rules are ranked before those sub-layers (the exact interleaving is not modeled) |
| `@import` | Full support; the imported stylesheet is fetched and its rules merged in place, including transitively nested `@import`s (with circular-import protection). Relative `url()` references inside an imported stylesheet — including `@font-face src` — resolve against that stylesheet's own location, not the document's |

---

## CSS Selectors

PeachPDF evaluates a subset of CSS selectors. Selectors that are parsed but not implemented are silently ignored — rules using them will not apply.

CSS comments (`/* ... */`) are supported anywhere in a stylesheet, including between selectors and declarations, and are stripped before parsing.

### Basic Selectors

| Selector | Syntax | Notes |
|----------|--------|-------|
| Universal | `*` | Matches all elements |
| Type | `div` | Matches by element name |
| Class | `.foo` | Matches by `class` attribute |
| ID | `#foo` | Matches by `id` attribute |
| Compound | `div.foo`, `.foo#bar` | Multiple simple selectors on the same element; all parts must match |
| Selector list | `div, p` | Comma-separated; applies the rule to all matching elements |

### Attribute Selectors

| Selector | Syntax | Notes |
|----------|--------|-------|
| Presence | `[attr]` | Element has the named attribute |
| Exact match | `[attr=value]` | Attribute value exactly equals `value` |
| Whitespace list | `[attr~=value]` | Attribute is a whitespace-separated list containing `value` |
| Contains | `[attr*=value]` | Attribute value contains `value` as a substring |
| Starts with | `[attr^=value]` | Attribute value starts with `value` |
| Ends with | `[attr$=value]` | Attribute value ends with `value` |
| Hyphen prefix | `[attr\|=value]` | Attribute value equals `value` or starts with `value-` |

### Combinators

| Combinator | Syntax | Notes |
|------------|--------|-------|
| Descendant | `div p` | Matches `p` anywhere inside `div` |
| Child | `div > p` | Matches `p` that is a direct child of `div` |
| Adjacent sibling | `div + p` | Matches `p` immediately preceded by `div` at the same level |
| General sibling | `div ~ p` | Matches `p` preceded by `div` anywhere at the same level |

### Nesting

[CSS Nesting](https://developer.mozilla.org/en-US/docs/Web/CSS/CSS_nesting) is supported: a style rule may contain nested style rules, and the nesting selector `&` refers to the parent rule.

```css
.card {
  color: #333;
  & .title { font-weight: 700; }   /* == .card .title (& is explicit) */
  .body { color: #555; }           /* == .card .body (implicit descendant) */
  &.featured { border: 2px solid gold; }  /* == .card.featured */
  > img { border-radius: 8px; }    /* == .card > img */
}
```

A nested selector is resolved against its parent (`&` takes the parent's specificity, exactly like `:is(<parent>)`); a nested selector with no `&` is made a descendant of the parent. Nesting works to any depth, under a parent selector list (`.a, .b { & .c { } }` matches under either), with a bare type selector (`.card { img { } }`), and inside [`@media`](#css-at-rules)/[`@layer`](#css-at-rules) (the nested rule inherits that context). **Not supported:** an at-rule (`@media`/`@layer`/`@supports`) nested *inside* a style rule — place those at the top level.

### Pseudo-elements

`::before`, `::after`, `::marker`, `::first-letter`, and `::first-line` are supported. All other pseudo-elements are parsed but have no effect.

| Pseudo-element | Notes |
|----------------|-------|
| `::before` | Full support; use with the `content` property |
| `::after` | Full support; use with the `content` property |
| `::marker` | Full support for every property the spec allows on markers — see below |
| `::first-letter` | Full support — see below |
| `::first-line` | Full support for every property CSS2.1 allows — see below |
| All others | Parsed but ignored |

Both the single-colon legacy syntax (`:before`, `:after`) and the modern double-colon syntax (`::before`, `::after`) are accepted. `::marker` has no legacy single-colon form, matching the spec.

**`::marker`** is a real, independently laid-out and painted box (the same as `::before`/`::after`), matching CSS2.1 §12.5.1 / CSS Lists Level 3. It's generated for any element whose *computed* `display` is `list-item` — not just `<li>` — so `<div style="display: list-item">` gets a real marker and numbering too, exactly like a `<li>` would.

- **`content`** — `normal` (the default; the marker shows the automatic bullet/number/image driven by `list-style-type`/`list-style-image` on the list item) or an explicit override: a string, `counter()`/`counters()`, `attr()`, `url()`/gradient (rendered as a real image, same as `content: url()` on `::before`/`::after`), or `none` (no marker at all). An explicit override fully replaces the automatic bullet/number/shape — unlike the automatic case, no `"."` suffix is added, so include any trailing punctuation/spacing in the string yourself.
- **`color`**, **font properties** (`font`, `font-family`, `font-size`, `font-style`, `font-weight`, `font-variant`, to the extent PeachPDF supports them generally) and **`direction`** all take effect on the marker's own glyph/shape, independent of the list item's own styling.
- List numbering (the default, non-overridden case, and any `content: counter(list-item)` override) is backed by the real CSS `list-item` counter (see [Generated Content](#generated-content) below), so both always agree — including with `<ol start>`/`<ol reversed>`/`<li value>` in play.
- Properties outside this set (e.g. `background`, `border`, `width`) have no defined effect on `::marker` — this isn't a PeachPDF gap: CSS Lists Level 3 §3.1.1 itself declares marker-box width/height/margin/padding/alignment layout "not fully defined," and restricts applicable properties to the set above (plus `white-space`/`text-combine-upright`/`unicode-bidi`, and animation/transition properties — none of which apply to PeachPDF's static PDF output anyway). No browser implements box-model properties on `::marker` either, for the same reason.

```css
/* Custom bullet + color, independent of the list item's own text color */
li::marker { content: "→ "; color: crimson; }

/* Big, bold chapter numbers */
ol.chapters > li::marker { font-size: 1.5em; font-weight: bold; }
```

**`::first-letter`** splits the first letter of an element's own real text into a separate, independently styled box. Per CSS1 §1.2, any leading punctuation immediately before the first letter (e.g. an opening quote mark) is included as part of the same unit. The target text may be several inline levels deep (e.g. `p::first-letter` on `<p><em>Hello</em> world</p>` styles the "H" inside the `<em>`) — the search stops at (does not cross into) a nested block-level or atomic inline-level descendant (e.g. a nested `<div>` or `inline-block`), which starts its own independent first-letter scope. Targets the element's own real text only, not `::before`-generated content.

```css
/* Classic drop cap */
p.intro::first-letter { font-size: 300%; float: left; color: crimson; }
```

**`::first-line`** styles whichever content ends up on a block's first formatted line — no box is synthesized (unlike `::before`/`::after`/`::first-letter`), since CSS2.1 restricts it to properties with no layout/box-model effect: `color`, `background` (solid `background-color` only — see note below), `text-decoration`, and the font-metric/spacing set `font-*`, `word-spacing`, `letter-spacing`, `vertical-align`, `text-transform`. Any other property set via `::first-line` (e.g. `margin`, `border`) has no effect, per spec.

Width-affecting properties (`font-size`, `word-spacing`, `letter-spacing`) are fully supported even when a single inline element's content straddles the boundary between the first and second line — the words that actually land on line 1 use the first-line styling and the words that overflow to line 2 correctly revert to the element's own normal styling, rather than one or the other leaking across the boundary.

```css
/* Small-caps drop-in lede, common in print typesetting */
p.lede::first-line { font-weight: bold; color: darkslateblue; font-variant: small-caps; }
```

Known narrowing: a `background-image` layer (as opposed to a solid `background-color`) set via `::first-line` is not first-line-aware and paints using the element's own normal background instead.

### Pseudo-classes

Because PeachPDF renders a static PDF with no interactive or dynamic state, state-based pseudo-classes are parsed but not evaluated and will not match any elements. The structural pseudo-classes (which depend only on an element's position in the document, not on interactive state) are fully supported, including the CSS "An+B" formula.

| Pseudo-class | Notes |
|--------------|-------|
| `:link` | Matches `<a>` elements that have an `href` attribute |
| `:root` | Matches the document's root element (the `<html>` element) |
| `:first-child`, `:last-child` | Equivalent to `:nth-child(1)` / `:nth-last-child(1)` |
| `:only-child` | Matches an element with no other element siblings |
| `:first-of-type`, `:last-of-type` | Equivalent to `:nth-of-type(1)` / `:nth-last-of-type(1)` |
| `:only-of-type` | Matches an element with no other same-tag element siblings |
| `:nth-child(an+b)`, `:nth-last-child(an+b)` | Full "An+B" support, including `odd`/`even` keywords, negative steps (e.g. `:nth-child(-n+3)`), and whitespace around the offset's sign (`:nth-child(10n + 1)` as well as `:nth-child(10n+1)`) |
| `:nth-of-type(an+b)`, `:nth-last-of-type(an+b)` | Same as above, counting only same-tag siblings |
| `:nth-column(an+b)`, `:nth-last-column(an+b)` | Matches a table cell against its column position. Only accounts for `colspan` within the same row — a cell's column position does not account for `rowspan` carried over from earlier rows, since that bookkeeping only exists during layout, not at the point selectors are matched |
| `:nth-child(an+b of S)`, `:nth-last-child(an+b of S)` | CSS Selectors Level 4 `of <selector>` extension — the An+B position is computed only among siblings matching `S`; `S` may be a comma-separated selector list |
| `:not(S)` | Matches an element that does not match `S`. Nesting `:not()` inside `:not()` (e.g. `:not(:not(.foo))`) is rejected — the whole enclosing selector is invalid and the rule matches nothing |
| `:is(S)`, `:matches(S)` | Matches an element that matches any selector in the (comma-separated, forgiving) list `S`. `:matches()` is the legacy alias for `:is()` |
| `:where(S)` | Like `:is(S)`, but always contributes **zero specificity** (CSS Selectors 4 §16) — so a rule written with `:where(…)` is overridden by any normal selector, which is how reset/normalize layers stay low-priority |
| `:has(S)` | Matches an element with a descendant matching `S`; `S` may be a comma-separated selector list. Only the default descendant relationship is supported — CSS4 leading-combinator forms (`:has(> S)`, `:has(+ S)`, `:has(~ S)`) are not supported and are silently discarded by the parser |
| All others | Parsed but not matched — rules are silently ignored |

Known gap: `:nth-column()`/`:nth-last-column()`'s same-row-only limitation described above.

State-based pseudo-classes other than `:link` (`:hover`, `:focus`, `:active`, `:visited`, `:checked`, `:disabled`, `:empty`, etc.) are parsed but not applied — PeachPDF renders a static PDF with no browsing history or interaction state, so `:visited`/`:active` never match by design.

### Cascade & Specificity

Rule application respects real CSS specificity, not just source order: for a given element, matching rules are applied in true document order, then stably re-sorted by specificity (inline style > ID count > class/attribute/pseudo-class count > type/pseudo-element count) so a higher-specificity rule always wins over a lower-specificity one regardless of which was declared first. Equal-specificity rules still resolve by source order (last one wins), and `!important` continues to take precedence over normal declarations, applied per-origin (author `!important` beats author normal; user-agent `!important` beats everything).

---

## CSS Media Queries

PeachPDF renders to PDF, so only media queries that target the `print` medium (or the universal `all` medium) are evaluated. Rules inside `@media screen` are ignored entirely, which lets web stylesheets that separate screen and print styles work correctly out of the box.

[Media *feature* queries](https://developer.mozilla.org/en-US/docs/Web/CSS/@media#media_features) are evaluated against the page box and the renderer's characteristics — so responsive breakpoints select by the page width instead of all applying at once. Both the legacy `min-`/`max-` prefixes (`@media (min-width: 768px)`) and the [range syntax](https://developer.mozilla.org/en-US/docs/Web/CSS/@media#syntax_improvements_in_level_4) (`@media (width >= 48rem)`) are supported; inside a media query, `em`/`rem` resolve against the initial `16px` font size (not the document root's), per [Media Queries 4](https://developer.mozilla.org/en-US/docs/Web/CSS/CSS_media_queries/Using_media_queries).

```css
/* applied — print medium matches */
@media print {
  body { font-size: 12pt; }
}

/* applied — "all" matches every medium */
@media all {
  p { line-height: 1.5; }
}

/* ignored — screen-only rules are skipped */
@media screen {
  body { font-size: 16px; }
}
```

| Media query | Applied in PDF? | Notes |
|-------------|-----------------|-------|
| `@media print { }` | Yes | Directly targets the print medium |
| `@media all { }` | Yes | Applies to every medium, including print |
| `@media only print { }` | Yes | Equivalent to `@media print` |
| `@media screen { }` | No | Screen-only rules are skipped |
| `@media not print { }` | No | Explicitly excluded from the print medium |
| `@media not screen { }` | Yes | Applies to any non-screen medium |
| Comma-separated list | Partial | Applied if **any** entry in the list matches print (e.g. `@media print, screen` applies) |
| `min-width`/`max-width`/`width`, and `height` | Yes | Evaluated against the **page-box** dimensions. Range syntax (`width >= 48rem`) and `min-`/`max-` prefixes both supported |
| `orientation`, `aspect-ratio` | Yes | Derived from the page-box width and height |
| `resolution`, `device-pixel-ratio` | Yes | Evaluated against the CSS reference density of 96 dpi (1 dppx) |
| `prefers-color-scheme` | Yes | Reports `light` by default; set `PdfGenerateConfig.PreferredColorScheme = PdfColorScheme.Dark` to render dark-mode styles |
| `color`, `color-index`, `monochrome`, `grid` | Yes | Fixed device characteristics: color output (8 bits/channel), not a color-index/monochrome device, not a grid/tty |
| `hover`, `pointer`, `any-hover`, `any-pointer`, `update`, `scripting` | Yes | All report the static-document resting state (`none`) — a PDF cannot be hovered, pointed at, updated, or scripted |
| `prefers-reduced-motion` | Yes | Reports `reduce` (a static PDF has no motion) |
| `prefers-contrast`, `prefers-reduced-transparency` | Yes | Report `no-preference` |
| A feature name PeachPDF doesn't recognize | No | The whole media query is invalid and its block does not apply (per Media Queries 4, an unknown feature is false) |

---

## CSS Paged Media

PeachPDF supports the [CSS Paged Media](https://developer.mozilla.org/en-US/docs/Web/CSS/Guides/Paged_media) specification, which controls page dimensions, margins, and running headers/footers.

### `@page` rule

The `@page` at-rule targets PDF pages. A rule without a selector applies to all pages; pseudo-selector rules override it for specific pages.

```css
@page {
  size: A4 portrait;
  margin: 25mm 20mm;
}

@page :first {
  margin-top: 40mm;   /* extra space on the cover page */
}
```

| Feature | Support | Notes |
|---------|---------|-------|
| `@page { }` — base rule | Full | Applies to all pages |
| `@page :first { }` | Full | Applies only to page 1 |
| `@page :left { }` | Full | Applies to even-numbered pages |
| `@page :right { }` | Full | Applies to odd-numbered pages |
| `@page name { }` — named page | Full | Activated by `page: name` on elements; see [Named pages](#named-pages) |

**Cascade order:** the base rule is the fallback; a matching named-page rule overrides it; pseudo-selector rules override named-page rules. When both `:first` and `:right` match page 1, the last matching rule in the stylesheet wins.

**Per-page margin variation:** when a pseudo-selector or named-page rule sets `margin-top`, `margin-left`, etc., those values override the base margins for that page. **All four margins are layout-affecting**, per the CSS Paged Media page-box model. Top and bottom overrides give each page its own content-band *height*, so content paginates against those variable bands. Left and right overrides give each page its own content-box *width*: top-level (main-column) block content re-wraps to that page's own measure, because [the edges of the page area act as a containing block for the layout that occurs between page breaks](https://www.w3.org/TR/css-page-3/#page-model). So `@page :first { margin-left: 0 }` gives a first page whose text genuinely flows into the wider measure, and mirrored `@page :left` / `@page :right` margins (for binding gutters) re-wrap each page's text to its own width. Zero is a valid override — `@page :first { margin: 0 }` gives a first page whose content band is the entire physical sheet, enabling a true four-edge full-bleed cover (size the cover element to the full sheet, e.g. `width: 8.5in; height: 11in`). An element ending exactly on a page boundary with a forced break after it continues on the very next page — no blank page is manufactured for an exact-fit cover (css-break-3's forced-break-at-boundary rule).

Known boundaries of per-page margins:

- **A block spanning a page boundary keeps its start-page measure.** A single paragraph (or other box) that begins on one page and continues onto a differently-margined page is *not* re-wrapped mid-way: it keeps one measure — the page it starts on — across its fragments. This follows the [CSS fragmentation model](https://developer.mozilla.org/en-US/docs/Web/CSS/CSS_fragmentation), where a box's inline size is shared by all of its fragments; only a *new* block starting on the next page picks up that page's measure.
- **Only main-column blocks reflow.** Per-page width applies to block content whose containing block is the page area (the document's main column — direct block children of `<html>`/`<body>`). Content nested inside another block adopts that ancestor's measure rather than its own page's, and flex items, table cells, multi-column children, and blocks with an explicit or percentage `width` keep the base measure. Named-page (`page: <name>`) left/right overrides likewise do not yet re-wrap their content.
- **Percentage heights resolve against the base band.** The initial containing block's height is the base content band, even on a page whose own band is taller — size full-bleed elements with absolute units, not `height: 100%`.
- **The initial containing block uses the base page area.** ICB-relative resolution (the root element's own width, ICB-referencing percentages, and the `position: fixed` containing block) resolves against the configured base page area, not the first page's `:first`-overridden area.
- **Named-page bands begin at the forced break.** An element whose used `page` name differs from the active one always forces a page break, so a named page's own margins take effect exactly from that fresh page onward (through the element's own content and descendants), and revert to the enclosing page's band once content leaves that subtree — see [Named pages](#named-pages).
- `position: fixed` elements and `background-attachment: fixed` layers keep positioning against the base page box on margin-overridden pages (they ride the page's content shift rather than re-resolving against that page's own margins).
- When content-empty pages are skipped (see pagination), `:first`/`:left`/`:right` resolve against the underlying page sequence, not the renumbered output pages.

**Units in `@page` margins:** base and per-page rules resolve margins through the same conversion, so a textually identical margin always produces identical page geometry whether it sits in the base rule or a selector-carrying rule. All absolute units (including spec-correct `px` at 0.75pt — see [Length units](#length-units)), `em`/`ex` (against the `@page` context's own `font-size` when a base or matching `@page` rule sets one, per [css-page-3 §7.1](https://www.w3.org/TR/css-page-3/#page-size-prop), else the root element's font), `rem` (always the root element's font), `%` (against the layout page width, for all four sides, per CSS's margin-percentage rule), and `calc()` expressions over those units are supported in both base and per-page rules. Viewport units (`vw`/`vh`/`vmin`/`vmax`) and `ch` are not supported in `@page` margins: such a declaration is invalid and is dropped ([CSS Syntax error handling](https://www.w3.org/TR/css-syntax-3/#error-handling)), leaving that side at its previously-cascaded value — the base margin for a per-page rule, or the configured (`PdfGenerateConfig`) / UA-default margin for a base rule. Base and per-page rules are fully symmetric here.

### `size` property

```css
@page { size: A4 landscape; }
@page { size: 200mm 150mm; }
```

| Syntax | Example | Notes |
|--------|---------|-------|
| Named keyword | `A4`, `A5`, `A3`, `B4`, `B5`, `letter`, `legal`, `ledger`, `tabloid` | Sets width and height from the standard paper size |
| Orientation only | `portrait`, `landscape` | Rotates the configured page size |
| Keyword + orientation | `A4 landscape` | Named size with explicit orientation |
| Explicit lengths | `210mm 297mm`, `595pt 842pt`, `40em 60em` | Any two CSS `<length>` values ([css-page-3 §7.1](https://www.w3.org/TR/css-page-3/#page-size-prop)): absolute units (`pt`, `px`, `in`, `cm`, `mm`, `pc`) plus the font-relative `em`/`ex` (against the `@page` context's own `font-size` when set, else the root element's font — the same basis `@page` margins use) and `rem` (always the root font). Percentages are not a `<length>` for `size`, and viewport/`ch` units have no page-sheet basis — either leaves the configured page size in place |

When `@page { size: ... }` is present it overrides the `PageSize` or `ManualPageWidth`/`ManualPageHeight` configured via `PdfGenerateConfig`. `size` is honored on the base `@page` rule only — a `size` declared inside a pseudo-selector or named-page rule is ignored (every sheet in one document has the same physical dimensions; only margins vary per page).

### Margin boxes

Margin boxes are sub-rules of `@page` that place text inside the page margins (outside the content area). There are 16 standard boxes arranged around the four margins:

```css
@page {
  margin: 25mm 20mm;

  @top-left    { content: "Company Name"; font-size: 8pt; }
  @top-center  { content: "Document Title"; font-size: 8pt; font-weight: bold; }
  @top-right   { content: "Page " counter(page) " of " counter(pages); font-size: 8pt; }
  @bottom-left { content: "© 2025 Acme Corp"; font-size: 7pt; color: #888; }
}
```

**Available boxes:**

| Row / Column | Left | Center | Right |
|---|---|---|---|
| Top corners | `@top-left-corner` | — | `@top-right-corner` |
| Top margin | `@top-left` | `@top-center` | `@top-right` |
| Bottom margin | `@bottom-left` | `@bottom-center` | `@bottom-right` |
| Bottom corners | `@bottom-left-corner` | — | `@bottom-right-corner` |
| Left margin | `@left-top` | `@left-middle` | `@left-bottom` |
| Right margin | `@right-top` | `@right-middle` | `@right-bottom` |

**Supported `content` values:**

| Value | Example | Notes |
|-------|---------|-------|
| String literal | `content: "Header text"` | Rendered as-is |
| `counter(page)` | `content: counter(page)` | Current 1-based page number |
| `counter(pages)` | `content: counter(pages)` | Total page count |
| `string(name)` | `content: string(chapter)` | Named string captured via `string-set`; see below |
| Mixed | `content: "Page " counter(page) " of " counter(pages)` | Concatenated |
| `url(...)` | `content: url("logo.svg")` | An image (raster or SVG, incl. `data:` URIs) — useful for a logo in a running header. Rendered at natural size, aligned within the box by the box's `text-align`/`vertical-align` (same as text content: the default follows the box's position — e.g. `@top-right` end-aligned, `@top-center` centered — and an explicit `text-align`/`vertical-align` applies), clipped to the box. Not combinable with text/counter/string content in the same declaration |
| Gradient function | `content: linear-gradient(to right, red, blue)` | `linear-gradient()`/`radial-gradient()`/`conic-gradient()` (and their `repeating-` forms), filling the box |
| `none` | `content: none` | Suppresses the box (useful in `:first` to hide the header on the cover page) |

**Supported style properties in margin boxes:**

| Property | Notes |
|----------|-------|
| `color` | Text color; hex and `rgb()` supported |
| `font-family` | Font name; falls back to Arial |
| `font-size` | Any CSS length; e.g. `8pt`, `10px` |
| `font-weight` | `bold` or `normal` |
| `font-style` | `italic` or `normal` |
| `text-align` | `left`, `center`, `right`; default is inferred from box position |
| `vertical-align` | `top`, `middle`, `bottom`; default is `middle` |
| `width` / `min-width` / `max-width` | Controls the width of top/bottom margin boxes; boxes with explicit widths are honoured; remaining space is distributed equally among `auto` boxes. Relative units resolve per [css-page-3 §8](https://www.w3.org/TR/css-page-3/#margin-dimension): `%` against the margin area the box sits in (the content-box width shared by a top/bottom row), `em`/`ex` against the box's own computed font size, `rem` against the root. Viewport units (`vw`/`vh`/`vmin`/`vmax`) and `ch` have no page context and size the box as `auto` |
| `height` / `min-height` / `max-height` | Controls the height of left/right margin boxes; relative units resolve as for `width`, with `%` against the content-box height shared by a left/right column |

### Named pages

The CSS `page` property on an element activates a named `@page` rule starting on the page containing that element. Its used value is **tree-based** ([css-page-3 §3](https://www.w3.org/TR/css-page-3/#using-named-pages)): an element with no `page` of its own (`page: auto`) uses its parent's used value, so the named page carries through the element's own content and descendants — continuing correctly across a chapter's own multi-page span — but content that leaves that subtree (a following sibling, or anything back out at an ancestor's level) **reverts** to the enclosing page (the default page, or an outer named page). A named page therefore does not leak its margins or margin-box overrides onto later, unrelated content. This lets different parts of a document use different page styles (e.g., wider margins for an appendix, or a different running header per chapter). Per css-page-3, an element whose used `page` name differs from the one currently in effect — including a reversion back to the default — **forces a page break** before it, so a named page's styles (including layout-affecting top/bottom margins) always begin on a fresh page, and the following default content resumes on its own fresh page.

```css
@page chapter {
  @top-right { content: "Chapter Section"; font-size: 8pt; }
}

/* Elements with page: chapter activate @page chapter */
div.chapter { page: chapter; }
```

```html
<div class="chapter">
  <h1>Chapter 1</h1>
  <p>Content...</p>
</div>
```

| Value | Behavior |
|-------|---------|
| `page: auto` (default) | Uses the base `@page { }` rule |
| `page: <ident>` | Activates `@page <ident> { }` starting on the page containing this element |

If multiple elements with different `page` values appear on the same page, the last one in document order wins.

An `@page` rule's selector may also list several comma-separated names, sharing one rule across all of them:

```css
@page chapter1, chapter2, chapter3 {
  @top-center { content: "Running Header"; }
}
```

Page names are case-sensitive (`page: Chapter` will not activate `@page chapter { }`).

**Limitation:** named-page activation and reversion are honored for normal block-flow content. A `page` change or reversion among the children of a flex container, table, or multi-column block is not — those layout modes position their own children independently of the page-break machinery, so a named page applied deep inside them may not begin (or revert) on a fresh page. Apply `page` to a block-level element in the normal flow for reliable results.

### Named strings (`string-set` / `string()`)

Named strings capture element content as the document is laid out and replay it in margin boxes. This is the standard mechanism for running headers that show the current chapter or section title.

```css
/* Capture the heading text whenever an h1 or h2 is encountered */
h1 { string-set: chapter content(); }
h2 { string-set: section  content(); }

@page {
  @top-left   { content: string(chapter); font-size: 8pt; font-style: italic; }
  @top-right  { content: string(section);  font-size: 8pt; }
}
```

**`string()` keyword variants:**

| Keyword | Behavior |
|---------|---------|
| `string(name)` / `string(name, first)` | First assignment of `name` that appears on this page; if none, the last assignment from a previous page |
| `string(name, last)` | Last assignment of `name` that appears on this page; if none, the last from a previous page |
| `string(name, start)` | Last assignment of `name` that started before this page (running header — the value in effect at the top of the page) |
| `string(name, first-except)` | Empty on the page where `name` is first assigned; otherwise same as `first` |

### Headers and footers — complete example

```html
<!DOCTYPE html>
<html>
<head>
<style>
@page {
  size: A4 portrait;
  margin: 25mm 20mm 25mm 20mm;

  @top-left   { content: "Acme Corp"; font-size: 8pt; font-family: Arial; color: #555; }
  @top-center { content: string(chapter); font-size: 8pt; font-family: Arial; font-weight: bold; }
  @top-right  { content: "Confidential"; font-size: 8pt; font-family: Arial; color: #c00; }

  @bottom-left   { content: "© 2025 Acme Corp"; font-size: 7pt; font-family: Arial; color: #888; }
  @bottom-center { content: "Page " counter(page) " of " counter(pages); font-size: 8pt; font-family: Arial; }
}

/* Suppress the header on the cover page */
@page :first {
  @top-left   { content: none; }
  @top-center { content: none; }
  @top-right  { content: none; }
}

h1 { string-set: chapter content(); }
</style>
</head>
<body>
  <!-- Cover (page 1 — no header) -->
  <div style="page-break-after: always;">
    <h1>Annual Report 2025</h1>
    <p>Cover page — header is suppressed by @page :first</p>
  </div>

  <!-- Chapter 1 (page 2+) -->
  <h1>Introduction</h1>
  <p>Running header now shows "Introduction" in the top-center margin.</p>
</body>
</html>
```

---

## CSS-wide Keywords

All five CSS-wide keywords are supported on every CSS property. They are resolved during the cascade, before a property value reaches the rendering engine.

| Keyword | Behavior |
|---------|----------|
| `inherit` | Uses the parent element's computed value for the property. On the root element (no parent), falls back to the initial value. |
| `initial` | Resets the property to its CSS specification initial value, ignoring inheritance. |
| `unset` | Acts as `inherit` for inherited properties (e.g. `color`, `font-size`) and as `initial` for non-inherited properties (e.g. `margin`, `padding`). |
| `revert` | Rolls back to the value from the previous cascade origin. In an author stylesheet rule, reverts to the user-agent (UA) stylesheet value. In an inline style, reverts to the author stylesheet value. |
| `revert-layer` | Layer-aware: rolls the property back to the value it would have from the lower-priority [`@layer`](#css-at-rules) cascade layers of the same origin (then, if none, the previous origin) — revealing an earlier layer's value rather than discarding the whole author origin the way `revert` does. Works for both normal and `!important` declarations, and for custom properties. |

All five keywords can be combined with `!important`.

---

## CSS Custom Properties

PeachPDF supports [CSS custom properties](https://developer.mozilla.org/en-US/docs/Web/CSS/--*) (`--foo: value`) and the [`var()`](https://developer.mozilla.org/en-US/docs/Web/CSS/var) function, including inheritance, fallback values, and interaction with the CSS-wide keywords above.

```css
.card {
  --brand-color: #2c3e50;
  background: var(--brand-color);
  border: 1px solid var(--accent-color, #333); /* fallback used since --accent-color is undefined */
}
```

| Feature | Support | Notes |
|---------|---------|-------|
| Declaration (`--name: value`) | Full | Custom property names are case-sensitive (`--Foo` and `--foo` are distinct) and accept almost any token sequence as a value |
| `var(--name)` | Full | Substituted with the custom property's cascaded value before the containing declaration is applied |
| `var(--name, fallback)` | Full | The fallback (which may itself contain `var()`, including further fallbacks) is used when `--name` is undefined |
| Inheritance | Full | Custom properties are always inherited, regardless of whether the property they're used in is normally inherited |
| Shorthand properties | Full | `var()` inside a shorthand (e.g. `margin: var(--a) var(--b)`) is resolved before the shorthand is expanded into its longhands |
| `inherit` / `unset` on a custom property | Full | Pulls the parent element's value, since custom properties are always inherited |
| `initial` on a custom property | Full | Clears the property to the guaranteed-invalid value (absent) |
| `revert` / `revert-layer` on a custom property | Full | `revert` restores the value from the previous cascade origin; `revert-layer` restores the value from lower-priority cascade layers of the same origin (see the [CSS-wide keywords](#cascade--specificity) note), same as for built-in properties |
| Cyclic references | Handled | A custom property that references itself, directly or through a chain (`--a: var(--b); --b: var(--a);`), resolves to the guaranteed-invalid value instead of looping |
| `@property` (typed/registered custom properties) | Supported | A custom property registered via [`@property`](https://developer.mozilla.org/en-US/docs/Web/CSS/@property) gains a typed `syntax`, an `initial-value`, and an `inherits` flag (see the [`@property` at-rule row](#css-at-rules)); this applies to both HTML and SVG styling |

When a `var()` reference can't be resolved and no fallback is given, the containing declaration falls back the same way `unset` does: to the parent's value for an inherited property, or to the property's initial value otherwise.

---

## CSS Math Functions

PeachPDF supports [`calc()`](https://developer.mozilla.org/en-US/docs/Web/CSS/calc), [`min()`](https://developer.mozilla.org/en-US/docs/Web/CSS/min), [`max()`](https://developer.mozilla.org/en-US/docs/Web/CSS/max), and [`clamp()`](https://developer.mozilla.org/en-US/docs/Web/CSS/clamp) anywhere a `<length>`, `<percentage>`, `<angle>`, or plain `<number>` is accepted — width, height, margin, padding, inset (`top`/`left`/`right`/`bottom`), border-width, border-spacing, border-radius, gap, flex-basis, font-size, line-height, text-indent, the length/number arguments of `transform` functions like `translateX()`/`scale()`, the angle argument of `rotate()`/`skewX()`/`skewY()` and gradient direction angles, and the hue component of `hsl()`/`hsla()`.

```css
.card {
  width: calc(100% - 40px);
  padding: clamp(8px, 5%, 24px);
  transform: rotate(calc(45deg + 10deg));
  margin-left: min(5vw, 10px); /* vw isn't resolvable - see Unsupported CSS Features below */
}
```

| Feature | Support | Notes |
|---------|---------|-------|
| `calc()` — `+`, `-`, `*`, `/` | Full | Standard CSS type-checking rules apply: `+`/`-` require matching operand categories (percentages freely combine with lengths), `*`/`/` require one operand to be a plain number |
| `min()` / `max()` | Full | Any number of comma-separated arguments, all of the same category |
| `clamp(min, val, max)` | Full | Exactly 3 arguments; if `min` is greater than `max`, the used value is `max` (per spec). The `none` keyword for an unbounded `min`/`max` is not supported |
| Nesting | Full | `calc()`/`min()`/`max()`/`clamp()` may nest inside each other and inside parentheses, to any depth |
| Combined with `var()` | Full | The custom property is substituted first, then the resulting expression is validated and evaluated the same as a literal one |
| Percentages inside a math function | Full | Resolved against the same base the plain percentage form would use at that property (e.g. containing-block width for `width`/`margin`, parent font-size for `em`-relative `font-size`). Not accepted at all for the length-only properties (`border-width`, `border-spacing`), matching those properties' plain (non-`calc()`) behavior |
| Angle units (`deg`, `grad`, `rad`, `turn`) inside a math function | Full | Mixed angle units fold to a single value at parse time (e.g. `rotate(calc(1turn / 4))` → `rotate(90deg)`), since angle units, unlike lengths/percentages, never need layout context to resolve |
| Divide-by-zero / invalid category mixes (`calc(10px + 5)`, `calc(1px * 1px)`, `calc(10px + 5deg)`) | Rejected | The whole declaration is treated as invalid, the same as any other malformed CSS value |
| Time and resolution units (`s`, `dpi`) inside a math function | Not supported | PeachPDF doesn't support these unit categories at all, with or without a math function |
| Viewport units (`vw`/`vh`/`vmin`/`vmax`) inside a math function | Not supported | PeachPDF has no viewport-unit support anywhere |
| A math function inside CSS Grid track sizing | Full | A `calc()`/`min()`/`max()`/`clamp()` length resolves as a grid track size — bare, and inside `minmax()`/`fit-content()`/`repeat()` arguments (a wrong-category math function, e.g. an angle, drops the whole track list at parse time) |

Note: `background-position` and `background-size` are not listed in the table above because they're not part of the math-function-specific test matrix — both fully support `calc()` in their length/percentage values (e.g. `background-position: calc(50% - 10px) center`), resolved via the same length parser used everywhere else in this table.

---

## Tagged PDF (PDF/UA) Support

PeachPDF can optionally produce a *tagged* PDF — one with a logical structure tree (`/StructTreeRoot`) exposing the document's headings, paragraphs, lists, tables, links, and images to assistive technology (e.g. screen readers), per ISO 32000-1's tagged-PDF conventions and in the direction of PDF/UA conformance.

Tagging is **off by default** and enabled with a single `PdfGenerateConfig` flag — see [Enabling tagged PDF (PDF/UA) output](usage-examples.md#enabling-tagged-pdf-pdfua-output) in Usage Examples for the configuration snippet and everything that turning it on does (automatic `/Lang` from `<html lang>`, `alt`-attribute `/Alt` entries, `/Link` structure elements cross-referenced with their annotations, and `/Lbl`/`/LBody` list-item splitting). When `EnableTaggedPdf` is left at its default (`false`), none of this runs — output is byte-for-byte the same as if tagging didn't exist in the codebase at all.

### `-peachpdf-pdf-tag-type` (tagged PDF structure type)

The HTML-tag → PDF-structure-type mapping is not hardcoded — it's driven entirely by a custom CSS property, `-peachpdf-pdf-tag-type`, applied via ordinary CSS rules (PeachPDF's own default stylesheet sets it for standard HTML elements; author stylesheets can override it like any other property).

| | |
|---|---|
| Initial value | `auto` |
| Applies to | All elements, and the `::marker` pseudo-element (see [Pseudo-elements](#pseudo-elements) above) |
| Inherited | No |
| Percentages | N/A |

```css
/* Promote a styled <div> to a real BlockQuote in the structure tree */
div.pull-quote { -peachpdf-pdf-tag-type: BlockQuote; }

/* Make a purely decorative wrapper invisible to the structure tree - its children attach
   directly to the nearest tagged ancestor instead */
span.decorative-wrapper { -peachpdf-pdf-tag-type: none; }

/* Suppress marker tagging for a purely decorative list */
ul.decorative li::marker { -peachpdf-pdf-tag-type: none; }
```

Accepted values (case-insensitive): `auto`, `none`, `Part`, `Art`, `Sect`, `Div`, `Index`, `BlockQuote`, `Caption`, `TOC`, `TOCI`, `P`, `H1`–`H6`, `L`, `LI`, `Lbl`, `LBody`, `DL`, `DL-Div`, `DT`, `DD`, `Span`, `Quote`, `Table`, `TR`, `TH`, `TD`, `THead`, `TBody`, `TFoot`, `BibEntry`, `Code`, `Figure`, `Formula`, `Artifact`, `Note`, `Reference`, `Link`.

- **`auto`** (the initial value) — resolved from the element's own HTML tag via the default mapping table below. An element with no default mapping and no author override resolves to `Div` (block-level) or `Span` (inline-level).
- **`none`** — the element is fully transparent in the structure tree: no structure element is created for it, and its children attach directly to the nearest tagged ancestor. This is the escape hatch for purely presentational wrapper elements.
- Any other value is used directly as the element's PDF standard structure type, author-overridable on any element regardless of what (if anything) the default stylesheet set.

This property only has an effect when `EnableTaggedPdf` is `true` — with tagging disabled, it's parsed and cascades normally (so it doesn't break unrelated selector matching) but is never read by anything.

#### Default tag-type mapping

| HTML | `-peachpdf-pdf-tag-type` |
|------|---------------------------|
| `h1`–`h6` | `H1`–`H6` |
| `p` | `P` |
| `div`, `header`, `footer`, `main`, `address`, `hgroup`, `fieldset`, `form`, `center`, `dir`, `menu`, `pre` | `Div` |
| `span` | `Span` |
| `ul`, `ol` | `L` |
| `li` | `LI` |
| `li::marker` | `Lbl` |
| `dl` | `DL` |
| `dt` | `DT` |
| `dd` | `DD` |
| `table` | `Table` |
| `tr` | `TR` |
| `th` | `TH` |
| `td` | `TD` |
| `thead` | `THead` |
| `tbody` | `TBody` |
| `tfoot` | `TFoot` |
| `caption`, `figcaption` | `Caption` |
| `img`, `svg`, `figure` | `Figure` |
| `blockquote` | `BlockQuote` |
| `q` | `Quote` |
| `article` | `Art` |
| `section`, `nav`, `aside` | `Sect` |
| `hr` | `Artifact` |
| `code`, `kbd`, `samp`, `var` | `Code` |
| `a[href]` | `Link` (a bare `<a>` with no `href` is not a hyperlink and does not default to `Link`) |
| `html`, `body` | `none` (transparent — children attach to the synthetic document root) |

Any tag not listed here (e.g. `<cite>`, `<mark>`, `<time>`) falls through to the `auto` fallback: block-level elements resolve to `Div`, inline-level elements to `Span`.

#### Known limitation — anonymous (CSS-generated) table structure cannot be tag-overridden

A table assembled purely through CSS (`display: table` / `table-row` / `table-cell` on arbitrary elements, rather than real `<table>`/`<tr>`/`<td>` markup) gets its row/cell/group tagging (`TR`/`TH`-or-`TD`/`THead`/`TBody`/`TFoot`) from a hardcoded fallback based on the computed `display` value, **not** from `-peachpdf-pdf-tag-type` — the synthesized anonymous boxes PeachPDF creates to complete the table model have no source HTML element for any selector, author or default stylesheet, to match against. Authors who need override control over table structure tagging (e.g. distinguishing header cells from data cells, which the anonymous fallback cannot do — it always tags anonymous cells `TD`) must use real `<table>`/`<tr>`/`<th>`/`<td>`/etc. markup rather than relying on CSS's table display model to synthesize the structure implicitly.

---

## Unsupported CSS Features

The following CSS features are not supported:

- **Grid: masonry** — the core grid formatting model (tracks, placement, named lines, `grid-template-areas`, auto-flow, alignment, `subgrid`, and the `grid`/`grid-template` mega-shorthands) is supported; see [Grid](#grid). Masonry, named lines inside `repeat()`, and `span <name>` are not
- **3D perspective** — the `perspective()` transform function, and the `perspective`/`perspective-origin`/`transform-style`/`backface-visibility` properties
- **Transitions and animations** — `transition`, `animation`, `@keyframes`
- **Filters and effects** — `filter`, `backdrop-filter`, `mix-blend-mode` (not parsed at all)
- **`text-shadow`**
- **`word-wrap` / `overflow-wrap`**
- **`outline`** and `outline-*` properties
- **CSS selectors** — see the [CSS Selectors](#css-selectors) section above for what is and is not supported
- **Viewport units** (`vw`, `vh`, `vmin`, `vmax`) and container-relative units (`cqw`, `cqi`, etc.) — note responsive `@media` feature queries (`min-width`, `prefers-color-scheme`, …) **are** supported (see [CSS Media Queries](#css-media-queries)); it is the viewport/container length *units* that are not
