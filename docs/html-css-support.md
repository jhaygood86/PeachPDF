# HTML & CSS Support

PeachPDF renders a subset of the HTML and CSS specifications. This page documents exactly what is and is not supported. Where a feature is only partially supported, the specific gaps are noted.

## Forward compatibility

**Only standards-compliant documents are guaranteed to keep rendering the same way across PeachPDF versions.** As support grows, PeachPDF moves toward what the specifications say — so a document written against the standard keeps working, and gets better as gaps close.

A document that relies on anything *outside* the standard carries no such guarantee. That includes a value or property PeachPDF happens to accept today but the relevant specification does not define; behavior that falls out of a documented gap (for example, laying content out as though an unimplemented property were absent); and quirks of how PeachPDF currently approximates a feature. Any of these may change in a release that brings the affected area closer to spec, and such a change is not treated as a regression.

Where a correction like that has a visible effect, it is called out in the release notes for the version it shipped in.

## Length units

All CSS length units resolve through one shared conversion, at their spec-defined physical ratios ([CSS Values & Units §6.2](https://www.w3.org/TR/css-values-3/#absolute-lengths)) — everywhere: body layout, fonts, borders, backgrounds, images, `@page` geometry, and SVG.

| Unit | Physical size | In PDF points |
|------|---------------|---------------|
| `px` | 1/96 in | 0.75pt |
| `pt` | 1/72 in | 1pt |
| `in` | 1 in | 72pt |
| `cm` / `mm` | metric | 28.35pt / 2.835pt |
| `pc` | 1/6 in | 12pt |

In particular, `px` is spec-correct CSS pixels: a `96px`-wide element is exactly one inch wide in the output PDF, and a 96×96-pixel image renders at its true CSS size of one square inch — the same as every browser's print output and other spec-conformant paged renderers. Relative units (`em`/`rem`/`%`) resolve against their usual per-property bases; the font-measured units (`ex`/`ch`/`cap`/`ic`/`lh` and their root-element `rex`/`rch`/`rcap`/`ric`/`rlh` forms) are read from the used font — see [Font-relative units](#font-relative-units); viewport units (`vw`/`vh`/`vmin`/`vmax`, and their logical/small/large/dynamic variants) are supported against the PDF page box — see [CSS Viewport Units](#css-viewport-units).

### Font-relative units

The units below are defined by the font, per [CSS Values and Units 4 §6.1](https://developer.mozilla.org/en-US/docs/Web/CSS/length#font-relative_lengths). Each is **measured from the font the element actually uses** — the first family in its `font-family` list that resolves — rather than being a fixed fraction of `font-size`, so `10ch` in a monospace font and `10ch` in a proportional font are different widths.

| Unit | Measures | When the font can't say |
|------|----------|-------------------------|
| `em` | the element's own `font-size` | — |
| `ex` | the font's x-height (OS/2 `sxHeight`) | `0.5em` |
| `ch` | the advance of the "0" (U+0030) glyph | `0.5em` |
| `cap` | the font's cap height (OS/2 `sCapHeight`) | the font's ascent |
| `ic` | the advance of the "水" (U+6C34) ideograph, in whichever family in the stack supplies it | `1em` |
| `lh` | the element's used [`line-height`](https://developer.mozilla.org/en-US/docs/Web/CSS/line-height) | — |
| `rem`, `rex`, `rch`, `rcap`, `ric`, `rlh` | the same measurements, taken from the **root element's** font (and, for `rem`/`rlh`, its `font-size`/`line-height`) instead of the element's own | as above |

The measured units are accepted everywhere a `<length>` is, including inside [`calc()`](https://developer.mozilla.org/en-US/docs/Web/CSS/calc), gradient stop positions and radii, and [SVG](supported-svg-features.md#coordinate-systems--units) and [MathML](supported-mathml-features.md) lengths. In `font-size` itself, and in `line-height` for `lh`, they refer to the **parent's** font and line-height (the element's own is what is being computed), as the specification requires.

A context with no font of its own — a [media query](#css-media-queries) or an `@page` margin, size or margin-box length — has nothing to measure, so there each unit takes the fallback in the last column (and `cap` and `lh` use `0.7em` and `1.2em`).

---

## HTML Elements

### Global attributes

| Attribute | MDN Reference | Notes |
|-----------|--------------|-------|
| `dir` | [dir](https://developer.mozilla.org/en-US/docs/Web/HTML/Global_attributes/dir) | `ltr`, `rtl`, and `auto` are all supported on any element. `auto` (and `<bdi>`'s implicit default when it carries no `dir` of its own) resolves the element's base direction from the first strong-directional character in its own text content, per the HTML Standard's directionality algorithm — skipping into a descendant only if that descendant has no `dir` of its own, and never descending into a nested element that sets its own direction |

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
| `hr` | [hr](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/hr) | The rule *is* the box's border, painted by the same border code as every other block-level box, so every [`border-style`](https://developer.mozilla.org/en-US/docs/Web/CSS/border-style) works on it — `inset`/`outset`/`groove`/`ridge` shade their faces, `dotted`/`dashed` fit their pattern to the edge, `double` draws its two lines, and `border-style: none` paints nothing at all. `noshade` and `color` keep the rule flat rather than engraved, per the HTML Standard's own UA rule for them, and a flat rule — whether flattened by one of those attributes or by an author's own `border-style` — takes its colour from [`color`](https://developer.mozilla.org/en-US/docs/Web/CSS/color): `<hr color="red">` is red, and both `<hr noshade>` and `hr { border-style: dashed }` are the UA sheet's own gray. The default engraved rule derives its two greys from the bevel instead, so neither `color` nor an inherited colour tints it — see [the `currentcolor` note under border styles](#how-each-border-style-is-drawn). It is positioned by the same block-flow code, so it follows [CSS 2.1 §9.4.3](https://www.w3.org/TR/CSS21/visuren.html#relative-positioning) (a relatively-offset preceding sibling does not move it) and [§8.3.1](https://www.w3.org/TR/CSS21/box.html#collapsing-margins) (its margin collapses against the nearest *in-flow* predecessor, never a float). Its default margin is `0.5em` above and below with `auto` on the left and right, as in the HTML Standard, so a rule narrower than its container centres, and an author `margin` overrides all of it. The legacy attributes map as the HTML Standard says: `align` (`left`, `right` or `center`, matched case-insensitively against the exact value) sets the horizontal margins rather than `text-align`; `size` is the rule's *total* height, so a `size` above 1 is a `height` of `size` minus 2 pixels (the two 1px borders count towards it) and a `size` of 1 or less drops the bottom border and leaves a single 1px line; and `color` or `noshade` fills the rule's content box in its own colour, so a thick coloured rule is a solid bar rather than two lines with a gap between them. A rule with no explicit `height` is exactly as tall as its own borders and padding, per [§10.6.3](https://www.w3.org/TR/CSS21/visudet.html#normal-block)'s used content height of 0, so an unpadded rule's two borders meet with nothing between them; a declared `height` adds content between them, and `max-height` clamps it ([§10.7](https://www.w3.org/TR/CSS21/visudet.html#min-max-heights)). Whatever that resolves to is also what the next block is placed against and what a container holding only the rule takes as its own. Its `width` follows the ordinary block rules: a percentage resolves against the content width of its containing block — the content edge of the nearest block container ancestor, per [§10.1](https://www.w3.org/TR/CSS21/visudet.html#containing-block-details) and [§10.2](https://www.w3.org/TR/CSS21/visudet.html#the-width-property) — with the rule's own margins, borders and padding outside that basis, and `auto` fills what is left of that width once they are taken out ([§10.3.3](https://www.w3.org/TR/CSS21/visudet.html#blockwidth)). So an unstyled rule's border box spans its container exactly, and a declared `width` — percentage or length, with or without `box-sizing: border-box` — measures the same on a rule as on an equivalent empty `<div>`. `min-width` and `max-width` clamp it like any block ([§10.4](https://www.w3.org/TR/CSS21/visudet.html#min-max-widths)), and an `auto`-width rule shrinks to fit rather than filling when it is a float, an absolutely positioned box, a flex item or an `inline-block`. A percentage or `calc()` `height` against a containing block with no definite height of its own computes to `auto`, as in a browser, whichever form the percentage is written in. A rule with no border, padding or height is 0 tall, like an empty `<div>`, and paints nothing. |
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
| `bdi` | [bdi](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/bdi) | Full support — isolates its content from the surrounding paragraph's Unicode Bidi Algorithm resolution (`unicode-bidi: isolate`, per the UA stylesheet), and when it has no `dir` attribute of its own, its base direction is auto-detected from its content's first strong character, same as an explicit `dir="auto"` |
| `bdo` | [bdo](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/bdo) | Full support |
| `big` | [big](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/big) | Deprecated element; rendered with a larger font size |
| `br` | [br](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/br) | Full support. A forced break ends the line it falls on, so one with nothing after it in its block leaves no line behind it: `x<br>` is one line tall, and so is `x<br>` followed by a block-level sibling. Only the last break in a run is taken back this way — `x<br><br>` is two lines and `x<br><br>c` is three |
| `cite` | [cite](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/cite) | Rendered as italic |
| `code` | [code](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/code) | Rendered in a monospace font |
| `del` | [del](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/del) | Rendered with strikethrough |
| `em` | [em](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/em) | Rendered as italic |
| `i` | [i](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/i) | Rendered as italic |
| `ins` | [ins](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/ins) | Rendered with underline |
| `kbd` | [kbd](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/kbd) | Rendered in a monospace font |
| `nobr` | [nobr](https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/nobr) | Obsolete, non-standard element; not supported. Use a standard element with `white-space: nowrap` instead. For well-formed legacy input that cannot be changed, an opt-in stylesheet can reproduce its non-wrapping layout, but malformed/nested parser recovery remains unsupported; see [Applying compatibility styles to legacy HTML](usage-examples.md#applying-compatibility-styles-to-legacy-html) |
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
| `img` | [img](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/img) | Full support; images are loaded via the configured network loader. `data:` URIs and local `file:` URIs (including relative paths, which resolve against the document base — see [Usage Examples](usage-examples.md#rendering-a-local-html-file)) are always supported regardless of the configured loader. An SVG source (`.svg` file, `data:image/svg+xml`) renders as real vector PDF content — see [Supported SVG Features](supported-svg-features.md). Raster decode support: JPEG, PNG, BMP, GIF, WebP (VP8/VP8L, including alpha), AVIF (baseline still images, including alpha), and TIFF (uncompressed/LZW/PackBits, 1/2/4/8/16-bit grayscale/RGB/palette/CMYK — decode only, no encode); TGA, PSD, and HDR are not supported. A CMYK or YCCK JPEG is embedded preserving its native CMYK data (with its embedded ICC profile carried through when present) rather than converted to RGB, always at its natural pixel size — see [Color and ICC profiles](usage-examples.md#color-and-icc-profiles). A CMYK TIFF is likewise preserved (decoded natively and embedded as a raw CMYK stream, with its embedded ICC profile carried through when present) rather than converted to RGB. An opaque PNG (no real per-pixel alpha channel, not interlaced) embeds losslessly and byte-for-byte via its own compressed data rather than being re-encoded as a lossy JPEG — including a `tRNS` chroma-key PNG, via a PDF color-key `Mask` array — and an opaque BMP/GIF embeds via a raw lossless stream at its own natural display size — see [Lossless embedding for opaque PNG/BMP/GIF images](usage-examples.md#lossless-embedding-for-opaque-pngbmpgif-images) for the `ImageCompression` option controlling this |
| `object` | [object](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/object) | Implements the HTML "replacement algorithm" for a static renderer: if `data` resolves to a supported image (including through the `type` attribute or a `data:` URI's own MIME header — checked without a network fetch when the declared type is already known not to be an image), the element renders exactly like `img`. Otherwise it falls back to its DOM children, which may themselves be nested `object`/`img` elements and are resolved the same way recursively — matching how browsers fall through a chain of nested `<object>` fallbacks |
| `iframe` | [iframe](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/iframe) | Rendered as a placeholder box with a gray border. For YouTube and Vimeo embed URLs, a video thumbnail image is displayed |
| `video` | [video](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/video) | Video playback isn't possible in a static PDF, so the element renders its `poster` image (the frame browsers show before playback) as a replaced element — exactly like `img`, honoring [`object-fit`/`object-position`](#box-model). With no `poster` (or if it fails to load), the element falls back to being an ordinary container laying out any fallback DOM content |
| `svg` | [svg](https://developer.mozilla.org/en-US/docs/Web/SVG/Element/svg) | Inline SVG renders as real vector PDF content, not a rasterized bitmap — see [Supported SVG Features](supported-svg-features.md) for the full SVG compatibility matrix |
| `math` | [math](https://developer.mozilla.org/en-US/docs/Web/MathML/Reference/Element/math) | Inline MathML renders as real vector PDF content (glyphs, fraction bars, radicals, stretchy operators), not a rasterized bitmap — see [Supported MathML Features](supported-mathml-features.md) for the full MathML compatibility matrix. Tagged as a `Formula` structure element by default when [tagged PDF output](#tagged-pdf-pdfua-support) is enabled, with the original MathML source attached as a PDF 2.0 Associated File — see [MathML Associated Files](#mathml-associated-files) |

### Forms

Form elements are rendered as static boxes by default. There is no interactive behavior unless [interactive PDF forms output](#interactive-pdf-forms-support) is explicitly enabled — inputs cannot be focused or edited, and forms cannot be submitted.

| Element | MDN Reference | Notes |
|---------|--------------|-------|
| `button` | [button](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/button) | Rendered as a static `inline-block` box; never becomes an interactive AcroForm field, even with interactive PDF forms enabled |
| `input` | [input](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/input) | Rendered as a static `inline-block` box with a browser-like default size. With [interactive PDF forms](#interactive-pdf-forms-support) enabled, becomes a real fillable text, checkbox, or radio field depending on its `type` |
| `select` | [select](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/select) | Rendered as a static `inline-block` box. With [interactive PDF forms](#interactive-pdf-forms-support) enabled, becomes a real fillable combo-box field populated from its `option` children |
| `textarea` | [textarea](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/textarea) | Rendered as a static `inline-block` box; never becomes an interactive AcroForm field, even with interactive PDF forms enabled |

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

### Malformed markup

Two of the HTML Standard's [tree-construction](https://html.spec.whatwg.org/multipage/parsing.html#tree-construction)
error-recovery rules are worth stating, because both change the **element tree** a stylesheet then
matches against — most visibly through the sibling combinators `+` and `~`, and through
`:first-child`/`:nth-child()`:

- **An end tag whose element is not open is ignored**, and the insertion point does not move. Content
  after it stays where it was rather than being reparented.
- **`</p>` is the exception**: when no `<p>` is open it generates an *empty* `<p>`, immediately closed.
  This is what the spec requires, and it is observable. An end tag written after a construct that
  already implied it — `<p><table>…</table></p>`, where `<table>` closes the paragraph itself — leaves
  a real, empty paragraph element behind, so `p + table + p` matches *that* element rather than the
  next authored paragraph.

`</br>`, which the spec turns into a `<br>` *start* tag, is not implemented and is ignored like any
other unmatched end tag.

---

## CSS Properties

### Box Model

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `width` | [width](https://developer.mozilla.org/en-US/docs/Web/CSS/width) | Full support |
| `height` | [height](https://developer.mozilla.org/en-US/docs/Web/CSS/height) | Full support. When content is taller than an explicit `height`, the box does not grow to fit it — content overflows past the box's bottom edge (same behavior as `max-height`/`overflow: hidden` elsewhere in this engine). On a `<table>` or `<tr>` this instead follows [CSS 2.1 §17.5.3](https://www.w3.org/TR/CSS21/tables.html#height-layout): the table's height becomes the *maximum* of the specified height and the rows' natural total, never a clip, and any surplus is distributed proportionally across the rows (a `<tr>`'s own `height` is likewise a per-row minimum) |
| `max-width` | [max-width](https://developer.mozilla.org/en-US/docs/Web/CSS/max-width) | Full support, including replaced elements (images), absolutely/fixed positioned boxes, flex items, and table cells (a cell's `max-width` caps its column) |
| `min-width` | [min-width](https://developer.mozilla.org/en-US/docs/Web/CSS/min-width) | Full support, including replaced elements (images), absolutely/fixed positioned boxes, flex items, and table cells (a cell's `min-width` widens its column) |
| `max-height` | [max-height](https://developer.mozilla.org/en-US/docs/Web/CSS/max-height) | Full support, including replaced elements (images), absolutely/fixed positioned boxes, and flex items. When content is taller than `max-height`, the box does not grow to fit it — content overflows past the box's bottom edge (same behavior as `overflow: hidden` elsewhere in this engine) |
| `min-height` | [min-height](https://developer.mozilla.org/en-US/docs/Web/CSS/min-height) | Full support, including replaced elements (images), absolutely/fixed positioned boxes, and flex items. On a `<table>` or `<tr>`, same §17.5.3 minimum-and-redistribute behavior as `height` above |
| `margin` | [margin](https://developer.mozilla.org/en-US/docs/Web/CSS/margin) | Shorthand and all four longhands (`margin-top`, `margin-right`, `margin-bottom`, `margin-left`) are supported. On an in-flow block-level box, `auto` follows [CSS 2.1 §10.3.3](https://www.w3.org/TR/CSS21/visudet.html#blockwidth): both horizontal margins `auto` centre the box in its containing block's *content* width (whichever `box-sizing` that block uses), and exactly one `auto` margin takes all of the remaining space, so `margin-left: auto` pushes a fixed-width block to the end edge. A `<table>` centres with both margins `auto` but does not yet right-align with a single `margin-left: auto`. |
| `padding` | [padding](https://developer.mozilla.org/en-US/docs/Web/CSS/padding) | Shorthand and all four longhands (`padding-top`, `padding-right`, `padding-bottom`, `padding-left`) are supported |
| `box-sizing` | [box-sizing](https://developer.mozilla.org/en-US/docs/Web/CSS/box-sizing) | `content-box` and `border-box` are supported. Not inherited, per spec — a common pattern like `html { box-sizing: border-box }` needs `*, *::before, *::after { box-sizing: inherit }` to propagate it, the same as in browsers. |
| `aspect-ratio` | [aspect-ratio](https://developer.mozilla.org/en-US/docs/Web/CSS/aspect-ratio) | Supported in both directions (`aspect-ratio: <number> [ / <number> ]?`, `auto`, or `auto <ratio>`). A box with a **definite width** and an **auto height** takes its height from the width via the ratio; a **definite height** with an **auto width** derives the width from the height, where the width would otherwise be shrink-to-fit (an absolutely-positioned auto-width box, and replaced elements) — a normal-flow block's auto width stays stretch-fit, which wins over the ratio, matching browsers. The ratio applies to the box-sizing box, and a ratio-derived height is definite, so a percentage-height descendant resolves against it (this is what makes a pure-CSS chart's bars take their height from a ratio-sized container); an **indefinite** percentage height is treated as automatic, so the ratio sizes it too. The ratio-derived dimension is clamped by `min-*`/`max-*`. A definite length in the other axis overrides the ratio. On **replaced elements** (images, `<svg>`, `<object>`, `<video>` poster): a bare `<ratio>` overrides the element's natural ratio, `auto <ratio>` prefers the natural ratio and falls back to the specified one, and `auto`/no value uses the natural ratio. Not yet applied: the CSS Sizing §5.1 *transferred* automatic-minimum-size for flex/grid items (the min-content-through-the-ratio min size), and height→width on non-replaced inline-block/float boxes. |
| `object-fit` | [object-fit](https://developer.mozilla.org/en-US/docs/Web/CSS/object-fit) | Supported on every replaced element that renders content — `<img>` (raster and SVG source), `<object>` resolving to an image, `<video>` (its `poster` image, which PeachPDF renders since it can't play video), and inline `<svg>`: `fill` (default — stretch to the content box), `contain` (scale to fit, preserving aspect ratio, letterboxing the remainder), `cover` (scale to cover, preserving aspect ratio, cropping the overflow to the content box), `none` (the content's intrinsic size), and `scale-down` (the smaller of `none` and `contain`). |
| `object-position` | [object-position](https://developer.mozilla.org/en-US/docs/Web/CSS/object-position) | Supported on the same replaced elements as `object-fit`, sharing the [`background-position`](#backgrounds) grammar (keywords, lengths, percentages, and the edge-offset form). Positions the `object-fit`-sized content within the content box (and selects which part is visible when `cover`/`none` overflows). Defaults to `50% 50%` (center). |

#### Logical box-model properties

The [CSS logical box-model properties](https://developer.mozilla.org/en-US/docs/Web/CSS/CSS_logical_properties_and_values) are supported and resolve to their physical equivalents, per each box's own resolved `direction` and `writing-mode` — following the [CSS Writing Modes Level 4 §7.1](https://www.w3.org/TR/css-writing-modes-4/#logical-to-physical) abstract-to-physical mapping table. Under the default `writing-mode: horizontal-tb` / `direction: ltr`, block-start/-end are top/bottom and inline-start/-end are left/right; a vertical `writing-mode` rotates block-start/-end onto the left or right edge instead, and `direction: rtl` flips which physical edge is inline-start/-end within whichever axis is inline for that writing mode (`sideways-lr` reverses the inline mapping relative to `vertical-rl`/`vertical-lr`/`sideways-rl`).

| Logical property | Resolves to | Notes |
|------------------|-------------|-------|
| `margin-block` / `margin-inline` | the two physical block-edge / inline-edge margins | 1–2 values (1 applies to both edges) |
| `margin-block-start` / `-end`, `margin-inline-start` / `-end` | one physical margin edge | single edge |
| `padding-block` / `padding-inline` (+ `-start`/`-end` longhands) | the physical `padding-*` edges | same 1–2-value / single-edge model as margin |
| `inset` | `top`+`right`+`bottom`+`left` | 1–4 values (the standard box-edge shorthand — always physical, not writing-mode-dependent, per spec) |
| `inset-block` / `inset-inline` (+ `-start`/`-end` longhands) | the physical `top`/`right`/`bottom`/`left` edges | 1–2-value / single-edge |
| `border-block-start` / `-end`, `border-inline-start` / `-end` | one physical border edge shorthand | `<width> <style> <color>` |
| `border-block` / `border-inline` | both block / both inline edges | one `<width> <style> <color>` applied to both edges |
| `border-block-width` / `-style` / `-color` (and `border-inline-*`) | the two physical edge width/style/color longhands | 1–2 values |
| `border-block-start-width` / `-style` / `-color` (all four edges) | the physical per-edge `border-*-width`/`-style`/`-color` longhands | single edge |

Each logical longhand keeps its own identity through the cascade and is resolved to a physical edge once the box's own `direction`/`writing-mode` are known, rather than being aliased directly onto a physical longhand at parse time. When a declaration block is serialized back to CSS, the logical longhands are used as declared — a logical shorthand (e.g. `margin-block`) is never reconstructed from them.

> **Limitation:** when a logical and a physical declaration target the same edge on the same box (e.g. both `margin-left` and `margin-inline-start` set, under `direction: ltr`), the logical value always wins regardless of which was actually declared later, rather than true last-declared-wins cascade order.

### Borders

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `border` | [border](https://developer.mozilla.org/en-US/docs/Web/CSS/border) | Shorthand supported; also `border-top`, `border-right`, `border-bottom`, `border-left` |
| `border-width` | [border-width](https://developer.mozilla.org/en-US/docs/Web/CSS/border-width) | Shorthand and all four longhands supported |
| `border-style` | [border-style](https://developer.mozilla.org/en-US/docs/Web/CSS/border-style) | Shorthand and all four longhands supported; values: `none`, `hidden`, `solid`, `dashed`, `dotted`, `double`, `inset`, `outset`, `groove`, `ridge`. `hidden` behaves exactly like `none` — the used [`border-width`](https://developer.mozilla.org/en-US/docs/Web/CSS/border-width) of that side is 0, so the edge is neither painted nor given any space in the box model — except inside a `border-collapse: collapse` table, where it also suppresses the shared grid line outright and beats every competing declaration ([CSS 2.1 §17.6.2](https://www.w3.org/TR/CSS21/tables.html#border-conflict-resolution), clause 1 — see the `border-collapse` row below). See [How each border style is drawn](#how-each-border-style-is-drawn) below |
| `border-color` | [border-color](https://developer.mozilla.org/en-US/docs/Web/CSS/border-color) | Shorthand and all four longhands supported |
| `border-collapse` | [border-collapse](https://developer.mozilla.org/en-US/docs/Web/CSS/border-collapse) | `collapse` resolves borders per CSS 2.1 [§17.6.2](https://www.w3.org/TR/CSS21/tables.html#border-conflict-resolution) (width, then style, then cell/row/row-group/column/column-group/table origin, then position), including on a repeated `<thead>`/`<tfoot>` across pages. The table's own border box contains each outermost grid line whole rather than the half §17.6.2 describes, and where two grid lines cross, one of them paints the whole square they share — both matching browsers; see [How each border style is drawn](#how-each-border-style-is-drawn). `border-radius` is undefined by the spec on a collapsed table and is not drawn there, matching common browser behavior |
| `border-spacing` | [border-spacing](https://developer.mozilla.org/en-US/docs/Web/CSS/border-spacing) | Full support for tables |

#### How each border style is drawn

[CSS 2.1 §8.5.3](https://www.w3.org/TR/CSS21/box.html#border-style-properties) defines what each keyword
means but leaves the exact rendering to the renderer. PeachPDF draws them to match what a browser
produces, so a document proofed in a browser prints the same way:

- **`dotted`** paints round dots whose diameter is the border width, and **`dashed`** paints dashes
  twice the border width. In both cases the ideal gap is one border width, but the gap is then
  stretched or squeezed so that a whole number of dots/dashes spans the edge exactly — the pattern
  always begins and ends flush with the corner rather than being cut off part-way through. Because the
  fit depends on the edge, the spacing changes slightly as a box's width or height changes, and an
  edge's horizontal and vertical runs may use slightly different gaps. An edge too short to hold two
  dashes is drawn solid. For square-cornered outlines and borders with matching patterned sides,
  translucent dots and dashes are composited once across adjoining edges, so their corners have the
  same opacity as the straight runs.
- **`double`** paints two lines with a gap between them, each exactly one third of the border width.
- **`groove`** and **`ridge`** each paint two halves of the border: `groove` draws its outer half as
  `inset` and its inner half as `outset`, and `ridge` does the reverse.
- **`inset`** and **`outset`** shade one pair of sides darker and the other lighter. `inset` darkens the
  top and left and lightens the bottom and right; `outset` is the mirror image. Both ends of the
  lightness range are special-cased so the bevel never disappears into itself: a color too dark to
  darken visibly lightens both faces instead, by differing amounts, while a color too light to lighten
  visibly keeps the declared color on the lit pair rather than being clipped to white.

On a [`border-collapse: collapse`](https://developer.mozilla.org/en-US/docs/Web/CSS/border-collapse)
table `inset` and `outset` are drawn from the same two faces but pick between them differently,
because a collapsed grid line is not any one box's edge: it straddles the boundary shared by whatever
lies on either side of it, so it shows **both** faces at once, one per half. The half at the smaller
coordinate (the left half of a column line, the upper half of a row line) takes the face a bottom or
right edge would, and the half at the larger coordinate the face a top or left edge would — on every
line alike, interior or outermost, whichever cell's, row's or table's declaration won it. A collapsed
`inset` therefore draws the same two bands as a `ridge`, and a collapsed `outset` the same as a
`groove`, which is what a browser paints for the same table. `groove` and `ridge` themselves are
unchanged by this: they already draw two halves, and the rule above happens to pick the same two
faces in the same order a box edge does, so they look identical either side of `border-collapse`. A page box's or page-margin box's border
has four real sides and so follows the per-side rule above instead, with one difference from an
element's: its edges are painted full-width and full-height rather than mitred, so where two of them
meet, the left or right edge's face covers the corner square outright instead of the two splitting it
along the diagonal.

Where two collapsed grid lines cross, the square they share is painted whole by one of them, and never
split along a diagonal. The wider line takes it; at equal width, the one whose style ranks higher
in [§17.6.2](https://www.w3.org/TR/CSS21/tables.html#border-conflict-resolution)'s order
(`hidden`, `double`, `solid`, `dashed`, `dotted`, `ridge`, `outset`, `groove`, `inset`); and failing
both, the row line, except on the table's first row line, where the column line takes it unless that
column line is the one at the table's inline start (its leftmost, in a left-to-right table). The four
corners of the table are crossings like any other and are filled accordingly, rather than left blank.
This is what a browser paints for the same table; CSS 2.1 resolves a border per grid-line *segment* and
says nothing about the square two crossing segments share. The one case where a square is still shared
is a crossing where a line's own resolved border changes from one side of it to the other — the two
runs meeting there split it between them rather than one taking it whole.

A collapsed table's own border box contains each outermost grid line **whole**. §17.6.2 instead puts
only half of it inside ("the width of the table includes half the table border"), which paints the
other half outside the table — over whatever precedes it, or clipped away entirely at a page or
container edge, so an outer line comes out thinner than an interior one. PeachPDF follows browsers
here, so no part of a collapsed border is ever painted outside the table it belongs to: a 2×2 table of
60×40px cells whose every border is `20px` occupies exactly 180×140px — 20 + 60 + 20 + 60 + 20 across
— rather than the 160×120px §17.6.2's half-line box would give it.

A bevelled border side whose color is [`currentcolor`](https://developer.mozilla.org/en-US/docs/Web/CSS/color_value#currentcolor_keyword)
— which is `border-color`'s initial value, so this covers every `border: 2px inset` that names no
color — does **not** shade the element's own [`color`](https://developer.mozilla.org/en-US/docs/Web/CSS/color).
It shades a fixed light grey, `rgb(238, 238, 238)`, whose two faces are `#9a9a9a` and `#eeeeee`. This
matches Chromium, and it is what makes an unstyled `<hr>` and a bare `border: 2px inset` look the way
they do in a browser rather than shading black text into a black-on-black frame. Consequences worth
knowing: `color` has no effect at all on such a border, a translucent or `transparent` `color` does not
make it translucent, and naming the color explicitly (`border: 2px inset #808080`) opts back into
shading that color — a different result (`#2c2c2c` over `#d4d4d4`) from the same grey left implicit.
Elements with a table [`display`](https://developer.mozilla.org/en-US/docs/Web/CSS/display) type
(`table`, `inline-table`, `table-row`, `table-cell`, and the rest) are exempt and do shade their own
`currentcolor`. `outline-color` and `column-rule-color` are never substituted this way, even when their
own style is bevelled — only the four `border-*-color` longhands are.

Because `currentcolor` is resolved per element rather than once during the cascade, a
`border-color: inherit` inherits the keyword itself, not the parent's resolved colour — so the child
resolves it against **its own** `color`, and against its own bevel base if the child's border style is
bevelled. A child with no `color` of its own inherits one, so it usually matches its parent anyway; a
child that sets `color` does not.

Every style mitres into its neighbours at the corners, including each individual line of a `double`
border and each half of a `groove`/`ridge`, so adjacent edges of differing width or color meet on the
corner's diagonal — and the mitre follows the line from the border box's outer corner to its inner
corner, so it stays correct when the two sides differ in width and the cut is not 45°. This is what
makes the classic zero-size "border triangle" work. `outline-style` uses all the same rendering,
banded outward from the border edge — an outline is drawn exactly as a border of the same width would
be on the rectangle that `outline-offset` + `outline-width` inflates the border box to, so a
`dotted`/`dashed` outline fits its pattern to that outer rectangle's sides and a dot lands in each of
its corners. A large negative `outline-offset` shrinks the outline into the border box, but its outside
shape remains at least twice the outline width in each dimension so the outline stays visible.

With `border-radius`, a border whose four sides share a style, color and width follows the curve as
one continuous outline, which keeps the corners seamless and lets a `dotted`/`dashed` pattern be
fitted to the whole perimeter so it runs evenly all the way round. `double` is drawn as two such
outlines at its thirds; `groove` and `ridge` are drawn as two curved half-width bands, with their
per-side bevel colors meeting through each corner.

Non-uniform rounded borders share the same curved corner-transition geometry across every style.
The transition follows the ratio of the adjoining widths, as allowed by [CSS Backgrounds and
Borders §4.4](https://www.w3.org/TR/css-backgrounds-3/#corner-transitions): solid and shaded sides
fill their part of the curve, each `double` line and `groove`/`ridge` half keeps its own band, and a
`dotted`/`dashed` centerline is clipped to its side's part of the corner. On an unsliced box, every
patterned side is phased against the complete rounded centerline before clipping, like a browser. This
means a small size change can move a dot or dash across a corner transition: a dot may visually join
the adjacent side at one size and sit wholly within its own side at another. Sliced fragments instead
fit each remaining open side independently and leave a square end where a physical edge is omitted
rather than closing a false rounded corner.

### Border Radius

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `border-radius` | [border-radius](https://developer.mozilla.org/en-US/docs/Web/CSS/border-radius) | Shorthand; supports 1–4 values with optional `/` for elliptical radii (e.g. `10px / 20px`) |
| `border-top-left-radius` | [border-top-left-radius](https://developer.mozilla.org/en-US/docs/Web/CSS/border-top-left-radius) | Accepts `<length>` or `<percentage>`; optional second value sets the vertical radius independently |
| `border-top-right-radius` | [border-top-right-radius](https://developer.mozilla.org/en-US/docs/Web/CSS/border-top-right-radius) | Same as above |
| `border-bottom-right-radius` | [border-bottom-right-radius](https://developer.mozilla.org/en-US/docs/Web/CSS/border-bottom-right-radius) | Same as above |
| `border-bottom-left-radius` | [border-bottom-left-radius](https://developer.mozilla.org/en-US/docs/Web/CSS/border-bottom-left-radius) | Same as above |

Percentages are relative to the border-box width (horizontal radius) and height (vertical radius). Overlapping adjacent radii are automatically reduced proportionally per the CSS spec.

### Border Image

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `border-image` | [border-image](https://developer.mozilla.org/en-US/docs/Web/CSS/border-image) | Shorthand for the five longhands below |
| `border-image-source` | [border-image-source](https://developer.mozilla.org/en-US/docs/Web/CSS/border-image-source) | `none`, or any `<image>` — a `url()` (raster or SVG), or a `linear-gradient()`/`radial-gradient()`/`conic-gradient()` (including their `repeating-` forms) |
| `border-image-slice` | [border-image-slice](https://developer.mozilla.org/en-US/docs/Web/CSS/border-image-slice) | 1–4 `<number>`/`<percentage>` values plus the optional `fill` keyword, in any order |
| `border-image-width` | [border-image-width](https://developer.mozilla.org/en-US/docs/Web/CSS/border-image-width) | 1–4 values, each `<length>`, `<percentage>`, `<number>`, or `auto` |
| `border-image-outset` | [border-image-outset](https://developer.mozilla.org/en-US/docs/Web/CSS/border-image-outset) | 1–4 values, each `<length>` or `<number>` |
| `border-image-repeat` | [border-image-repeat](https://developer.mozilla.org/en-US/docs/Web/CSS/border-image-repeat) | 1–2 keywords: `stretch` (default), `repeat`, `round`, `space` |

The source image is sliced into nine regions per the standard algorithm: the four corners are placed unchanged (scaled independently on each axis to fit `border-image-width`, but never tiled); the four edges are stretched or tiled along their one free axis per `border-image-repeat`; the center is painted, tiled the same way on both axes, only when `fill` is declared. An SVG source is sliced at its own intrinsic size — its `width`/`height`, or the `viewBox` they default to — so the artwork keeps its own proportions, exactly as `<img src="frame.svg">` would draw it. A gradient, or an SVG that declares no size at all, has no intrinsic size to slice against and is rendered at the size of the border-image area itself (the border box, extended by `border-image-outset`) instead; `border-image-slice` percentages on such a source are therefore relative to that area. On any vector or generated source, a bare `<number>` in `border-image-slice` counts CSS pixels of that source, not PDF points. Repainting the same SVG source at the same size reuses one document-local Form XObject, including across pages. Once `border-image-source` resolves to a real, loaded image, it replaces `border-style`'s own stroke entirely for that box; an unset or failed-to-load source falls back to painting the ordinary border as if `border-image` were never declared.

Known limitations:

- Slice regions are sampled without smoothing (nearest-neighbor). A region is cut out of the source with a clip, and a smoothing sampler bleeds the neighboring region's pixels across that cut — at the scale factors a small texture stretched over a border reaches, a visible fraction of the whole frame. A raster source stretched far past its own resolution therefore shows its pixels rather than a smooth ramp; a vector source is unaffected.
- `border-image-outset` accepts a `<percentage>` component (resolved against the same side's own border width) — broader than the specification's `<length> | <number>` grammar, a pre-existing behavior at the parsing layer.

### Outline

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `outline` | [outline](https://developer.mozilla.org/en-US/docs/Web/CSS/outline) | Shorthand for `outline-color`, `outline-style`, `outline-width`, in any order |
| `outline-color` | [outline-color](https://developer.mozilla.org/en-US/docs/Web/CSS/outline-color) | Any `<color>`, `currentcolor` (the initial value), or the legacy `invert` keyword |
| `outline-style` | [outline-style](https://developer.mozilla.org/en-US/docs/Web/CSS/outline-style) | `none` (initial), `auto`, `solid`, `dashed`, `dotted`, `double`, `groove`, `ridge`, `inset`, `outset`, each drawn as described under [How each border style is drawn](#how-each-border-style-is-drawn). `hidden` is **not** an `outline-style` value — [CSS Basic User Interface 4 §3.3](https://www.w3.org/TR/css-ui-4/#outline-style) excludes it, unlike every `border-*-style` — so a declaration using it is invalid and dropped, leaving whatever the cascade already resolved. Use `none` to suppress an outline |
| `outline-width` | [outline-width](https://developer.mozilla.org/en-US/docs/Web/CSS/outline-width) | `<length>`, or `thin`/`medium`/`thick`. Ignored when `outline-style` is `auto` — see below |
| `outline-offset` | [outline-offset](https://developer.mozilla.org/en-US/docs/Web/CSS/outline-offset) | `<length>`, may be negative to pull the outline back over the border |

Unlike `border`, outline is layout-neutral: it never participates in box sizing, is drawn entirely outside the border edge (offset by `outline-offset`), and can visually overlap sibling content without shifting anything. It is also drawn **on top** of that sibling content, following [CSS Positioned Layout 4's painting order](https://www.w3.org/TR/css-position-4/#painting-order). An outline paints over the in-flow content, floats, and `z-index: auto`/`0` positioned boxes of its stacking context. It paints **under** that stacking context's positive-`z-index` content, so a raised overlay covers an outline beneath it, as in Chromium. Browsers differ here: Firefox draws outlines over positive-`z-index` content as well. There is one deliberate difference from Chromium: Chromium also draws outlines under `z-index: auto`/`0` positioned boxes, which PeachPDF paints under the outline, so a following `position: relative` sibling cannot cover a ring. A positioned box, float, inline-block, or flex or grid item paints as one unit: it draws the outlines inside it before any later content paints. With `border-radius`, the outline follows the curve too: each corner radius expands outward by the same `outline-offset` + `outline-width` its rectangle itself expands by, matching Chromium's rounded-outline geometry — CSS Basic User Interface 4 leaves the exact treatment as a UA choice, but rounding to match is the one most browsers make.

When an inline element wraps across multiple lines, its outline is drawn as **one connected shape** around all of them rather than leaving the two line-wrap edges open the way `border`/`background` do under `box-decoration-break: slice` (the default) — CSS Basic User Interface 4 recommends a fragmented outline be fully connected rather than open on some sides. The shape is the outline of the region the lines' rectangles cover once `outline-offset` has been applied to each of them: where consecutive lines touch or overlap — which is what a `border`, extra padding, or a positive `outline-offset` on a wrapped inline typically causes — they merge into a single stepped contour, with the outline running around its concave corners rather than closing across each wrap. Lines that don't touch stay separate pieces of the same shape, which is how an ordinary widely-spaced wrapped inline still reads as one ring per line. This matches Chromium, which builds the same region the same way.

Every outline style follows that shape, including the patterned and bevelled ones. On a square-cornered contour, `dotted` and `dashed` fit their pattern along each straight edge, so a dash lands on every corner it turns, including the concave ones a merge introduces; a rounded one has no corner for a dash to land on, so it is fitted to the closed contour as a whole, which spaces the pattern evenly all the way round rather than restarting it at each corner. `groove`, `ridge`, `inset` and `outset` pick each edge's light or dark face from the direction that edge runs in rather than from which side of a box it is — the only rule that is still well defined once the lines have merged and a "side" no longer is. Both match what Chromium does for the same content.

For translucent square-cornered `dotted` and `dashed` outlines, each corner dot or dash is painted once, so the corners have the same opacity as the straight runs. The same applies to borders with matching patterned sides.

One limitation remains: only the element's own line boxes take part in the region, not the border boxes of any atomic inlines (images, inline-blocks) inside it, so an outline around content taller than its own line box can cut through it where Chromium's would go around.

A page or column break is a different axis and is unaffected by any of this: the region is always built from one page's own rectangles, so a box broken across pages gets its own shape on each, and the block-axis edge the break cuts through stays open on both sides of it, exactly as border's own `slice` geometry already leaves it.

`outline-style: auto` is a UA-defined appearance, typically used for a browser's own focus ring. CSS Basic User Interface 4 says the `outline-width` property **is ignored** when `outline-style` is `auto`, and separately allows a UA to treat `auto` as `solid` — the second is a choice about the style, not a licence to keep the author's width. PeachPDF therefore draws `auto` as a solid ring at its own fixed width of 2px, whatever `outline-width` says (including `0`), matching the ring Chrome draws. That ring is also *centred* on the edge `outline-offset` puts it at, rather than sitting wholly outside it the way every other style does, so with the default zero offset it straddles the border edge by 1px either side — again matching Chrome. If you want an outline of a width you control, use any other `outline-style`.

`outline-color: invert` is a legacy value: rather than approximating it with a fixed color, PeachPDF renders it as a true per-pixel color inversion of whatever is underneath the outline, using a PDF blend mode (`/Difference` composited with white, which is mathematically equivalent to inverting the backdrop). This is a genuine inversion, not a fixed guess at a contrasting color, and it composites correctly regardless of what's behind the outline at render time.

### Box Shadow

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `box-shadow` | [box-shadow](https://developer.mozilla.org/en-US/docs/Web/CSS/box-shadow) | Comma-separated list of shadow layers, each `inset? <offset-x> <offset-y> <blur-radius>? <spread-radius>? <color>?`. Supports outset (drop) and `inset` shadows, negative offsets, negative spread, multiple layers, and an omitted color (which uses the element's own `color`, i.e. `currentColor`). Offsets, blur and spread may use any `<length>` unit (including `em`/`rem`) or a `calc()`/`min()`/`max()`/`clamp()` expression; a value with an unrecognised unit (`2foo`) makes the declaration invalid, and a percentage is not a valid length here. The first-listed layer paints on top. Honors `border-radius`. **A blurred shadow is a real Gaussian blur** — the blur radius is twice the standard deviation, as the specification defines it — rendered into a bitmap beneath the element, which itself stays vector; see [Rasterized effects](#rasterized-effects). An outset shadow is knocked out under the element's own border box (following its rounded corners), so it never shows through a transparent element, and a rounded `inset` shadow is confined to the padding edge, whose corner radius is the `border-radius` minus the border width, with its lit inner edge keeping rounded corners too. A shadow with no blur is plain vector geometry. Where the output cannot carry a bitmap, the blur falls back to a stack of concentric fills that approximates it. |

The first two lengths are the shadow's horizontal and vertical offset; the optional third is the blur radius (must be non-negative) and the optional fourth is the spread radius. Multiple shadows are layered, the first-listed drawn last (on top). An outset shadow paints behind the element's background; an `inset` shadow paints over the background, clipped to the padding box and fading inward.

Blur rendering: PDF has no blur, so a blurred shadow is drawn into a bitmap at `PdfGenerateConfig.RasterizationDpi`, Gaussian-blurred, and placed beneath the element. Only the shadow is a bitmap — the element's own background, border and text stay vector, selectable and tagged — and a shadow is knocked out under the box it belongs to. Under PDF/A-1 or PDF/X-1a/X-3, which forbid the soft mask a blurred bitmap needs, a document using a blurred shadow is rejected, as any semi-transparent shadow always was.

### Transforms

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `transform` | [transform](https://developer.mozilla.org/en-US/docs/Web/CSS/transform) | Supports `matrix()`, `matrix3d()`, `translate()`/`translateX()`/`translateY()`/`translateZ()`/`translate3d()`, `scale()`/`scaleX()`/`scaleY()`/`scaleZ()`/`scale3d()`, `rotate()`/`rotateX()`/`rotateY()`/`rotateZ()`/`rotate3d()`, and `skew()`/`skewX()`/`skewY()` (their angle arguments accept a unitless `0`, e.g. `rotate(0)`; `skew()`'s second argument is optional). Multiple functions may be chained in one value; they compose per spec (the last-listed function is applied first, closest to the element). Not inherited. `perspective()` is supported: an element whose transform involves it, or a `perspective` on its parent, is drawn in perspective (see below). |
| `perspective` | [perspective](https://developer.mozilla.org/en-US/docs/Web/CSS/perspective) | `none` or a `<length>`: how far the viewer is from the plane, for this element's **direct children** (as the `perspective()` function does for the one element it is in). A child's 3D transform is then viewed through it, from the point `perspective-origin` names. A deeper descendant sees it too when the child is a `transform-style: preserve-3d` box that keeps it in the same 3D space. Not inherited. See [3D transforms and perspective](#3d-transforms-and-perspective). |
| `perspective-origin` | [perspective-origin](https://developer.mozilla.org/en-US/docs/Web/CSS/perspective-origin) | The vanishing point, a `<position>` (one or two `<length>`/`<percentage>`/keyword values) relative to the element's border box; `50% 50%` by default. |
| `backface-visibility` | [backface-visibility](https://developer.mozilla.org/en-US/docs/Web/CSS/backface-visibility) | `visible` (default) or `hidden`. A `hidden` element is not painted while its back faces the viewer, as decided by the transformed normal: `rotateY(180deg)` shows the back, `scaleX(-1)` (a mirror in the plane) does not. |
| `transform-style` | [transform-style](https://developer.mozilla.org/en-US/docs/Web/CSS/transform-style) | `flat` (default) or `preserve-3d`, which puts an element's children in the same 3D space as the element: their transforms accumulate down the tree, and the planes are depth-sorted and, where they intersect, resolved per pixel (a cube of six faces, a carousel, a flip card with a hidden back). Not inherited. A grouping property (`overflow` other than `visible`, `opacity` below 1, `filter`, `backdrop-filter`, `clip-path`, a `mix-blend-mode` other than `normal`) forces the used value back to `flat`. `preserve-3d` establishes a stacking context. See [3D transforms and perspective](#3d-transforms-and-perspective). |
| `transform-origin` | [transform-origin](https://developer.mozilla.org/en-US/docs/Web/CSS/transform-origin) | 1–3 values (`<length>`/`<percentage>`/keyword for X and Y, plain `<length>` for Z). X/Y percentages resolve against the border-box. Defaults to `50% 50% 0`. Not inherited. |

#### 3D transforms and perspective

3D transform functions are composed as a genuine 4×4 matrix and projected onto the element's own flat plane. Without perspective that projection is affine and mathematically exact — `translate3d()`/`scale3d()`/`rotateX()`/`rotateY()`/`rotate3d()`/`matrix3d()` all render as true, lossless 2D transforms of the flattened element, in vector form.

**Perspective is not affine**, so a PDF transformation matrix cannot carry it: parallel edges stop being parallel and a receding plane foreshortens. An element whose transform involves `perspective()`, or that is a direct child of a `perspective` container and has a 3D transform that moves it out of its plane (`rotateX()`, `rotateY()`, `rotate3d()`), is therefore painted into a bitmap in its own untransformed space and warped through the projective map at `PdfGenerateConfig.RasterizationDpi` (see [Rasterized effects](#rasterized-effects)); a plane brought closer than its flat size is rendered with proportionally more pixels so it does not blur. A plane parallel to the view plane (`translateZ()` alone) is only scaled, and stays vector. The part of an element that passes behind the viewer is not drawn.

- **Text** in a warped element stays selectable: it is supplied again as invisible text positioned by the transform's linearisation at the element's centre, which is exact there and approximate towards the edges under strong perspective.
- **Filters, opacity, `clip-path` and blend modes** on a warped element apply to it before the warp, in its own space, as they do without perspective.
- **Not modelled.** A warped element inside another transformed ancestor is warped in its parent's untransformed space. Under PDF/A-1 and PDF/X-1a/X-3 the warped bitmap has soft edges and is rejected unless the document asks for [flattening](usage-examples.md#flattening-transparency-for-pdfa-1-and-pdfx).

**Shared 3D space (`transform-style: preserve-3d`).** An element whose used `transform-style` is `preserve-3d` starts a *3D rendering context*: it and every descendant reached through a chain of `preserve-3d` elements are *planes* in one space. Each plane's transform is accumulated down from the context's root (its own transform about its `transform-origin`, then the `perspective` its parent gives its children, then its parent's whole transform), so a child of a rotated parent rotates with it and a `perspective` set above the context reaches every plane in it, however deep. Whatever is not itself in the chain (an element with `transform-style: flat`, or one a grouping property forces flat) is flattened into its plane.

- The planes are painted into bitmaps and composed into one picture through a per-pixel depth test at `PdfGenerateConfig.RasterizationDpi`: a nearer plane covers a farther one whatever the document order, and planes that intersect are resolved exactly where they cross rather than by sorting. Translucent planes blend in back-to-front order of their centres. Each plane honours its own `backface-visibility`.
- A context whose planes all stay parallel to the view plane at one depth has nothing to resolve, and is painted exactly as it would be without `preserve-3d`, in vector form.
- Text stays selectable, as for a single warped element.
- **Approximations.** A translucent pixel does not hide what is behind it from a plane painted after it (the planes are ordered back to front by their centres, which is exact unless translucent planes intersect each other).

### Opacity

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `opacity` | [opacity](https://developer.mozilla.org/en-US/docs/Web/CSS/opacity) | Full support; not inherited (it composites the element and its whole subtree as a group, per spec). Rendered as a genuine, isolated PDF transparency group — the element's subtree is painted into an offscreen Form XObject and flattened, then that single flattened result is composited onto the page at the given alpha, so overlapping content within the element (e.g. two overlapping semi-transparent children) doesn't double-darken where it overlaps. |
| `clip-path` | [clip-path](https://developer.mozilla.org/en-US/docs/Web/CSS/clip-path) | Basic shapes are supported: `polygon()` (with an optional `nonzero`/`evenodd` fill rule), `inset()` (1–4 edge offsets, with an optional `round <border-radius>`), `circle()` and `ellipse()` (with a `<length-percentage>` or `closest-side`/`farthest-side` radius and an optional `at <position>`), `path()`, `url(#id)` (referencing an SVG `<clipPath>` element), and `none`. A shape (or a bare `<geometry-box>` with no shape) is resolved against the element's **border-box** by default, or against another box selected by a trailing or leading `<geometry-box>` keyword (`content-box`, `padding-box`, `margin-box`; `fill-box`/`stroke-box`/`view-box` compute to border-box for a plain HTML element per spec) — `circle(50%) padding-box` and `padding-box circle(50%)` are equivalent. A bare `<geometry-box>` (no shape function at all) clips to that box's own shape, including the element's declared `border-radius` for `border-box`/`padding-box`/`content-box` (reduced by the border/padding width for the latter two, the same as `background-clip`); `margin-box` has no CSS-defined outward radius growth and always clips to a sharp rectangle. The result clips the whole element (background, border, content, and descendants) and is transformed together with any `transform` on the element. A `calc()` expression is accepted as a `polygon()`/`inset()` coordinate or radius (resolved against the reference box at layout time). Invalid values are dropped at parse time. `inset(... round <border-radius>)` accepts the same 1–4-value (optionally `/`-split horizontal/vertical) grammar as the `border-radius` property; an invalid or negative-literal radius invalidates the whole declaration, and radii that would overlap are proportionally reduced per [CSS Backgrounds and Borders Level 3 §4](https://www.w3.org/TR/css-backgrounds-3/#corner-overlap), the same algorithm the `border-radius` property itself uses. Offsets that overconstrain `inset()`'s width or height (their sum exceeds the reference box's own extent) are proportionally reduced rather than producing an inverted rectangle. `path()` takes an optional leading `nonzero`/`evenodd` fill rule and a string of [SVG path data](https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Attribute/d) (the same grammar `<path d="...">` uses); its numbers are unitless CSS pixels, with the path's own (0, 0) origin placed at the resolved reference box's top-left corner — there is no percentage resolution against the box's size, unlike the other shapes. Per spec, a `path()` string that isn't well-formed SVG 1.1 path data, or that parses to no segments, is itself invalid and drops the whole `clip-path` declaration, the same as any other invalid value. `url(#id)` references a `<clipPath>` element defined anywhere in the document — including inside an `<svg>` that is never itself rendered, such as the common `<svg style="display:none">` defs-only pattern — and honors that `<clipPath>`'s own `clipPathUnits` (`userSpaceOnUse`, the default, with the same unitless-pixel/origin convention as `path()`; or `objectBoundingBox`, mapping its `0..1` child geometry onto the resolved reference box); an unresolvable id drops the clip (no clipping applied) rather than the whole declaration. |
| `clip` | [clip](https://developer.mozilla.org/en-US/docs/Web/CSS/clip) | Legacy CSS 2.1 rectangular clipping (superseded by `clip-path` above, kept for compatibility). Per [CSS 2.1 §11.1.2](https://www.w3.org/TR/CSS21/visufx.html#clipping), applies only to absolutely/fixed-positioned elements — it has no effect on a statically or relatively positioned box. `rect(<top>, <right>, <bottom>, <left>)` (comma- or space-separated) and `auto` are supported; `top`/`bottom` are offsets from the top border edge and `right`/`left` are offsets from the left border edge — not a width/height pair — and `auto` on an individual edge means "use the box's own edge there" (don't clip that side). Resolved against the element's border-box, like `clip-path`. |

### Filters and Blend Modes

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `mix-blend-mode` | [mix-blend-mode](https://developer.mozilla.org/en-US/docs/Web/CSS/mix-blend-mode) | All 16 standard values (`normal`, `multiply`, `screen`, `overlay`, `darken`, `lighten`, `color-dodge`, `color-burn`, `hard-light`, `soft-light`, `difference`, `exclusion`, `hue`, `saturation`, `color`, `luminosity`), rendered as a genuine PDF blend mode (`ExtGState /BM`, ISO 32000-1 §11.3.5) — not an approximation. Per [CSS Compositing and Blending 1 §3.1](https://www.w3.org/TR/compositing-1/#mixing), any value other than `normal` establishes a stacking context (see [Stacking Context](#stacking-context) above). |
| `filter` | [filter](https://developer.mozilla.org/en-US/docs/Web/CSS/filter) | Every filter function is supported. `opacity()`, `brightness()`, `contrast()`, and `invert()` are genuinely native, real PDF color math rather than an approximation (see below). `blur()`, `grayscale()`, `sepia()`, `saturate()`, and `hue-rotate()` have no PDF equivalent, so an element carrying one is rendered into a bitmap, filtered, and embedded — see [Rasterized effects](#rasterized-effects) below. `drop-shadow()` follows the element's real alpha shape and is rendered the same way (see below). [`backdrop-filter`](#filters-and-blend-modes) has its own row. Per [Filter Effects 1 §3](https://www.w3.org/TR/filter-effects-1/#FilterProperty), any value other than `none` establishes a stacking context (see [Stacking Context](#stacking-context) above). |
| `backdrop-filter` | [backdrop-filter](https://developer.mozilla.org/en-US/docs/Web/CSS/backdrop-filter) | The same function list as `filter` (`blur()`, `brightness()`, `contrast()`, `grayscale()`, `hue-rotate()`, `invert()`, `opacity()`, `saturate()`, `sepia()`; `drop-shadow()` has no meaning on a backdrop and is ignored), applied to whatever was painted behind the element, inside its border box, and drawn under the element's own background, clipped to its `border-radius`. `-webkit-backdrop-filter` is the same property. The filtered backdrop is a bitmap at `PdfGenerateConfig.RasterizationDpi` (see [Rasterized effects](#rasterized-effects)); the edges of the box are extended by mirroring for the blur, so it does not darken at the edges. The element creates a stacking context, and is a *backdrop root* for its own descendants. Only what is inside the element's nearest backdrop root is the backdrop: an ancestor with `opacity` below 1, a `filter`, a `mix-blend-mode`, a `clip-path` or its own `backdrop-filter` isolates its content from what lies outside it; with none, the backdrop is everything painted before it on the page, over white paper. The filter is not applied to an element with a `transform`, or under a transformed ancestor that is inside its backdrop root, because its backdrop is then in another coordinate space (the element paints without it). A nested `backdrop-filter` sees the earlier one's result, to a depth of three. Under PDF/A-1 or PDF/X-1a/X-3 it is rejected, like any effect that needs a bitmap with soft edges |

`opacity()` composes into the same isolated-transparency-group alpha the `opacity` property itself uses (see [Opacity](#opacity) above). `brightness()`, `contrast()`, and `invert()` are each an affine, per-color-channel transform — [CSS Filter Effects 1](https://www.w3.org/TR/filter-effects-1/#FilterPrimitivesSubregion) defines every one of them as an equivalent `feColorMatrix`/`feComponentTransfer` — so they map directly onto a PDF `ExtGState /TR` transfer function (ISO 32000-1 §8.6.5.3): real, spec-correct color math evaluated by the PDF reader itself, not a rasterized approximation. Multiple native functions chained in one `filter:` value still compose into a single transfer function, so a whole native-only filter chain costs one PDF object, the same as one function alone.

`drop-shadow(<offset-x> <offset-y> <std-deviation>? <color>?)` draws a blurred, offset copy of the element's real alpha shape — its glyphs, an image's transparent corners, an SVG's outline, a `clip-path` — in the shadow colour, beneath the element. Unlike `box-shadow`, the third length is the standard deviation itself, not twice it. It is rendered in the same bitmap as any other raster `filter` function and applied in list order, so `drop-shadow(…) drop-shadow(…)` shadows the first shadow too, as the specification describes. A graphics that cannot rasterize falls back to shadowing the element's border-box rectangle.

#### Rasterized effects

A few effects cannot be expressed as vector PDF content. A blur needs per-pixel convolution (which also means a blurred `box-shadow` or `text-shadow`, and a `drop-shadow()` that follows an element's real shape), and `grayscale()`, `sepia()`, `saturate()` and `hue-rotate()` each [mix color channels together](https://developer.mozilla.org/en-US/docs/Web/CSS/filter-function/grayscale), while a PDF can only recolor each channel on its own. For these, PeachPDF renders the element into a bitmap, applies the whole `filter` list to it in the order it was written, and embeds the result. `backdrop-filter` is the same idea applied to the content painted behind an element, and an SVG `<filter>` with a blur, lighting or other pixel primitive is evaluated the same way (see [SVG filters](supported-svg-features.md#filters)). Everything else on the page stays vector.

- **Which elements.** Any element whose `filter` list contains `blur()`, `grayscale()`, `sepia()`, `saturate()`, `hue-rotate()` or `drop-shadow()` with an actual effect (`blur(0)`, `grayscale(0)`, `saturate(1)` and `hue-rotate(0deg)` change nothing and stay vector). The bitmap covers the element, all of its descendants and its own `box-shadow`, grown by three standard deviations of every `blur()` so the blur has room to spread. A `brightness()`, `contrast()`, `invert()` or `opacity()` written next to one of these functions is applied in the same bitmap pass, in order; on their own they stay native vector math. The element's own `opacity` and `mix-blend-mode` are applied after the filters, as the specifications order them.
- **Blur radius.** `blur(<length>)` is the standard deviation of the Gaussian, in the length's own units. Up to a deviation of 2 device pixels the exact kernel is used; beyond that, the three-pass box approximation Filter Effects 1 specifies for `feGaussianBlur`.
- **Resolution.** The bitmap is rendered at `PdfGenerateConfig.RasterizationDpi` pixels per inch of paper (default `300`; `72` to `1200`; `--raster-dpi` on the [command line](cli.md)), so it stays sharp when zoomed or printed, up to that resolution. The value is a physical resolution, independent of `PixelsPerInch`: the bitmap is always placed at exactly the size of the content it replaces, so an inch stays an inch and only the number of pixels behind it changes — with `PixelsPerInch = 96` and a value of `288`, each CSS pixel is backed by 3 × 3 = 9 bitmap pixels. A region that would exceed `PdfGenerateConfig.MaxRasterPixels` (64 million pixels by default) is rendered at a lower resolution just large enough to fit; its placed size never changes. Bitmaps are exempt from `DownscaleImages`. See [Rasterized effects and resolution](usage-examples.md#rasterized-effects-and-resolution).
- **Text.** The text of a rasterized HTML element is drawn into the bitmap, and PeachPDF also places the same text over it as invisible, positioned text, so it can still be selected, searched and copied, and tagged-PDF structure still points at it. Text drawn by an inline `<svg>` or `<math>` inside the element is part of the bitmap only, and `text-decoration` lines are not part of the selectable overlay.
- **Limits.** Colour-emoji (`COLR`) glyphs inside a rasterized element are drawn as flat monochrome outlines, `text-decoration-skip-ink` is not applied, a CMYK image is drawn through a colour-unmanaged RGB conversion (the PDF keeps its CMYK data everywhere else), and a stroke thinner than one device pixel is widened to one pixel. A document that requests [PDF/A-1 or PDF/X-1a/X-3](usage-examples.md#pdfa-1-and-transparency) conformance is rejected if it uses one of these effects, because a bitmap with soft edges needs a transparency soft mask, which those levels forbid.

### Backgrounds

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `background` | [background](https://developer.mozilla.org/en-US/docs/Web/CSS/background) | Shorthand supported; all longhand components are parsed and applied |
| `background-color` | [background-color](https://developer.mozilla.org/en-US/docs/Web/CSS/background-color) | Full support |
| `background-image` | [background-image](https://developer.mozilla.org/en-US/docs/Web/CSS/background-image) | URL, data URI, and all CSS gradient functions: `linear-gradient()`, `radial-gradient()`, `conic-gradient()`, `repeating-linear-gradient()`, `repeating-radial-gradient()`, and `repeating-conic-gradient()`; all accept multi-stop gradients with stop positions in any `<length-percentage>` unit (absolute, percentage, `em`/`rem`/`ex`/`ch`, or a viewport/container-relative unit), two-position hard-stop shorthand, color hints, and `rgba()`/alpha transparency; radial gradients support `circle`/`ellipse` shape, `at <position>` centering, an explicit radius in any of those same length units, and all four size keywords; conic gradients support `from <angle>` and `at <position>`, and a `calc()` expression is accepted as an angular color-stop position (e.g. `calc(1turn * 0.35)`); all gradient functions support CSS Color Level 4 color-space interpolation (`in srgb`, `in srgb-linear`, `in display-p3`, `in lab`, `in oklab`, `in lch`, `in oklch`, `in hsl`, `in hwb`, `in xyz`/`in xyz-d65`, `in xyz-d50`) with polar hue-interpolation methods (`shorter hue`, `longer hue`, `increasing hue`, `decreasing hue`); the `in <color-interpolation-method>` prelude is validated against [CSS Images 4 §3.1](https://drafts.csswg.org/css-images-4/#color-interpolation-method) and an invalid one is dropped at parse time (an unknown color space, a hue method on a rectangular space, or a malformed direction). The wide-gamut RGB interpolation spaces `a98-rgb`, `prophoto-rgb`, and `rec2020` are valid CSS but not supported, and a gradient that requests one is dropped rather than approximated. A url() source that is an SVG (`.svg` file, `data:image/svg+xml`) renders as real vector content — a reusable PDF Form XObject positioned/sized/repeated via `background-position`/`background-size`/`background-repeat` exactly like a raster image — not rasterized; see [Supported SVG Features](supported-svg-features.md) |
| `background-position` | [background-position](https://developer.mozilla.org/en-US/docs/Web/CSS/background-position) | Full support: keywords, lengths, percentages, `calc()`, and the 4-value edge-offset syntax (e.g. `right 10px bottom 20px`); applies to url() images and gradients alike. Comma-separated multi-layer values cycle against the number of `background-image` layers (a single value applies to every layer) |
| `background-size` | [background-size](https://developer.mozilla.org/en-US/docs/Web/CSS/background-size) | Full support: `auto`, `cover`, `contain`, lengths, percentages, and `calc()`, for both url() images and gradients — a gradient with an explicit size smaller/larger than the box is rendered once and then positioned/repeated exactly like an image. Comma-separated multi-layer values cycle against the number of `background-image` layers |
| `background-repeat` | [background-repeat](https://developer.mozilla.org/en-US/docs/Web/CSS/background-repeat) | Full support: all keywords (`repeat`, `no-repeat`, `repeat-x`, `repeat-y`, and the 1/2-value forms). Comma-separated multi-layer values cycle against the number of `background-image` layers |
| `background-origin` | [background-origin](https://developer.mozilla.org/en-US/docs/Web/CSS/background-origin) | Full support; `border-box`, `padding-box`, `content-box`. Comma-separated multi-layer values cycle against the number of `background-image` layers |
| `background-attachment` | [background-attachment](https://developer.mozilla.org/en-US/docs/Web/CSS/background-attachment) | `scroll` (default) and `fixed` are supported. Since a PDF page has no scrolling viewport, `fixed` is given the paginated-media meaning of CSS Paged Media: the background positioning area is the page box rather than the element's own box, and it repeats identically (page-anchored) on every page — the same page-anchored idea `position: fixed` uses, though that one's containing block is the page *area* (see [`position`](#positioning)). `background-clip` is unaffected, so the image is still only ever visible within the element's own box. Comma-separated multi-layer values cycle against the number of `background-image` layers |
| `background-clip` | [background-clip](https://developer.mozilla.org/en-US/docs/Web/CSS/background-clip) | Full support; `border-box`, `padding-box`, `content-box`, `text`. Comma-separated multi-layer values cycle against the number of `background-image` layers; when there are multiple values, `background-color` uses the last (bottom-most) one, per spec. For `padding-box`/`content-box` on a box with `border-radius`, the inner curve's radius is the border-box radius reduced by the border width (and, for `content-box`, the padding too), clamped to zero, per the spec's corner-clipping rule. `text` clips every background layer to the union of the element's own laid-out glyph outlines, including nested inline descendants — the standard gradient-text idiom (`background: linear-gradient(...); background-clip: text; color: transparent;`) renders as intended. Unlike this library's SVG gradient-text support, the glyphs stay ordinary selectable/extractable PDF text (`color: transparent` only makes them invisible, it does not convert them to vector art). A box under a true vertical `writing-mode` (`vertical-rl`/`vertical-lr`) clips to the same real glyph-outline union: an upright run builds it character by character — each character's outline additionally clipped to its own reserved cell when its font carries real OpenType vertical metrics (`vhea`/`vmtx`/`VORG`), matching the identical per-cell clip painting itself already applies — and a rotated (sideways) run builds one whole-run outline and carries it into its physical footprint with the same rotation used to paint it. A box falls back to `border-box` (rounded corners preserved) when its font has no decodable outline, or the box uses `writing-mode: sideways-rl`/`sideways-lr` (a different value, laid out horizontally with only its glyphs rotated) |

#### Canvas background (`<html>`/`<body>`)

Per [CSS Backgrounds 3 §2.11.2](https://www.w3.org/TR/css-backgrounds-3/#root-background), the root element's background doesn't just paint its own box — it fills the whole *canvas* (here: every page). PeachPDF resolves this once per document: `<html>`'s own background (`background-color` and/or `background-image` layers) is used if it declares one; otherwise `<body>`'s background is propagated to the canvas instead; otherwise no canvas fill happens at all. When `<html>` has a background of its own, `<body>`'s background is not propagated and paints as an ordinary box background over `<body>`'s own box. Whichever element was used for the canvas fill isn't separately re-painted at its own (possibly much smaller than a page) laid-out rect — the canvas fill already covers it. A non-`<body>`/`<html>` element's own background (e.g. a `<div>`) is unaffected and continues to paint normally. The fill repeats identically on every page the document spans. The `@page` box's own background — a further tier *below* this canvas fill — is documented in [`@page` rule](#page-rule) below.

### Color & Typography

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `color` | [color](https://developer.mozilla.org/en-US/docs/Web/CSS/color) | Full support for named colors, hex, `rgb()`/`rgba()` in both the legacy comma form (`rgb(r, g, b)`, `rgba(r, g, b, a)`) and the CSS Color Level 4 space-separated form with an optional slash alpha (`rgb(r g b)`, `rgb(r g b / a)`, `rgba(r g b / a%)`), `hsl()`/`hsla()`, `hwb()`, and the CSS Color Level 4 wide-gamut functions `oklch()`, `oklab()`, `lab()`, and `lch()` (number or percentage components, an angle hue with any unit for the polar forms, and an optional `/ <alpha>`), all resolved to sRGB. [`color-mix()`](https://developer.mozilla.org/en-US/docs/Web/CSS/color_value/color-mix) is supported (`color-mix(in <space>, <color> <p>?, <color> <p>?)`) — premultiplied-alpha mixing in any of the interpolation spaces above, so the common opacity-modifier shape `color-mix(in oklab, <color> 50%, transparent)` yields that color at half alpha, including a hex operand of either form (`#e11d48` or `#2563eb`). These color functions work anywhere a `<color>` is accepted (`color`, `background-color`, the `background`/`border` shorthands, gradient color stops). [`device-cmyk()`](https://developer.mozilla.org/en-US/docs/Web/CSS/color_value/device-cmyk) (`device-cmyk(c m y k)`/`device-cmyk(c m y k / a)`, number or percentage components, `/`-only alpha — no legacy comma-alpha form) is also supported, and unlike every color function above, it is **not** approximated to sRGB: a `device-cmyk()`-authored `color`/`background-color`/border color is carried through to the PDF as a real `DeviceCMYK` fill or stroke operator, exactly as authored — PDF's own support for a document mixing `DeviceRGB` and `DeviceCMYK` content means an RGB-authored color elsewhere on the same page is completely unaffected. `color()`, and CSS Color 5 relative-color syntax, are not supported. A `color-mix()` between two `device-cmyk()` operands mixes directly in C/M/Y/K space (ignoring the declared `in <space>` keyword, which has no CMYK equivalent); a gradient whose stops are all `device-cmyk()` likewise interpolates directly in C/M/Y/K space, the same way an all-RGB gradient interpolates in RGB space. A `color-mix()` or gradient mixing a `device-cmyk()` operand with an RGB-authored one has no defined conversion and is rejected (invalid declaration / thrown error respectively) — see [PDF/X-conformant output](usage-examples.md#generating-pdf-x-conformant-output) for how an achromatic (gray/black) RGB color is still handled under strict CMYK-only PDF/X-1a output |
| `font` | [font](https://developer.mozilla.org/en-US/docs/Web/CSS/font) | Shorthand supported; all components are parsed |
| `font-family` | [font-family](https://developer.mozilla.org/en-US/docs/Web/CSS/font-family) | Full support. Generic families (`serif`, `sans-serif`, `monospace`, `cursive`, `fantasy`) and `system-ui` resolve to a real installed substitute matching actual Chromium behavior per platform (hardcoded specific families on Windows/macOS/Android, delegated to the OS's own fontconfig on Linux) — see [Fonts](usage-examples.md#fonts) for the full per-platform table. Every mapping is verified against the system's actually-installed fonts before use, falling back to the platform default font otherwise, so a requested substitute that isn't present never silently resolves to an arbitrary, unrelated font |
| `font-size` | [font-size](https://developer.mozilla.org/en-US/docs/Web/CSS/font-size) | Full support including absolute sizes (`medium`, `large`, etc.), relative sizes (`smaller`, `larger`), lengths, and percentages |
| `font-stretch` | [font-stretch](https://developer.mozilla.org/en-US/docs/Web/CSS/font-stretch) | The 9 CSS Fonts Level 3 keywords (`ultra-condensed` … `normal` … `ultra-expanded`); inherited and cascaded, and consulted for real face selection when a family has multiple registered faces at different stretch values (e.g. two `@font-face` rules with different `font-stretch` descriptors) — nearest-stretch matching per CSS Fonts Level 4 §5.2. Percentage/range values (the variable-font syntax) are not supported |
| `font-style` | [font-style](https://developer.mozilla.org/en-US/docs/Web/CSS/font-style) | `normal`, `italic`, `oblique`, and CSS Fonts Level 4's `oblique <angle>` (e.g. `oblique 10deg`) — when no real oblique/italic face is available and the renderer has to synthesize one, an explicitly declared angle drives the exact synthesized shear amount instead of a fixed default |
| `font-variant` | [font-variant](https://developer.mozilla.org/en-US/docs/Web/CSS/font-variant) | Real shorthand over `font-variant-caps`, `font-variant-ligatures`, `font-variant-numeric`, `font-variant-east-asian`, `font-variant-position`, `font-variant-alternates`, and `font-feature-settings`, combinable in one declaration (e.g. `font-variant: small-caps common-ligatures oldstyle-nums;`) — setting it resets every covered longhand it doesn't mention back to its initial value, per CSS Cascading. `none` sets `font-variant-ligatures` to `none` and every other covered longhand to `normal` |
| `font-variant-caps` | [font-variant-caps](https://developer.mozilla.org/en-US/docs/Web/CSS/font-variant-caps) | All 7 keywords (`normal`, `small-caps`, `all-small-caps`, `petite-caps`, `all-petite-caps`, `unicase`, `titling-caps`) parse and cascade. When the resolved font actually has the corresponding OpenType `GSUB` feature (`smcp`/`c2sc`/`pcap`/`c2pc`/`unic`/`titl` respectively, implemented via either Single or Alternate Substitution — see [Text shaping](#text-shaping)), real glyph substitution is used. When the font lacks the feature, `small-caps`/`all-small-caps` fall back to synthesis (originally-lowercase letters upper-cased and drawn at a reduced size; under `all-small-caps`, already-uppercase letters are shrunk too, approximating `c2sc`); the other four keywords never synthesize — an unsupported font renders them as `normal` with no visual effect |
| `font-variant-ligatures` | [font-variant-ligatures](https://developer.mozilla.org/en-US/docs/Web/CSS/font-variant-ligatures) | `normal`, `none`, `common-ligatures`/`no-common-ligatures`, `discretionary-ligatures`/`no-discretionary-ligatures`/`historical-ligatures`/`no-historical-ligatures`, and `contextual`/`no-contextual` all actually control GSUB substitution (see [Text shaping](#text-shaping)) — `contextual` drives a font's `calt` feature via GSUB contextual/chaining-context lookups |
| `font-kerning` | [font-kerning](https://developer.mozilla.org/en-US/docs/Web/CSS/font-kerning) | `auto`, `normal`, and `none` are all supported: `auto`/`normal` apply a font's GPOS `kern` feature (pair/class kerning) when the font defines one; `none` disables it. Mark-to-base/mark-to-mark positioning (diacritic placement) is unaffected by this property — it's always applied when a font defines it, matching real browser behavior |
| `font-variant-numeric` | [font-variant-numeric](https://developer.mozilla.org/en-US/docs/Web/CSS/font-variant-numeric) | All 8 keywords (`lining-nums`, `oldstyle-nums`, `proportional-nums`, `tabular-nums`, `diagonal-fractions`, `stacked-fractions`, `ordinal`, `slashed-zero`) parse, cascade, and activate the corresponding OpenType `GSUB` feature (`lnum`/`onum`/`pnum`/`tnum`/`frac`/`afrc`/`ordn`/`zero`) on a font that has it; a keyword the resolved font doesn't support is a silent no-op (no synthesis for this property) |
| `font-variant-east-asian` | [font-variant-east-asian](https://developer.mozilla.org/en-US/docs/Web/CSS/font-variant-east-asian) | All 9 keywords (`jis78-forms`, `jis83-forms`, `jis90-forms`, `jis04-forms`, `simplified`, `traditional`, `full-width`, `proportional-width`, `ruby`) parse, cascade, and activate the corresponding OpenType `GSUB` feature (`jp78`/`jp83`/`jp90`/`jp04`/`smpl`/`trad`/`fwid`/`pwid`/`ruby`) on a font that has it; a keyword the resolved font doesn't support is a silent no-op |
| `font-variant-position` | [font-variant-position](https://developer.mozilla.org/en-US/docs/Web/CSS/font-variant-position) | `normal`, `sub` and `super` parse, cascade, and activate the corresponding OpenType `GSUB` feature (`subs`/`sups`) on a font that has it. When the resolved font lacks the feature, the run is **synthesized** instead, as the spec requires: the text is drawn with a reduced-size face and its baseline shifted, using the scale and offset the font's own `OS/2` table recommends (`ySuperscriptYSize`/`ySuperscriptYOffset` and the subscript pair), falling back to representative ratios only for a font that leaves those fields at zero. Real substitution and synthesis are never combined. The decision is per element, matching the spec's rule that a run whose glyphs aren't all available is synthesized as a whole. Synthesized sub/superscripts don't affect line height or any other box dimension — unlike [`vertical-align`](#css-property-support)'s `sub`/`super`, which shift the whole inline box within its line |
| `font-variant-alternates` | [font-variant-alternates](https://developer.mozilla.org/en-US/docs/Web/CSS/font-variant-alternates) | `normal`, `historical-forms` (activates a font's `hist` feature), and the six functional clauses `stylistic(<name>)`/`styleset(<name>#)`/`character-variant(<name>#)`/`swash(<name>)`/`ornaments(<name>)`/`annotation(<name>)`, combinable in one declaration (each may appear at most once). Each `<name>` is looked up in a matching [`@font-feature-values`](#css-at-rules) rule for the element's used font family: `stylistic()`/`swash()`/`ornaments()`/`annotation()` activate `salt`/`swsh`/`ornm`/`nalt` respectively, using the looked-up integer the same way `font-feature-settings` uses one (`1` the first alternate, `2` the second, and so on); `styleset()` turns on one or more numbered `ss01`–`ss20` features at once (one per integer the name's `@styleset` block declares); `character-variant()`'s name maps to a `cv01`–`cv99` feature (its first integer) with an optional second integer selecting the value passed to it (defaulting to `1`). A name absent from the registry (or a `@font-feature-values` rule that doesn't cover the used family) contributes nothing, the same "unknown resolves to no-op" behavior `font-palette` uses for an unmatched name. Takes precedence over a `font-feature-settings` entry for the same tag |
| `font-feature-settings` | [font-feature-settings](https://developer.mozilla.org/en-US/docs/Web/CSS/font-feature-settings) | `normal` and a comma-separated list of `<string> [<integer> \| on \| off]?` feature tags (e.g. `"smcp" 1, "ss01" on`) activate the named OpenType `GSUB` features directly on a font that has them — including a feature implemented via Single, Multiple, Alternate, Ligature, or Contextual/Chaining Context substitution. For a feature a font implements via Alternate Substitution (e.g. a numbered stylistic set), the integer selects which glyph alternate to use (`1` the first, `2` the second, and so on) rather than just turning the feature on or off. A tag already controlled by one of the `font-variant-*` longhands above is governed by that longhand instead, per spec precedence — `font-feature-settings` never overrides it |
| `font-weight` | [font-weight](https://developer.mozilla.org/en-US/docs/Web/CSS/font-weight) | Keyword (`bold`, `normal`) and numeric (`1`–`1000`) values. `bolder`/`lighter` step relative to the parent's own resolved weight per the CSS2.1 §15.6 worked table, not a fixed always-bold/always-normal result. Face selection uses real CSS Fonts Level 4 §5.2 nearest-weight matching (not just an exact Regular/Bold pick) among every face registered for a family; when no face close enough to the request exists, a faux-bold is synthesized (fill+stroke render mode) rather than rendering with no visual distinction |
| `font-palette` | [font-palette](https://developer.mozilla.org/en-US/docs/Web/CSS/font-palette) | Selects which CPAL palette a `COLR`/`CPAL` color font paints with (see [Per-character font matching](#per-character-font-matching-and-coverage-fallback)). Inherited. `normal` uses palette 0; `light`/`dark` select the first palette the font flags usable with a light/dark background (via the CPAL v1 palette-type flags, falling back to `normal` when the font has none); a `<dashed-ident>` names a custom palette defined with [`@font-palette-values`](#css-at-rules); and [`palette-mix()`](https://developer.mozilla.org/en-US/docs/Web/CSS/font-palette#palette-mix) blends two palettes per CPAL entry in a given color space. The palette is applied to the element's used font family; a non-color font (or one with a single palette) is unaffected. Only affects `COLR`/`CPAL`-over-`glyf` color fonts (the color formats PeachPDF renders as vectors). Animation/interpolation of `font-palette` is not supported (a static PDF has no timeline) |
| `line-height` | [line-height](https://developer.mozilla.org/en-US/docs/Web/CSS/line-height) | Full support. A line box is built the way [CSS 2.1 §10.8.1](https://www.w3.org/TR/CSS21/visudet.html#line-height) describes: each inline box on the line contributes its own font ascent and descent plus half the **leading** (its `line-height` less its font's own height) on each side, the line reaches as far above and below its baseline as the furthest of them — including the **strut**, an imaginary inline box carrying the block's own font and `line-height` — and every box on the line hangs its content from that one shared baseline. So a `line-height` larger than the font centres the text in the line rather than hanging it from the top, and text of two different sizes on one line sits on a common baseline rather than sharing a top edge. An **empty** inline element, such as an icon wrapper with no text or a `<span>` holding only an absolutely positioned badge, is an inline box on the line too, so a larger font on it makes the line taller. An empty inline right after a space goes to the next line along with the word that follows it, if that word wraps. A line holding nothing but empty inlines stays zero-height ([CSS 2.1 §9.4.2](https://www.w3.org/TR/CSS21/visuren.html#inline-formatting)). In a vertical writing mode an empty inline does not yet make its column thicker. Because the two sides are maximised independently, a line can be taller than the largest single `line-height` on it. Where a `line-height` is *shorter* than the font, the leading is negative and the glyphs overflow the line box on both sides, matching the spec exactly — fragmentainer (page/column) membership is decided per line box rather than per word, so ink escaping above a line box does not risk escaping the fragmentainer the line was placed in |
| `vertical-align` | [vertical-align](https://developer.mozilla.org/en-US/docs/Web/CSS/vertical-align) | `baseline`, `sub`, `super`, `top`, `middle`, `bottom`, `text-top`, `text-bottom`, and `<length>`/`<percentage>` (offsetting the box from its own baseline; a percentage resolves against the box's own `line-height`) for any inline-level box relative to its line box, not just table cells (which use a separate, table-specific alignment algorithm accepting only the keyword subset — see [Tables](#tables)). An **atomic inline** — an `<img>`, an inline `<svg>`, MathML, a form control, an `inline-block` — is baseline-aligned from its own box rather than from font metrics ([CSS 2.1 §10.8.1](https://www.w3.org/TR/CSS21/visudet.html#line-height)): its **bottom margin edge** rests on the line's baseline, so `text <img> text` sits the image's bottom on the text's baseline and the line grows above the baseline to hold it, rather than the image hanging from the line's top edge. An `inline-block` whose `overflow` is `visible` and which has in-flow line boxes of its own instead uses the baseline of its **last** such line, so its text and the text beside it line up; one that is empty, or whose `overflow` is not `visible`, falls back to the bottom margin edge. An inline box wrapping an atomic inline moves with it, so its background and border stay attached |

#### Per-character font matching and coverage fallback

Font matching is per-codepoint, following the [CSS Fonts 4 matching algorithm](https://developer.mozilla.org/en-US/docs/Web/CSS/@font-face/unicode-range): each character is resolved to the first family in the `font-family` list whose face both covers that character (via its `@font-face` [`unicode-range`](https://developer.mozilla.org/en-US/docs/Web/CSS/@font-face/unicode-range), or, for a system/rangeless font, its actual `cmap` coverage) and contains a glyph for it. A run whose primary font lacks a glyph for some character therefore falls back to the next declared family that covers it, and multiple `@font-face` rules sharing one `font-family` but declaring different `unicode-range` subsets each supply their own characters.

Supplementary-plane (astral) characters above U+FFFF — where nearly all emoji live — are supported: they resolve through the font's format-12 `cmap` subtable, both for glyph lookup and for coverage-based fallback, and render the font's monochrome `glyf`/`CFF` outline.

**Color** emoji is supported for **`COLR`/`CPAL`** fonts (both COLR version 0 and version 1), including the COLR&nbsp;v1 build of Noto Color Emoji. PeachPDF decodes each color glyph and draws its visible artwork as native PDF vector content: COLR&nbsp;v0 layers are filled with their `CPAL` palette colors bottom-to-top, and COLR&nbsp;v1 paint graphs are composed — solid fills, linear/radial/conic ([`sweep`](https://learn.microsoft.com/en-us/typography/opentype/spec/colr#format-8-paintsweepgradient)) gradients, per-paint and per-gradient-stop alpha, affine transforms, glyph clips, and separable/HSL blend-mode compositing. Color glyphs are also searchable and selectable: a subset of the color font is embedded solely for invisible PDF text, with a `/ToUnicode` map and per-occurrence `/ActualText` preserving the exact source sequence copied from each glyph (including variation selectors, ZWJ, modifiers, regional indicators, and tag characters). The embedded subset increases file size compared with vector-only color-glyph output; it never supplies visible ink. A color glyph's artwork itself is stored only once per document however often it is used — including across pages and across font sizes — so a document repeating an emoji pays for the artwork once and for a short placement instruction at each occurrence.

Multi-codepoint **emoji sequences** resolve to their composed glyph when the font defines one, via the same `GSUB` path as any other ligature (see [Text shaping](#text-shaping)) — ZWJ sequences (👩‍💻, 🏳️‍🌈), regional-indicator pairs (🇺🇸, 🇯🇵), tag sequences (🏴󠁧󠁢󠁳󠁣󠁴󠁿), and [skin tone modifiers](https://www.unicode.org/reports/tr51/#Emoji_Modifiers) (👍🏻 👍🏽 👍🏿, `U+1F3FB`–`U+1F3FF`), including a modifier and a ZWJ in the same sequence (👩🏽‍💻). Emoji assigned an ordinary ideographic, emoji-base, or paired regional-indicator line-break opportunity wrap between complete grapheme clusters without requiring `overflow-wrap`; the sequences listed above remain intact. Other sequences, including keycaps, retain their own Unicode line-break behavior rather than gaining a break merely because they are emoji. A trailing `U+FE0F` (VARIATION SELECTOR-16) in such a sequence neither draws nor contributes an independent advance, and does not prevent the ligature from forming.

**Bitmap** color emoji is supported too: the **`CBDT`/`CBLC`** tables (the classic bitmap build of Noto Color Emoji; PNG image formats 17, 18 and 19, every index-subtable format) and **`sbix`** (Apple Color Emoji; `png ` and `jpg ` pictures, and `dupe` redirects). Each glyph is drawn as the font's own picture, scaled by `font-size` over the strike's pixels-per-em and placed by the picture's bearings; the smallest strike at least as large as the text is used, so a picture is scaled down rather than up. A picture used many times is stored once per document. Text stays searchable and selectable in the same way as for `COLR` glyphs, and the pictures are drawn by the [raster backend](#rasterized-effects) too, so an emoji inside a filtered or flattened element is not lost. The pictures have an alpha channel, so a document targeting PDF/A-1 or PDF/X-1a/X-3 needs [transparency flattening](usage-examples.md#flattening-transparency-for-pdfa-1-and-pdfx) to use them. `tiff` and `mask` `sbix` pictures, and bitmap formats other than PNG in `CBDT` (formats 1, 2, 5, 6, 7, 8, 9), are not drawn.

Still unsupported (a color-emoji font using only these renders blank or monochrome): **SVG-in-OpenType** (`SVG ` table), and `COLR` glyphs in **CFF**-flavored fonts (the vector path decodes `glyf` outlines only). COLR&nbsp;v1 variable (`PaintVar*`) paints render at their default instance; Porter-Duff-only composite modes that PDF cannot express fall back to source-over; a radial gradient's nonzero *inner* radius and a linear gradient's `p2` rotation are approximated.

When no family in the declared `font-family` stack (plus the default font) covers a character, PeachPDF performs a last-resort system-fallback search across every OTHER font registered with the `PdfGenerator` instance — system-discovered fonts plus anything added via [`AddFontFromStream`](usage-examples.md) — the same final step the [CSS Fonts 4 font matching algorithm](https://www.w3.org/TR/css-fonts-4/#font-matching-algorithm) leaves to the user agent. When more than one registered font covers the character, the one whose own coverage best overlaps the character's Unicode script wins (so a font built for that script is preferred over one that merely happens to include an incidental glyph); a character with no script of its own (punctuation, digits) or a tie between equally-good candidates falls back to alphabetical family-name order for a reproducible result. A character nothing registered covers still shows a missing-glyph box.

Remaining boundaries: variation-selector sequences (`cmap` format 14) are not applied, and a font offering *only* a format-12 subtable (with no format-4 subtable) is not used.

### Text Layout

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `direction` | [direction](https://developer.mozilla.org/en-US/docs/Web/CSS/direction) | `ltr` and `rtl`. Drives a real [Unicode Bidirectional Algorithm (UAX #9)](https://www.unicode.org/reports/tr9/) implementation: mixed-direction text within a line is resolved per-character (not per whole word), digits and other neutral/weak runs are correctly embedded inside surrounding RTL text, and mirrored characters (parentheses, brackets, etc.) are substituted for their mirror glyph in an RTL run. See [Text shaping](#text-shaping). A block-level box narrower than its `rtl` containing block sits against the right edge, and an over-constrained one overflows toward the left, per [CSS 2.1 §10.3.3](https://www.w3.org/TR/CSS21/visudet.html#blockwidth) (horizontal writing modes only; a `<table>` narrower than its `rtl` container is not yet end-aligned) |
| `unicode-bidi` | [unicode-bidi](https://developer.mozilla.org/en-US/docs/Web/CSS/unicode-bidi) | `normal`, `embed`, `bidi-override`, `isolate`, `isolate-override` each push the corresponding explicit directional embedding/override/isolate onto the Unicode Bidi Algorithm's level stack (the same mechanism a raw Unicode LRE/RLE/LRO/RLO/LRI/RLI/FSI control character would), scoped to the element it's set on. `plaintext` re-derives its own base direction from the first strong character in its content (UAX #9 P2/P3), regardless of the computed `direction` property, the same first-strong-character detection HTML's `dir="auto"` performs |
| `writing-mode` | [writing-mode](https://developer.mozilla.org/en-US/docs/Web/CSS/writing-mode) | All five values parse, cascade, and inherit correctly, and correctly drive [logical box-model property](#logical-box-model-properties) resolution. `vertical-rl`/`vertical-lr` additionally get real vertical line flow for a block box holding plain inline content (text and simple nested inline elements): lines ("columns") stack along the block axis (right-to-left for `vertical-rl`, left-to-right for `vertical-lr`), text runs top-to-bottom within each column, glyphs orient per `text-orientation` (see below), and auto `width`/`height` both shrink to the content's own extent. A vertical box holding block-level children instead stacks each child along the same block axis, treating each already-laid-out child as one atomic unit — this is also what makes an **orthogonal-flow child** (one whose own `writing-mode` is perpendicular to its containing block's, per [CSS Writing Modes 4 §4.3](https://www.w3.org/TR/css-writing-modes-4/#orthogonal-flows)) work: it lays its own content out in its own writing mode, and the parent places the whole resulting box as one atomic unit along its own block axis. An auto-width, non-replaced orthogonal child is sized via real shrink-to-fit (`min(max-content, max(min-content, constraint))`, per §4.3) rather than stretching to fill the parent's available block-axis extent — a definite width, or content wide enough to need the full available extent anyway, is unaffected. A `direction: rtl` vertical box's block children correctly anchor to the physical bottom edge (its own inline-start) rather than the physical top `ltr` uses, with a shorter child's own unused space appearing as a gap at the top. Margins between block-axis-stacked children — and between a vertical box's own block-start/block-end edge and its first/last stacked child — really collapse per [CSS2.1 §8.3.1](https://www.w3.org/TR/CSS21/box.html#collapsing-margins) rather than being summed, stopping at a boundary where adjacent boxes' writing modes differ. [Flexbox](#flexbox) row/column axis selection, sizing, and page fragmentation are also writing-mode-aware. `display: table` is too, including `<thead>`/`<tfoot>`, `<caption>`, `border-collapse: collapse`, `rowspan`/`colspan`, and `vertical-align`: rows stack along the block axis and columns run top-to-bottom along the inline axis, using each cell's own `height` as its column-sizing hint; a `<caption>` stacks along the row axis and sizes across the full column axis; `<thead>`/`<tfoot>` are placed along the row axis, including a header/footer group with more than one row, which reverses its own rows' relative order under `vertical-rl` the same way `<tbody>` rows do; collapsed borders resolve on the correct logical edges and paint with the correct physical orientation; a `rowspan` cell's own row-axis extent grows to the combined extent of every row it spans (`colspan`'s own column-axis extent already worked, since a column's own width is an axis-agnostic concept); and `vertical-align` repositions a cell's content along the row axis rather than the column axis. A vertical box holding inline-only or block-level content is moved whole to the next page rather than fragmented mid-content, since it does not yet paginate its own content across pages under a vertical writing mode. A vertical `display: table` is different: a **plain grid** (no `rowspan`, `colspan`, `<caption>`, `<thead>`/`<tfoot>`, or `border-collapse: collapse`) whose own column-axis extent exceeds one page relocates the columns that don't fit onto later pages as whole units — real per-column pagination, reusing the page's own physical-Y fragmentation axis directly, since a vertical table's column axis genuinely *is* that axis. A table combining that with any of the five listed features is still moved whole, like any other vertical box. `sideways-rl`/`sideways-lr` still render as `horizontal-tb`. A [multi-column](#multi-column-layout) container has no real column arrangement under a vertical writing mode either — `column-count`/`column-width` are inert there, and it falls back to ordinary single-column block flow (which still paginates normally) rather than laying out columns along the wrong axis. A vertical box's own inline-only content now also supports `text-align` (`left`/`right` are always physical per CSS Writing Modes' line-left/line-right — direction-independent — while `start`/`end` resolve against `direction`; natural, pre-alignment placement already flushes to the physical bottom edge under `direction: rtl` rather than the physical top `ltr` uses, so which keyword is a no-op versus an active shift flips between the two), Unicode Bidi Algorithm reordering (mirroring the horizontal engine's own per-line reordering and mirroring, along the column's own inline axis instead of physical X), `hyphens: auto`/manual soft-hyphen breaks (splitting a word across a column boundary the same way horizontal flow splits across a line boundary), and `position: absolute`/`fixed` nested inside such content (already blockified per CSS2.1 §9.7 before it ever reaches this content, so it resolves fully via the same physical-offset mechanism a block-level positioned descendant already uses, reserving no column space). A **float** (`float: left`/`right`, also always physical) placed as a preceding sibling of such content now gets real column wrap-around too: a column whose own block-axis position overlaps the float's physical extent is narrowed to stop short of it, the same way a horizontal line narrows around a float. **Not yet supported under a vertical writing mode**: `box-decoration-break: clone`, real multi-column arrangement, a float's own starting position when interleaved between vertical text runs (it resolves via the ordinary top-down block-flow mechanism, which is not physically meaningful for a box stacked along the block axis instead), `clear` on an ordinary (non-floated) block-level child of a vertical box's own block content, and (for a table combining real per-column pagination with any of the five features above) real pagination together with that feature |
| `text-orientation` | [text-orientation](https://developer.mozilla.org/en-US/docs/Web/CSS/text-orientation) | `mixed`, `upright`, and `sideways` all parse, cascade, inherit, and are consumed by layout and painting under a vertical writing mode. `upright` forces every character upright; `sideways` rotates every character 90°; `mixed` (the default) classifies each character by Unicode's [Vertical_Orientation property](https://www.unicode.org/reports/tr50/) (UAX #50) and splits a run at each upright/rotated boundary, painting CJK ideographs, kana, and other `U`/`Tu`-classified characters upright while rotating everything else (Latin text, digits, etc.) — matching real vertical Japanese/Chinese/Korean typesetting, where CJK characters read upright down the column and embedded Latin/numeral runs rotate sideways. An upright character's down-the-column advance consults the font's own real OpenType vertical metrics (`vhea`/`vmtx`) when it carries them (mainly professional CJK vertical fonts), clipped to its own reserved cell so a glyph never bleeds into its neighbor's; a font without real vertical metrics — the common case — falls back to approximating the advance as the font's own line height. An upright character's anchor position additionally consults the font's real `VORG` vertical-origin table when it has one — CFF/CFF2 fonts only, per the OpenType spec's own restriction; a TrueType-outline font's `VORG` table (rare in practice) is never consulted, and such a font falls back to the plain top-of-cell anchor exactly as a font with no `VORG` at all does. A rotated (`sideways`) character always uses its natural horizontal advance rotated 90°, matching real browser behavior for sideways-oriented text |
| `hyphens` | [hyphens](https://developer.mozilla.org/en-US/docs/Web/CSS/hyphens) | `none`, `manual`, `auto` are parsed, cascaded, and inherited. `manual` and `auto` both honor an explicit soft hyphen (`&shy;`/U+00AD) as a line-break opportunity, rendering a literal `-` glyph only when that break is actually used. `auto` additionally performs real pattern-based automatic hyphenation (Liang's algorithm) for ~73 languages — see the note below the table for language coverage and exclusions |
| `hyphenate-character` | [hyphenate-character](https://developer.mozilla.org/en-US/docs/Web/CSS/hyphenate-character) ([CSS Text 4](https://www.w3.org/TR/css-text-4/#hyphenate-character)) | `auto` and an explicit `<string>` are both supported, for both automatic (`hyphens: auto`) and soft-hyphen breaks. `auto` always resolves to `-` (U+002D HYPHEN-MINUS) — this engine's pre-existing hyphen glyph — rather than a per-content-language typographic convention (the spec only recommends, not requires, the latter). An explicit empty string (`hyphenate-character: ""`) is honored: the line still breaks at the hyphenation point, with no visible glyph inserted |
| `hyphenate-limit-chars` | [hyphenate-limit-chars](https://developer.mozilla.org/en-US/docs/Web/CSS/hyphenate-limit-chars) ([CSS Text 4](https://www.w3.org/TR/css-text-4/#propdef-hyphenate-limit-chars)) | The full `[ auto \| <integer> ]{1,3}` grammar (word / before / after minimums, with the 2nd/3rd defaulting as spec'd) is supported and enforced on every candidate break, automatic or soft-hyphen. An `auto` component imposes no additional constraint of its own beyond `hyphens: auto`'s underlying pattern data, which already carries its own per-language minimums (see the note below the table). Minimums are counted in UTF-16 code units, not typographic character units — the spec's carve-out for nonspacing combining marks and intra-word punctuation not counting toward the minimum is not implemented |
| `hyphenate-limit-lines` | [hyphenate-limit-lines](https://developer.mozilla.org/en-US/docs/Web/CSS/hyphenate-limit-lines) ([CSS Text 4](https://www.w3.org/TR/css-text-4/#hyphenate-limit-lines)) | `no-limit` and a non-negative `<integer>` are both supported: once a block has produced that many consecutive lines ending in a hyphen, the next line is never hyphenated (the word wraps whole instead), resetting the count as soon as a line closes without one. The count carries across a page/column break, so a run of consecutive hyphenated lines that straddles the boundary is still capped rather than restarting |
| `hyphenate-limit-last` | [hyphenate-limit-last](https://developer.mozilla.org/en-US/docs/Web/CSS/hyphenate-limit-last) ([CSS Text 4](https://www.w3.org/TR/css-text-4/#hyphenate-limit-last-property)) | `none`, `always`, `column`, `page`, and `spread` are all supported: when the value forbids it, a hyphen that would otherwise end the last line before that kind of break is undone and the whole word moves to the fragmentainer the break resumes in instead — unless the hyphenated word was already alone on its line (a cheap proxy for "no narrower elsewhere would help"), in which case the hyphen is kept rather than deferred indefinitely with no benefit. This engine has no facing-page "spread" concept (no two-page layout), so `spread` behaves the same as `page` |
| `hyphenate-limit-zone` | [hyphenate-limit-zone](https://developer.mozilla.org/en-US/docs/Web/CSS/hyphenate-limit-zone) ([CSS Text 4](https://www.w3.org/TR/css-text-4/#hyphenate-limit-zone-property)) | `<length-percentage>` (percentages resolve against the line box's own length) is supported: a hyphenated break is only attempted when skipping it would leave more than this much space unfilled at the end of the line. The initial value `0` preserves this engine's pre-existing behavior of always preferring a hyphenated break over any unfilled space at all |
| `letter-spacing` | [letter-spacing](https://developer.mozilla.org/en-US/docs/Web/CSS/letter-spacing) | Full support, including negative values; spacing is added after every character including the last (realized via the PDF `Tc` character-spacing operator, which applies to every glyph shown), and one letter-spacing unit is folded into the following inter-word gap so adjacent words never collapse together. Per CSS Text Level 3 §7.2, spacing is not suppressed at the start/end of a *word* — only at the start/end of a *line*, which this engine does not special-case, leaving each line's own leading/trailing edge with a sub-pixel, visually negligible extra inset |
| `block-ellipsis` | [block-ellipsis](https://developer.mozilla.org/en-US/docs/Web/CSS/block-ellipsis) ([CSS Overflow 4](https://www.w3.org/TR/css-overflow-4/#block-ellipsis)) | `auto` (the default `…`), `none` (the block still stops at `line-clamp`'s own line limit, but with no marker appended at all), and an explicit `<string>` (including an empty one, `""` — same "no visible marker, still stops there" effect as `none`) are all supported, customizing the ellipsis `line-clamp` (below) generates. Meaningless without a `line-clamp` limit also set on the same box; does not honor a `::first-line` font override for the marker itself, same as `line-clamp`'s own generated ellipsis. Inherited, per spec — unlike `line-clamp` itself (below), setting a custom marker once on an ancestor applies it to every clamped descendant that doesn't redeclare it. This engine models it as its own standalone longhand rather than only as `line-clamp`'s two-value shorthand form (`line-clamp: 3 "…more"`), which is not accepted — set both properties separately instead |
| `line-clamp` | [line-clamp](https://developer.mozilla.org/en-US/docs/Web/CSS/line-clamp) | The single-value form, `none` or a positive `<integer>` limiting a block to that many visible lines. Unlike `text-overflow` below, this is a **layout-time** cutoff, independent of `overflow`: once a clamped block has produced that many lines, layout stops there — the block's own height reflects only the visible lines, and everything after them is never laid out at all, not merely hidden. A generated ellipsis is appended to the end of the last visible line whenever content was actually truncated (never when the block's content already fit within the limit), replacing only as many trailing words as it takes to make room for itself — the specified per-soft-wrap-opportunity granularity, not a character-level truncation the way `text-overflow: ellipsis` uses. Correctly limits line count and shrinks height under bidi/RTL horizontal text (the ellipsis's own placement within a mixed-direction line is not yet bidi-aware) and counts line boxes belonging directly to the clamped block's own formatting context; it does not clamp at all under a vertical `writing-mode`, does not accept the two-value `<integer> <block-ellipsis>` form (custom marker text/`auto`/`none`), and does not honor a `::first-line` font override for the ellipsis itself. The legacy `display: -webkit-box; -webkit-line-clamp: N; -webkit-box-orient: vertical` idiom is not recognized as an alias for this property |
| `tab-size` | [tab-size](https://developer.mozilla.org/en-US/docs/Web/CSS/tab-size) ([CSS Text 4](https://www.w3.org/TR/css-text-4/#tab-size-property)) | Both grammar forms are supported: a bare `<number>` (the default is `8`) is a multiple of the space character's own measured advance width, and a `<length>` is an absolute tab-stop width. Only meaningful where a tab character survives as literal content — `white-space: pre` and `pre-wrap` (`normal`/`nowrap`/`pre-line` already collapse a tab to an ordinary space before this property ever applies). Tab stops are measured from the true start of the tab's own rendered line, correctly accounting for a `pre-wrap` soft wrap and for content contributed by an earlier sibling inline element on the same line. Negative numbers and lengths, and percentages (not part of the grammar), are all rejected (the property keeps its previous value) |
| `text-align` | [text-align](https://developer.mozilla.org/en-US/docs/Web/CSS/text-align) | `left`, `right`, `center`, `justify`, `start`, `end`. Under `justify`, a line that **ends a paragraph** is aligned by `text-align-last` (below) instead of being stretched, per [CSS Text §6.1](https://www.w3.org/TR/css-text-3/#text-align-property) — that is the block's own last line *and* the last line before a forced break (`<br>`, or a preserved newline under `white-space: pre`/`pre-wrap`/`pre-line`), so a `<br>`-separated address block or verse stays ragged rather than being stretched line by line. A line that merely ends at a page or column boundary is neither, so it is justified like any other and the block resumes justified on the next page. A justified line's leftover width is distributed over its *justification opportunities* only ([CSS Text §6.4.5](https://www.w3.org/TR/css-text-3/#justify-algos)): word separators, plus the boundary between a CJK character and whatever sits next to it. Two adjacent inline boxes with no white space between them (`A<span>B</span>`, text beside an `<img>` or inline `<svg>`) stay contiguous, and a hyphen or other soft wrap opportunity inside a word is not an opportunity either. A line with no opportunity at all is *unexpandable text* (§6.4.3) and is aligned by `text-align-last` too (see that property's own row for the one case where PeachPDF follows the spec's letter rather than any browser); one whose content is already too long for its measure is start-aligned and spills past the end edge (§6.1) — the physical right edge under `direction: ltr`, the left under `rtl`. The spec's third kind of opportunity — between the letters of a *clustered* (South-East Asian) script — is not offered, since this engine does not break those scripts into clusters in the first place. `text-align` is a real shorthand over `text-align-all` and `text-align-last` (below), per [CSS Text §6.1](https://www.w3.org/TR/css-text-3/#text-align-property): a plain value (any of the six listed here) sets `text-align-all` and resets `text-align-last` to `auto`; `justify-all` sets both to `justify`; `match-parent` sets both to `match-parent` |
| `text-align-all` | [CSS Text §6.2](https://www.w3.org/TR/css-text-3/#text-align-all-property) | The longhand `text-align`'s shorthand actually sets: `left`, `right`, `center`, `justify`, `start`, `end`, `match-parent`. `match-parent` behaves as `inherit`, except that an inherited `start`/`end` is interpreted against the **parent's own** `direction` rather than this element's, recursing through a chain of `match-parent` ancestors; on the root element it computes to `start` |
| `text-align-last` | [text-align-last](https://developer.mozilla.org/en-US/docs/Web/CSS/text-align-last) | `auto`, `start`, `end`, `left`, `right`, `center`, `justify`, `match-parent` (same parent-direction resolution as `text-align-all` above, recursing through a chain of `match-parent` ancestors — degrading to `auto`'s own behavior below when the nearest ancestor that actually declared `text-align-last` left it `auto`), inherited. Governs every line that ends a paragraph — the block's last line and the last line before a forced break — plus any justified line with no justification opportunity to expand at ([CSS Text §6.3](https://www.w3.org/TR/css-text-3/#text-align-last-property) and §6.4.3). The initial `auto` defers to `text-align-all`, except under `text-align-all: justify`, where it is start instead: that is why an ordinary justified paragraph's closing line is ragged. `text-align-last: justify` stretches those lines like any other, though a line with no justification opportunity to expand at cannot be stretched: per §6.4.3's parenthetical ("If `text-align-last` is `justify`, then they must be aligned as for `center`"), PeachPDF centers it — a deliberate spec-literal choice that no browser engine (Chromium, Firefox, or Safari, all of which start-align here instead) follows. Applies to a vertical writing mode's columns along their own inline axis as well |
| `text-decoration` | [text-decoration](https://developer.mozilla.org/en-US/docs/Web/CSS/text-decoration) | Shorthand supported |
| `text-decoration-color` | [text-decoration-color](https://developer.mozilla.org/en-US/docs/Web/CSS/text-decoration-color) | Full support |
| `text-decoration-line` | [text-decoration-line](https://developer.mozilla.org/en-US/docs/Web/CSS/text-decoration-line) | `none`, `underline`, `overline`, `line-through`, and any space-separated combination of them (e.g. `underline overline` draws both lines). A decoration declared on a block-level box decorates that box's inline content — one line per line box, as wide as that line's content — rather than the box's full width, and it reaches the inline content of in-flow block-level descendants too; floated and absolutely positioned descendants are not decorated, and the decorating box's own padding and border are skipped while a descendant inline box's are not. An atomic inline — an image, an `inline-block`, an `inline-table` — is not decorated: the line breaks around its margin box and resumes after it (which box edge bounds that gap is left to the user agent by the spec; this engine uses the margin box, so an atomic inline's own margins are undecorated too). Under a vertical `writing-mode` (`vertical-rl`/`vertical-lr`), the line runs the correct physical axis — down the column, on the correct side of the glyphs — rather than across it; the atomic-inline break-around above is not yet extended to that axis, and neither is `text-decoration-skip-ink` below for an upright run (a rotated run's skip-ink is, per that property's own row) |
| `text-decoration-skip-ink` | [text-decoration-skip-ink](https://developer.mozilla.org/en-US/docs/Web/CSS/text-decoration-skip-ink) | `auto`, `none`, `all`. `auto` — the initial value — interrupts an underline or overline wherever it would cross a glyph's ink, leaving a gap either side that grows with the decoration thickness (up to a cap); `none` draws the line straight through. A `line-through` is never interrupted, per spec. The line breaks once per glyph, across everything that glyph puts in the line's path — so a letter whose ink the line meets in several places, such as the two sides of an `o` or the bowl of a `g`, gets one gap spanning the whole letter rather than one per stroke, and no fragment of line is left stranded inside it. The spec leaves this choice of shape to the user agent; this matches what browsers do. Under `auto`, CJK-script text (Han, Hiragana/Katakana, Hangul) is never interrupted regardless of where its ink falls — the spec calls this out explicitly as UA discretion a conforming engine should exercise; `all` has no such exception and always interrupts. Under a vertical `writing-mode`, skipping applies to a rotated (sideways) run — one ordinary horizontal glyph run reoriented as a whole, so its ink reduces to something this engine's existing band-sweep math can measure once mapped into that run's own pre-rotation orientation — but not yet to an upright run (stacked character-by-character down the column, with no such single horizontal layout to reduce to; see the vertical-writing-mode note on `text-decoration-line` above) |
| `text-decoration-style` | [text-decoration-style](https://developer.mozilla.org/en-US/docs/Web/CSS/text-decoration-style) | `solid`, `dashed`, `dotted`, `double`, `wavy`. `double` draws **two** strokes; each is a third of the resolved [`text-decoration-thickness`](https://developer.mozilla.org/en-US/docs/Web/CSS/text-decoration-thickness), with a gap the same size between them, so the pair's total span matches the declared thickness exactly — the same "sum of the two lines and the space between them" rule the equivalent [`border-style`](https://developer.mozilla.org/en-US/docs/Web/CSS/border-style) value follows. Below roughly one CSS pixel per third, each stroke is held at that minimum instead (so a very thin double rule reads as two clearly visible hairlines rather than fading into illegibility), and the pair's total then exceeds the declared thickness. The pair keeps the first stroke where a single one would sit and grows the second away from the text: downward for an underline and a line-through, upward for an overline, matching browsers. A double overline's upward growth is accounted for during layout — the line reserves the extra headroom it needs, so it renders correctly even flush against the top of a page, with no author-side margin required. `wavy` strokes a curved path rather than a dash pattern — its centerline is offset from a single stroke's position the same direction `double`'s second stroke grows, by about one resolved thickness; the exact wave shape (amplitude, wavelength) is left to the user agent by the [CSS Text Decoration Module Level 3](https://www.w3.org/TR/css-text-decor-3/#text-decoration-style-property) itself, not merely by this engine's own choice — browsers do not render an identical wave either. Not yet extended to a true vertical `writing-mode`: a `wavy` decoration there falls back to a plain solid line rather than a mispositioned wave. Decoration styles on SVG text follow the same rules as HTML **except** `double`, which SVG still paints as a single stroke |
| `text-decoration-thickness` | [text-decoration-thickness](https://developer.mozilla.org/en-US/docs/Web/CSS/text-decoration-thickness) | `auto`, `from-font`, and `<length>`/`<percentage>` (percentages resolve against the element's own font size). `auto` — the initial value — uses this engine's own fixed decoration-line thickness rather than deriving one from the font; `from-font` reads the resolved font's real OpenType `underlineThickness` metric (falling back to the same fixed thickness `auto` uses for a font that reports none). An automatically positioned underline keeps its top edge below the alphabetic baseline; the gap grows with thick strokes, so increasing `text-decoration-thickness` does not make the line expand upward through ordinary glyphs. The baseline it hangs from is the one the decorated line's own text is set on, so a line mixing font sizes — a small label beside a large figure, say — is underlined under all of it rather than at the height one of the sizes would give; text raised or lowered by [`vertical-align`](https://developer.mozilla.org/en-US/docs/Web/CSS/vertical-align), such as a superscript, does not move the line |
| `text-underline-offset` | [text-underline-offset](https://developer.mozilla.org/en-US/docs/Web/CSS/text-underline-offset) | `auto` and `<length>`/`<percentage>` (percentages resolve against the element's own font size — "1em"). `auto` — the initial value — is a zero offset: the underline sits exactly where `text-underline-position` places it. A positive value moves the underline further from the text (down, for the `auto`/`from-font`/`under` positions this engine implements); a negative value moves it closer. Only affects `underline`, never `overline`/`line-through`, per spec. Unlike a `double` overline's upward growth (see `text-decoration-style` below), an unusually large offset (or `under` on deep-descender text) is not yet reserved space by layout, so it can clip against a page's own bottom edge on a line flush against one |
| `text-underline-position` | [text-underline-position](https://developer.mozilla.org/en-US/docs/Web/CSS/text-underline-position) | Full grammar support: `auto \| [ from-font \| under ] \|\| [ left \| right ]`. `auto` — the initial value — is this engine's own existing automatic clearance below the alphabetic baseline. `from-font` reads the resolved font's real OpenType `underlinePosition` metric instead, used as-is even when it is exactly 0 (at the baseline) rather than falling back to `auto`'s clearance — 0 is itself a plausible authored metric, not a clear "no data" signal. `under` positions the underline below the line's own lowest descender rather than at a fixed baseline-relative clearance, so it does not cross descenders. `left`/`right` pin the underline to that literal physical edge instead of the writing mode's own default under/over mapping; if that pins the underline where an overline would otherwise draw, the overline moves to the opposite physical edge instead of overlapping it. `left`/`right`, and the from-font/under-vs-baseline distinction, are meaningful only under a vertical `writing-mode` — under `horizontal-tb` a `left`/`right` value parses but has no visible effect (there is no physical left/right edge distinct from the line's own inline extent there) |
| `text-indent` | [text-indent](https://developer.mozilla.org/en-US/docs/Web/CSS/text-indent) | Full support, including the `hanging` and `each-line` keywords (CSS Text 3) |
| `text-overflow` | [text-overflow](https://developer.mozilla.org/en-US/docs/Web/CSS/text-overflow) | `clip`, `ellipsis`. Applies per line box that overflows its container's content edge, in any writing mode/direction — not only a forced single-line (`white-space: nowrap`) box, and only when that container's own `overflow` is `hidden` (see the `overflow` row in Display & Layout below — `auto`/`scroll` don't establish a real clip in this non-interactive renderer either). The two-value edge syntax, a custom `<string>` replacement, and `fade()` are non-standard/experimental (see MDN) and not implemented. See `line-clamp` above for the related but distinct multi-line, layout-time truncation feature |
| `text-shadow` | [text-shadow](https://developer.mozilla.org/en-US/docs/Web/CSS/text-shadow) | Comma-separated list of shadows, each `<color>? <offset-x> <offset-y> <blur-radius>?`; the first-listed shadow paints on top, an omitted colour is the text's own `color`, and the property is inherited. Offsets and blur may use any `<length>` unit or a `calc()` expression; an unrecognised unit makes the declaration invalid. A shadow with no blur is the text drawn again at the offset, in vector. A blurred shadow is a real Gaussian blur (the blur radius is twice the standard deviation) rendered into a bitmap beneath the text, so the text itself stays vector and selectable; see [Rasterized effects](#rasterized-effects). Shadows are painted for horizontal text only, and not for text decorations (underline, line-through). |
| `text-transform` | [text-transform](https://developer.mozilla.org/en-US/docs/Web/CSS/text-transform) | `none`, `uppercase`, `lowercase`, `capitalize`. `full-width` converts ASCII characters, space, and Latin-1 currency/symbol characters to their fullwidth forms; halfwidth katakana, halfwidth Hangul jamo, and halfwidth symbol variants are not converted. `full-size-kana` is not supported |
| `overflow-wrap` / `word-wrap` | [overflow-wrap](https://developer.mozilla.org/en-US/docs/Web/CSS/overflow-wrap) | `normal`, `break-word`, `anywhere`; inherited. `word-wrap` is the legacy name alias and shares the same computed value. `break-word` and `anywhere` break an otherwise-unbreakable token at extended grapheme-cluster boundaries only when no ordinary wrapping or hyphenation opportunity fits the line, without inserting a hyphen. An ordinary opportunity before a token therefore takes priority over an emergency break inside it. They work in horizontal and vertical writing modes and have no effect when `white-space` forbids wrapping. As specified, `anywhere`'s emergency opportunities participate in min-content sizing (including table, flex, grid, and shrink-to-fit sizing), while `break-word`'s do not |
| `white-space` | [white-space](https://developer.mozilla.org/en-US/docs/Web/CSS/white-space) | `normal`, `nowrap`, `pre`, `pre-wrap`, `pre-line` |
| `word-break` | [word-break](https://developer.mozilla.org/en-US/docs/Web/CSS/word-break) | `normal`, `break-all`. `keep-all` is parsed and accepted but has no distinct effect — CJK text still breaks between characters the way `normal` does |
| `word-spacing` | [word-spacing](https://developer.mozilla.org/en-US/docs/Web/CSS/word-spacing) | Full support |

#### Text shaping

PeachPDF applies essentially all of [OpenType Layout](https://learn.microsoft.com/en-us/typography/opentype/spec/ttochap1) a font can exercise, including Arabic-family complex-script joining and the Universal Shaping Engine (USE) syllable reordering used by Devanagari, Bengali, Gujarati, and Tamil: `GSUB` single, multiple, alternate, ligature, contextual/chaining-context, and reverse-chaining single substitution (including a contextual/chaining-context rule whose own target is another contextual/chaining-context lookup, nested to an arbitrary depth); `GPOS` kerning, cursive attachment, and mark positioning; a real [Unicode Bidirectional Algorithm (UAX #9)](https://www.unicode.org/reports/tr9/); Arabic/Syriac-family joining-form resolution and shaping; and conjunct/reph formation with syllable-level glyph reordering for the four USE-shaped scripts above. Indic script reordering beyond those four (other USE-driven Brahmic scripts, e.g. Gurmukhi, Kannada, Malayalam, Oriya, Telugu) is not implemented. In practice:

- **Ligatures** are formed automatically from a font's `GSUB` `liga`/`clig` ("common ligatures") and `rlig` ("required ligatures") features — e.g. `f` + `f` becomes a font's `ff` ligature glyph when the font defines one, the same way a browser renders it, not only when the source text already contains a precomposed ligature codepoint (e.g. `ﬁ`, U+FB01). `font-variant-ligatures` controls this: `normal` (the initial value) and `common-ligatures` enable `liga`/`clig`, `none` and `no-common-ligatures` disable them, `discretionary-ligatures`/`historical-ligatures` (and their `no-*` forms) enable/disable a font's `dlig`/`hlig` features the same way, and `contextual`/`no-contextual` enable/disable a font's `calt` feature — but per spec `rlig` ("required ligatures") is never affected by this property, not even by `none`, since a font's required ligatures aren't a stylistic choice.
- **Glyph composition/decomposition and localized forms** (`ccmp`, `locl`) are applied to all text, for every script, without being requested — they are default-on features in the [OpenType feature registry](https://learn.microsoft.com/en-us/typography/opentype/spec/featuretags), not stylistic opt-ins, so a font that decomposes a precomposed letter into base + mark, or that keeps its emoji-sequence ligatures in `ccmp` rather than `liga`, is honored. This matters most for modern color-emoji fonts: Noto Color Emoji's entire `GSUB` feature list is a single `ccmp` record, and that is where its regional-indicator (country flag), tag-sequence, and ZWJ-sequence ligatures live.
- **Caps, numeric, East Asian, positional variants, and multiple substitution** (`font-variant-caps`, `font-variant-numeric`, `font-variant-east-asian`, `font-variant-position`, and arbitrary tags via `font-feature-settings`) apply a font's `GSUB` Single, Multiple, or Alternate Substitution features the same way — see the property table above for each one's specific OpenType tags and fallback behavior.
- **Contextual/chaining-context substitution** (GSUB lookup types 5/6, `calt` and any `font-feature-settings` tag a font routes through one) supports all three OpenType subtable formats (glyph-sequence, class-based, and coverage-based), including a font's `lookupFlag`/`GDEF` mark filtering — an intervening non-participating glyph (e.g. a diacritic) inside the matched backtrack/input/lookahead window is correctly skipped rather than breaking the match, the same way ligature substitution's own component matching already did.
- **Reverse-chaining context single substitution** (GSUB lookup type 8) is applied end-to-start, per spec, for the rare font that defines it.
- **Default-ignorable characters** — variation selectors (`U+FE00`–`U+FE0F`, `U+E0100`–`U+E01EF`), `ZWJ`/`ZWNJ`, the bidi controls, word joiner, the Hangul fillers, and the language tag characters (`U+E0000`–`U+E007F`) — draw nothing when the font has no glyph for them, rather than falling through to the font's missing-glyph box. That is what Unicode's [`Default_Ignorable_Code_Point`](https://www.unicode.org/reports/tr44/#Default_Ignorable_Code_Point) property means, and a font is expected not to cover them. They are still fully visible to shaping first, so a font that does map one (and ligates through it, as emoji ZWJ sequences do) behaves exactly as authored, and a ligature whose component list skips over one — e.g. `U+1F3F3 U+FE0F U+200D U+1F308` against a flag + ZWJ + rainbow rule — still forms.
- **Kerning, cursive attachment, and mark positioning** (`GPOS`) apply a font's pair/class kerning (`kern`, gated by [`font-kerning`](#color--typography)), cursive joining (`curs`, requested for Arabic-family joining text — see below), and mark-to-base/mark-to-ligature/mark-to-mark diacritic attachment (`mark`/`mkmk`, always applied when the font defines it — not a stylistic opt-out), including contextual/chained-contextual positioning (GPOS lookup types 7/8) nesting any of these.
- **Per-language feature selection**: GSUB feature selection uses the script's language-specific `LangSys` when the element's resolved language (its own `lang` attribute, or its nearest ancestor's, per the [HTML "language of a node" algorithm](https://html.spec.whatwg.org/multipage/dom.html#language)) maps to a recognized OpenType language tag, falling back to the script's default language system otherwise.
- **Arabic-family complex-script shaping** (Arabic, and other joining scripts sharing its state machine, e.g. Syriac) resolves each character's initial/medial/final/isolated joining form from its Unicode `Joining_Type`/`Joining_Group` (per [UAX #53](https://www.unicode.org/reports/tr53/) semantics) and requests a font's `init`/`medi`/`fina`/`fin2`/`fin3`/`med2`/`isol` GSUB features accordingly, ahead of the general ligature/contextual pass (so a font's own `rlig` ligatures — e.g. Arabic's lam-alef — see the already-joining-form-selected glyphs, matching real shaping engines' own staging) and a font's `curs` GPOS feature (cursive baseline attachment, for fonts whose joining relies on it rather than purely on positional substitution). Shaping itself always runs in true logical (source) order — matching real shaping engines — with only the resulting glyph list reversed for right-to-left display, since Arabic is a strong-RTL script and reads right-to-left regardless of the surrounding `direction`.
- **Universal Shaping Engine (USE) syllable reordering** (Devanagari, Bengali, Gujarati, Tamil) classifies each character's Unicode `Indic_Syllabic_Category`/`Indic_Positional_Category` into a syllable structure (consonant, conjunct-forming halant sequence, dependent vowel sign, vowel modifier — plus, for Bengali specifically, its own Consonant Placeholder and Syllable Modifier categories, neither reachable from any Devanagari/Gujarati/Tamil codepoint), applies a font's default glyph pre-processing (`locl`/`ccmp`/`nukt`/`akhn`), forms reph from a word-initial RA+virama sequence via the font's own `rphf` feature, applies the font's conjunct/half-form/subjoined-form features (`rkrf`/`abvf`/`blwf`/`half`/`pstf`/`vatu`/`cjct`), then reorders the resulting glyphs — repositioning a formed reph forward past the base consonant, and moving a pre-base dependent vowel sign (e.g. a vowel sign I) backward to immediately follow the base — before a final pass applies the font's own presentation features (`abvs`/`blws`/`haln`/`pres`/`psts`). Gujarati and Tamil need no script-specific classifier/grammar code beyond what Devanagari already exercises; other USE-driven Brahmic scripts (Gurmukhi, Kannada, Malayalam, Oriya, Telugu, and the rest of the family) are not yet supported.
- Arabic-family joining and Devanagari USE shaping (not yet Bengali/Gujarati/Tamil) also apply to SVG `<text>`/`<tspan>` content, resolved over each `<text>` element's own flattened character stream the same way — a maximal run of mutually-joining or same-syllable-participating characters (sharing one element, zero `letter-spacing`, and no explicit per-character `x`/`y`/`dx`/`dy`/`rotate` on any but the run's own first character — see [Text](supported-svg-features.md#text)) shapes and, for a right-to-left run, display-reverses as one atomic unit, matching HTML's own per-word treatment. Not supported for `<textPath>` (glyphs there are still isolated nominal forms, positioned individually along the path) or under a vertical `writing-mode` (Arabic and Devanagari are never vertically typeset in practice).
- **Bidirectional text** implements the full Unicode Bidirectional Algorithm: paragraph/embedding level resolution (including the [`dir="auto"`](#global-attributes) first-strong-character heuristic and `<bdi>`'s isolation), per-character reordering of mixed-direction runs within a line (not just whole-word reordering), correct placement of neutral/weak runs (digits, punctuation) against their strong-direction neighbors, and mirroring of paired characters (parentheses, brackets, etc.) in a right-to-left run. This applies uniformly to HTML text layout, inline and standalone SVG `<text>`/`<tspan>`, and `@page` margin-box content (headers/footers, counters, named strings).

SVG `<text>`/`<tspan>`/`<textPath>` request the same GSUB/GPOS features HTML text does — `font-variant-ligatures`/`-caps`/`-numeric`/`-east-asian`, `font-feature-settings`, `font-kerning`-gated `GPOS` kerning, and per-language `LangSys` selection (the bullet above, resolved from an element's own `lang`/`xml:lang` attribute or its nearest ancestor's — including an inline `<svg>`'s surrounding HTML document, when nothing in the SVG's own tree sets one) are all supported per-element (see [Text](supported-svg-features.md#text) for the full property list and this feature's own known simplifications, e.g. no small-caps synthesis fallback). `<text>`/`<tspan>` additionally get real Arabic-family joining and Devanagari USE reordering, per the bullets above.

#### `hyphens: auto` language coverage

`auto` performs real pattern-based automatic hyphenation only when the text's language is known: PeachPDF resolves each element's own language per the [HTML "language of a node" algorithm](https://html.spec.whatwg.org/multipage/dom.html#language) — its own `lang` attribute if set, else its nearest ancestor's, else `<html lang="...">`, else a calling application's `PdfGenerateConfig.DefaultLanguage` fallback for documents that declare no language at all. With no language available from any source, `auto` behaves like `manual` (no algorithmic hyphenation) rather than guessing. A declared language resolves to the closest available pattern set — the tag itself, then progressively shorter subtag prefixes (e.g. `de-AT` falls back to `de`'s default variant, `de-1996`) — and a language with no match anywhere in that chain silently falls back to the same no-op rather than erroring.

`auto` is also a no-op on a host with no Brotli decoder, because the pattern data is Brotli-compressed — a browser/WebAssembly host is the case that matters in practice. Text there lays out unhyphenated rather than the render failing.

Each language's pattern data carries its own minimum characters before/after a break (2/3 by default, TeX's own values, when a pattern file doesn't state otherwise); `hyphenate-limit-chars` layers on top of — not instead of — those, and an `auto` component in `hyphenate-limit-chars` leaves the pattern's own minimum as the only constraint on that side.

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
| `display` | [display](https://developer.mozilla.org/en-US/docs/Web/CSS/display) | `block`, `inline`, `inline-block`, `none`, `flex`, `inline-flex`, `grid`, `inline-grid`, `table`, `table-row`, `table-cell`, `table-header-group`, `table-footer-group`, `table-row-group`, `table-column`, `table-column-group`, `table-caption`, `list-item`, `contents` (see [`display: contents`](#display-contents) below) |
| `position` | [position](https://developer.mozilla.org/en-US/docs/Web/CSS/position) | `static`, `relative`, `absolute`, `fixed`. A fixed box's containing block is the **page area** ([CSS 2.1 §10.1](https://www.w3.org/TR/CSS21/visudet.html#containing-block-details): the viewport in continuous media, the page area in paged media), so `top`/`left` are measured from the page's content corner — `top: 0; left: 0` sits at the top-left of the content area, inside the `@page` margins, and `left: 0; right: 0` spans the full measure. Percentages resolve against that same area, per page: on a document whose pages have different margins or sizes, a fixed box follows each page's own area rather than holding one absolute position on the sheet. `sticky` is treated as `relative` with a zero offset, since there is no scroll to ever cross a sticky threshold against — it participates in normal flow and in stacking/z-index like a positioned box, but its `top`/`right`/`bottom`/`left` values (the scroll-threshold parameters, not a static offset) never shift it. `running(<custom-ident>)` ([css-gcpm-3](https://www.w3.org/TR/css-gcpm-3/#running-syntax)) removes the element from normal flow entirely, making it available to a page margin box via `content: element(<custom-ident>)` — see [Running elements](#running-elements-position-running--element) |
| `float` | [float](https://developer.mozilla.org/en-US/docs/Web/CSS/float) | `left`, `right`, `none`, `footnote` ([css-gcpm-3](https://www.w3.org/TR/css-gcpm-3/#footnotes)), `top`, `bottom`, `top-bottom`, `snap`, `inside`, `outside` ([css-page-floats](https://www.w3.org/TR/css-page-floats-3/)) — see [Page floats](#page-floats-float-topbottomtop-bottomsnapinsideoutside) for the last six. `footnote` removes an inline-level element from normal flow entirely and routes its content to the page's - or, with [`float-reference: column`](#footnotes-float-footnote), its own column's - footnote area, the same "remove from flow" idea `position: running()` uses for margin boxes; see [Footnotes](#footnotes-float-footnote). A `left`/`right`/`inside`/`outside` float shares one inline formatting context with the surrounding inline content of the block it is in regardless of source order — placed beside that content's line boxes, with text wrapping around it, whether the float **precedes or follows** the content on the same line ([CSS 2.1 §9.5](https://www.w3.org/TR/CSS21/visuren.html#floats), [§9.5.1 rule 6](https://www.w3.org/TR/CSS21/visuren.html#float-position)). A shrink-to-fit box holding both (a float, an auto-width `position: absolute` box, an auto table column) is sized for the text and the float side by side, as are two adjacent floats — which are themselves placed side by side correctly. A box that establishes its own formatting context and takes its height from content — a float, an `overflow` other than `visible`, an `inline-block`, an absolutely/fixed-positioned box, a table cell or caption, a flex or grid item — **contains** its floats: its height grows to cover any floating descendant hanging below its content ([CSS 2.1 §10.6.7](https://www.w3.org/TR/CSS21/visudet.html#root-height)), which is what makes the `overflow: hidden` containment idiom work. An ordinary block correctly does not, so a float still overhangs it. `display: flow-root`, css-display-3's dedicated way to ask for containment, is not implemented and computes as `block`. A `left`/`right`/`inside`/`outside` float whose own content is taller than fits the remaining page overflows that page rather than continuing its own content onto a later one — the page it starts on simply shows as much of it as fits, and everything else in the document around it still lays out and paginates normally either way |
| `float-reference` | [css-page-floats](https://www.w3.org/TR/css-page-floats-3/) | `inline` (initial), `column`, `region`, `page` - which fragmentation context a float is positioned relative to. Consumed today only by a `float: footnote` source, where `column` routes the note into the note area of the column its reference landed in rather than the page's (see [Footnotes](#footnotes-float-footnote)); `page` is the same page-level behavior as the default, and `inline`/`region` behave as `page` there - a footnote has no inline note area to fall back to, and PeachPDF implements no part of CSS Regions. A `top`/`bottom`/`top-bottom`/`snap` page float (see [Page floats](#page-floats-float-topbottomtop-bottomsnapinsideoutside)) does not consume this property either — it always resolves against the page, regardless of its own `float-reference` value, including `column`. MDN has no page for this property, so the specification is linked directly |
| `footnote-display` | [css-gcpm-3](https://www.w3.org/TR/css-gcpm-3/#footnote-display) | `block`, `inline`, `compact` — how a `float: footnote` body stacks in the note area; see [Footnotes](#footnotes-float-footnote) |
| `footnote-policy` | [css-gcpm-3](https://www.w3.org/TR/css-gcpm-3/#footnote-policy) | `auto`, `line`, `block` — forces a page break when a footnote's own note area can't be placed on the page its call landed on; see [Footnotes](#footnotes-float-footnote) |
| `clear` | [clear](https://developer.mozilla.org/en-US/docs/Web/CSS/clear) | `left`, `right`, `both`, `none` |
| `overflow` | [overflow](https://developer.mozilla.org/en-US/docs/Web/CSS/overflow) | Affects clipping regions; there is no interactive scrolling in PDF output. `hidden` clips descendant content to the padding edge, following any `border-radius` on the clipping box (rather than clipping to a rectangle regardless of rounding) — the curve's own radius is reduced by the border width the same way `background-clip: padding-box` is, above. As in [CSS Overflow 3](https://www.w3.org/TR/css-overflow-3/#overflow-properties), an out-of-flow positioned descendant is clipped only by `overflow: hidden` boxes on its containing block chain: an absolutely positioned descendant whose containing block (its nearest positioned ancestor) lies *outside* the `overflow: hidden` box escapes its clip, and a `position: fixed` descendant is not clipped by any `overflow` box unless one with a `transform` other than `none` (an identity one such as `translateZ(0)` or `scale(1)` included), `perspective`, `filter` or `backdrop-filter` forms its containing block. Give the `overflow: hidden` box `position: relative` to make it clip its absolutely positioned descendants. One exception: a positioned descendant that escapes the clip this way is still clipped when a stacking context that is not itself positioned (`opacity` below 1, `mix-blend-mode`, a flex or grid item with `z-index`) sits between it and the `overflow: hidden` box |
| `visibility` | [visibility](https://developer.mozilla.org/en-US/docs/Web/CSS/visibility) | `visible`, `hidden`, `collapse` — on a table row, row group, column, or column group, `collapse` removes it from the table's geometry entirely (the rows/columns after it shift in to fill the gap), distinct from `hidden`, which reserves the element's layout space and only omits painting it |
| `z-index` | [z-index](https://developer.mozilla.org/en-US/docs/Web/CSS/z-index) | Full support for positioned elements |

#### `display: contents`

An element with `display: contents` generates no box of its own: for box generation and layout it is
treated as if it had been replaced by its children and its `::before`/`::after`
([CSS Display 3 §2.5](https://www.w3.org/TR/css-display-3/#valdef-display-contents)). A flex or grid
container's items are therefore the children of a `contents` wrapper, a `<tr style="display: contents">`
hands its cells to the table around it, and a `<span style="display: contents">` inside a paragraph
disappears from its lines. Only the box tree is affected: selectors, inheritance and the element itself
still exist, so its children still inherit `color`, `font-size`, `direction` and the rest from it.

What follows from having no box:

- The element's own box-model and painting properties — `margin`, `padding`, `border`, `background`,
  `opacity`, `transform`, `position`, `float`, `overflow`, `width`/`height` — have nothing to apply to.
- A `::first-letter` or `::first-line` on the element has no effect, and `text-decoration` on it does not
  reach its content (decorations propagate through the box tree). A `::first-letter` on an ancestor block
  still reaches into its text.
- A `counter-reset`, `counter-increment` or `counter-set` on it has no effect
  ([CSS Lists 3 §4.5](https://www.w3.org/TR/css-lists-3/#nobox)), including the implicit `list-item`
  counters of a `<ol style="display: contents">`; its `<li>` children still count and get their markers, but
  a counter they increment stays in scope for the elements that follow the list (a second `contents` list
  continues the first one's numbering).
- `unicode-bidi` on it (so a `<bdi>`, `<bdo>` or `[dir]` element with `display: contents`) does not isolate or
  override its content, which stays in the surrounding paragraph — `unicode-bidi` acts on an inline box
  ([CSS Writing Modes 4](https://www.w3.org/TR/css-writing-modes-4/#unicode-bidi)). Its `direction` is still
  inherited by its children.
- In a table, cells whose rows are `contents` are wrapped in anonymous rows by the ordinary
  [table fixup](https://www.w3.org/TR/CSS22/tables.html#anonymous-boxes), so the cells of two consecutive
  `<tr style="display: contents">` share one row.

What it does *not* lose: the element is still in the document. Its `id` still resolves for `href="#id"`
links, named destinations, bookmarks and `target-counter()`/`target-text()` (at the place its content
begins); a link or `bookmark-level` on it works; its `string-set` is assigned where its content begins
([CSS Generated Content for Paged Media §1.1](https://www.w3.org/TR/css-gcpm-3/#string-set)); and with
[tagged PDF](#tagged-pdf-pdfua-support) its structure element is kept around its children's (`<ul style="display:
contents">` is still an `/L`, a `<section>` a `/Sect`). `<body style="display: contents">` still propagates
its background to the page canvas, and `display: contents` on the root element computes to `block`.

Elements whose rendering CSS does not fully control compute to `display: none` instead
([Appendix B](https://www.w3.org/TR/css-display-3/#unbox)): `<br>`, `<wbr>`, `<img>`, `<video>`, `<audio>`,
`<canvas>`, `<iframe>`, `<embed>`, `<object>`, `<meter>`, `<progress>`, `<input>`, `<select>`,
`<textarea>`, and inline `<svg>` and `<math>` themselves. `<button>`, `<details>`, `<fieldset>` and
`<legend>` just lose their box. A `::before`/`::after` with `display: contents` is laid out as `inline`.

**Known gaps:** `display: contents` on an element *inside* an inline `<svg>` or `<math>` (`<g>`, `<tspan>`,
`<use>`, `<mrow>`, …) has no effect, because those renderers do not read the CSS `display` property. A counter
incremented by a child of a `display: contents` list leaks into the elements after the list (above).

#### Atomic inline-level layout is approximated, not fully atomic

An `inline-block` whose content **fits on one line** has that content flowed through the
surrounding inline formatting context rather than being laid out as one opaque unit. The content
is correctly inset by its own border+padding (its label sits inside the padding box, and the line
reserves the full padding box height), and a non-`auto` `width` sizes it as declared
([CSS 2.1 §10.3.9](https://www.w3.org/TR/CSS22/visudet.html#inlineblock-width) uses shrink-to-fit
only for `width: auto`): the box's background, border and overflow clip are painted at that width
whether or not its content fills it — an empty one included, which is what makes the fixed-width
label column and the empty-bordered-box checkbox glyph work — and the line reserves the same
width, honoring `box-sizing`. Percentage widths resolve against the containing block's content
width, just as they do on other boxes.

A non-`auto` `height`/`min-height` — including a percentage, resolved against the containing
block's own height when that is itself definite
([CSS 2.1 §10.5](https://www.w3.org/TR/CSS22/visudet.html#the-height-property)) — sizes the box's
painted background, border and overflow clip the same way
([CSS 2.1 §10.6.3](https://www.w3.org/TR/CSS22/visudet.html#normal-block)): the box paints at the
declared (or floored-by-`min-height`) height whether or not its content fills it, and the line
itself reserves that same height
([CSS 2.1 §10.8](https://www.w3.org/TR/CSS21/visudet.html#line-height)). The extra height grows
from whichever edge the box's `vertical-align` anchors — the box's own top for `top` and the
default `baseline` on a box with real content, its own bottom for `bottom`/`text-bottom`, split
evenly around its own center for `middle` — except the one case noted in the gap below. A declared
height smaller than the content's own natural height is not shrunk to — the painted box always
keeps at least its natural height, the same "never pulled back" rule a wider-than-declared `width`
already gets.

A box whose content **does not** fit on one line inside it — because it declares a `width`
narrower than its content, or because its content is wider than the containing block — is instead
laid out as a genuine atomic box: it establishes its own formatting context, breaks its lines at
its own measure, and takes one place on the line that holds it, with its last line's baseline on
that line's baseline. So a narrow `inline-block` holding a paragraph comes out as a narrow column
of wrapped text, the way a browser lays it out, rather than as text broken at the width of the
block around it. Content that genuinely cannot be broken — a single long word — still overflows
the box rather than widening it. A box holding block-level content (a `<div>` inside an
`inline-block`) has always been laid out this way.

Two knock-on gaps remain:

- An `inline-block` laid out as an independent formatting context (because it holds block-level
  content or wraps its own inline content) moves whole to the next line when its margin box does not
  fit, and a declared content-box `width` correctly grows by its padding and border. `inline-table`,
  `inline-grid`, and `inline-flex` get the same treatment: each preflights an estimated used width
  (a declared, non-percentage `width` as-is, or otherwise its own max-content width bounded by the
  containing block) against what is left of the line and, if it does not fit, closes the line for it,
  the same way an ordinary word wraps ([CSS 2.1 §9.4.2](https://www.w3.org/TR/CSS21/visuren.html#inline-formatting)).
  The approximated one-line path described above is the one shape this does not yet cover: it can
  still discover that the box's declared width does not fit only after flowing some of its content,
  rather than moving the opaque box as a unit.
- A declared `height`/`min-height` taller than the box's natural content grows correctly from
  whichever edge `vertical-align` anchors, with one narrower exception: the default
  `vertical-align: baseline` on an **empty box, or one whose `overflow` isn't `visible`** — both have
  no baseline of their own, so the engine falls back to treating the box's bottom margin edge as its
  baseline, computed from the box's still-natural (pre-growth) rectangle before the extra height is
  applied. That case still only grows downward from its own top, which can leave the box positioned
  too low relative to the rest of the line.

### Stacking Context

Paint order follows the CSS [stacking context](https://developer.mozilla.org/en-US/docs/Web/CSS/Guides/Positioned_layout/Stacking_context) model. A new stacking context is established by:

- the document root
- `position: relative` or `absolute` with a `z-index` other than `auto`
- `position: fixed` or `sticky` (unconditionally, regardless of `z-index`)
- a flex item (a direct child of a `display: flex`/`inline-flex` container) with a `z-index` other than `auto`
- `opacity` less than 1
- a `transform` other than `none` — including one whose matrix is the identity, such as `rotate(0deg)`, `scale(1)` or `translateZ(0)`
- a `perspective` other than `none`
- `mix-blend-mode` other than `normal`
- `filter` other than `none`

Elements are painted as one self-contained, atomic unit once they establish a stacking context — for example, everything inside an `opacity: 0.5` box (including any absolutely-positioned descendants) fades together as a single composited group, and a `z-index`-ordered element's own descendants are ordered independently of the rest of the page.

Within one stacking context, normal-flow content paints in CSS2.1 Appendix E order: in-flow block-level descendants first, then non-positioned floats, then in-flow inline-level descendants (text and inline replaced content, e.g. an inline `<img>`/`<object>`), then positioned descendants. A plain block-level box whose entire content is inline (a wrapper `<div>` around nothing but an inline image, for example) is treated as belonging to the inline pass itself, since painting it is what paints that inline content. This local ordering is preserved when a float is hoisted past its immediate container as long as that container is itself positioned (`position: relative`/`absolute`/`fixed`/`sticky`), even without an explicit `z-index` of its own. The one remaining gap: a float whose immediate container is a plain, non-positioned wrapper (no `position` at all) still hoists all the way to the nearest true stacking-context ancestor instead of preserving local order, so it may not paint correctly relative to non-hoisted block/inline siblings at its original nesting level.

The following triggers from the CSS specification are **not** supported, since the underlying properties themselves have no effect in PeachPDF (see [Unsupported CSS Features](#unsupported-css-features)): `isolation`, `will-change`, `contain`.

An element that needs to escape a plain (non-stacking-context) ancestor to compete for z-order at its true enclosing stacking context — for example, a `z-index`-ordered element nested inside a plain `position: absolute` wrapper that has no `z-index` of its own — is still correctly clipped by every `overflow: hidden` ancestor on its containing block chain that it passes through along the way, including when multiple such ancestors are nested.

### Flexbox

CSS Flexbox Level 1 (`display: flex` / `inline-flex`) is supported, including multi-line wrapping, all alignment properties, and auto margins on both axes. Replaced elements (`<img>`, inline `<svg>`) work as flex items, including when mixed with block-level siblings. An `inline-flex` container is an atomic inline: its items belong to the flex formatting context inside it and stay there, whether they are inline-level or block-level, and the box as a whole takes its place on the line it sits on.

**Which children become items** follows [CSS Flexbox §4](https://developer.mozilla.org/en-US/docs/Web/CSS/CSS_flexible_box_layout/Basic_concepts_of_flexbox): every in-flow child is an item in its own right, whatever its `display` — an inline element, a floated child (whose `float` is ignored in flex/grid layout), a `::before`/`::after` pseudo-element (including one whose `content` is the empty string, a common spacer idiom sized purely by its own `width`/`height`), and a table-internal `display` such as `table-row`, which is blockified rather than treated as part of a table. Only a contiguous run of child *text* is wrapped in one anonymous item. CSS 2.1 §9.2.1.1's anonymous *block* box is a block-container rule and is not applied inside a flex container, so a flex container mixing inline-level and block-level children keeps each of them as its own item. The same applies to grid containers.

Every item is laid out, aligned and painted as a block, so its own `width`/`height` apply whatever its `display` says. The one thing that does not follow is the **computed value** itself: an inline-level item's `display` is reported as declared rather than blockified. This is not observable in layout.

Flexbox is writing-mode-aware: `flex-direction: row`/`row-reverse` always mean "main axis = the container's own inline axis" and `column`/`column-reverse` mean "main axis = the block axis" (CSS Flexbox 1 §3), so under `vertical-rl`/`vertical-lr` a `row` container's items stack top-to-bottom (physical Y) and a `column` container's items stack along the block axis (right-to-left for `vertical-rl`, left-to-right for `vertical-lr`) — the same reversal [logical box-model properties](#logical-box-model-properties) already apply. `justify-content: left`/`right` fall back to `start` when the main axis isn't parallel to the physical left/right axis, per spec. `row-gap`/`column-gap` selection is unaffected by writing-mode (they map to `flex-direction`'s own row/column identity, not the physical axis it lands on). A container's own definite-main-size resolution stays scoped to the physical Y dimension's `aspect-ratio` fallback (no equivalent width-driven fallback for a vertical-writing-mode `column` container, where main lands on physical X) — a narrow, deliberate boundary, not an oversight.

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `flex-direction` | [flex-direction](https://developer.mozilla.org/en-US/docs/Web/CSS/flex-direction) | `row`, `row-reverse`, `column`, `column-reverse` |
| `flex-wrap` | [flex-wrap](https://developer.mozilla.org/en-US/docs/Web/CSS/flex-wrap) | `nowrap`, `wrap`, `wrap-reverse`. `wrap-reverse` swaps the cross-start and cross-end edges, so the lines are stacked in the opposite direction — each still occupying its own cross size, in sequence, with whatever `align-content` puts between them. Lines of different cross size therefore stack without overlapping, and `align-content` reads in the reversed direction too: `flex-end` on a row container packs the lines against its *top* edge, and lines that do not fit overflow that edge |
| `flex-flow` | [flex-flow](https://developer.mozilla.org/en-US/docs/Web/CSS/flex-flow) | Shorthand for `flex-direction` + `flex-wrap`, in either order (`wrap row` as well as `row wrap`) |
| `justify-content` | [justify-content](https://developer.mozilla.org/en-US/docs/Web/CSS/justify-content) | `normal`, `flex-start`, `flex-end`, `start`, `end`, `left`, `right`, `center`, `space-between`, `space-around`, `space-evenly`, `stretch`. `start`/`end` are flow-relative and flip with `flex-direction: row-reverse`/`column-reverse` like `flex-start`/`flex-end`; `left`/`right` are physical keywords that do **not** flip with `row-reverse`, and fall back to `start` when the main axis isn't horizontal (`flex-direction: column`/`column-reverse`) — both per CSS Box Alignment 3 §8.3. `stretch` has no main-axis growth effect (that is `flex-grow`'s job) and packs at the start |
| `align-items` | [align-items](https://developer.mozilla.org/en-US/docs/Web/CSS/align-items) | `flex-start`, `flex-end`, `start`, `end`, `self-start`, `self-end`, `center`, `stretch`, `normal`, `baseline`. Applies in **both** directions: a non-`stretch` alignment shrink-wraps each item to its fit-content cross size and positions it (a row item vertically, a column item horizontally), while `stretch` (the default) fills the line's cross size. `baseline` aligns items by their first text baseline and is only meaningful for row-direction flex — column-direction flex falls back to `flex-start`. `flex-wrap: wrap-reverse` swaps the cross-start and cross-end edges for the items *within* a line as well as for the stack of lines, so `flex-start` puts a short item against the bottom of its row line and `flex-end` against its top, and a baseline-aligned group is flush with the line's bottom; `center` reads the same either way, and an item that stretches fills its line and is on both edges at once |
| `align-content` | [align-content](https://developer.mozilla.org/en-US/docs/Web/CSS/align-content) | `flex-start`, `flex-end`, `start`, `end`, `center`, `space-between`, `space-around`, `space-evenly`, `stretch`, `baseline`. The initial value `normal` behaves as `stretch`, growing every line equally to absorb the container's free cross space; `baseline`'s content-distribution fallback alignment is `start` |
| `align-self` | [align-self](https://developer.mozilla.org/en-US/docs/Web/CSS/align-self) | Same values as `align-items`, plus `auto` |
| `order` | [order](https://developer.mozilla.org/en-US/docs/Web/CSS/order) | Full support |
| `flex-grow` | [flex-grow](https://developer.mozilla.org/en-US/docs/Web/CSS/flex-grow) | Full support |
| `flex-shrink` | [flex-shrink](https://developer.mozilla.org/en-US/docs/Web/CSS/flex-shrink) | Full support |
| `flex-basis` | [flex-basis](https://developer.mozilla.org/en-US/docs/Web/CSS/flex-basis) | Length, percentage, `auto`, and `content` values are supported |
| `flex` | [flex](https://developer.mozilla.org/en-US/docs/Web/CSS/flex) | Shorthand for `flex-grow`, `flex-shrink`, `flex-basis`, including the `none` and `auto` keywords |
| `gap` / `row-gap` / `column-gap` | [gap](https://developer.mozilla.org/en-US/docs/Web/CSS/gap) | Full support on flex containers |
| `margin` auto values | [margin](https://developer.mozilla.org/en-US/docs/Web/CSS/margin) | `auto` on a main-axis margin absorbs free space on that line (e.g. `margin-left: auto` to push a flex item to the end); `auto` on a cross-axis margin absorbs the line's free cross space (e.g. `margin-left: auto` on a `column` item, or `margin-top: auto` on a `row` item), overrides `align-self`, and stops the item stretching |

A container whose `height` is a percentage that cannot resolve — because its own containing block has no definite height — is treated as auto-height, per [CSS Box Sizing 4 §5](https://www.w3.org/TR/css-sizing-3/#definite). A `column` container then sizes to its content and stacks its items normally; there is simply no free space for `justify-content` to distribute.

### Grid

CSS Grid Layout (`display: grid` / `inline-grid`) is supported, including explicit and implicit track sizing, line-based placement with spans, named grid lines and `grid-template-areas`, auto-placement, box/content alignment (including block-axis `baseline` item alignment), and **subgrid** ([CSS Grid Level 2 §9](https://developer.mozilla.org/en-US/docs/Web/CSS/CSS_grid_layout/Subgrid)). Track sizes may be lengths, percentages, `fr` flexible lengths, `auto`, `min-content`/`max-content`, `minmax()`, `fit-content()`, `repeat()` — including `repeat(auto-fill, …)` / `repeat(auto-fit, …)`, so the common responsive-card idiom `repeat(auto-fill, minmax(200px, 1fr))` works — and a `calc()` (or `min()`/`max()`/`clamp()`) length, both on its own and inside `minmax()`/`fit-content()`/`repeat()`. `fr` tracks resolve via the standard flex-fraction algorithm (honoring `minmax()` floors); intrinsic tracks size to their items' content, distinguishing `min-content` (the longest unbreakable word) from `max-content` (the full unwrapped line), and an item spanning several intrinsic tracks distributes its content contribution across them so a spanned track does not collapse to zero. Each track then grows toward its growth limit with the space the container actually has ([§12.6](https://www.w3.org/TR/css-grid-1/#algo-grow-tracks)), so a grid narrower than its own content shares what there is rather than overflowing itself; whatever is still free afterwards goes to the `fr` tracks, or, with none, stretches the `auto` ones.

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
| `justify-items` / `align-items` | [justify-items](https://developer.mozilla.org/en-US/docs/Web/CSS/justify-items) | Default alignment of items within their cell: `start`, `end`, `self-start`, `self-end`, `center`, `stretch` (default), `normal`, and — on `justify-items` only, since they're physical keywords valid only on the inline axis — `left`/`right`. `align-items: baseline` (block axis) aligns the items in a row on a shared first text baseline, growing the row to the baseline group's max-ascent + max-descent (CSS Box Alignment 3 §9.3); `justify-items: baseline` (inline axis) falls back to `start` (baseline is undefined on the inline axis without a vertical writing mode) |
| `justify-self` / `align-self` | [justify-self](https://developer.mozilla.org/en-US/docs/Web/CSS/justify-self) | A grid item's own cell alignment; `auto`/`normal` inherit the container's `*-items` value. `justify-self` also accepts the inline-axis-only physical keywords `left`/`right` |
| `justify-content` / `align-content` | [justify-content](https://developer.mozilla.org/en-US/docs/Web/CSS/justify-content) | Distributes leftover container space among the tracks: `start`, `end`, `center`, `space-between`, `space-around`, `space-evenly`, `stretch` (which stretches `auto` columns/rows to fill the container when nothing else does) — plus, on `justify-content` only, the inline-axis-only physical keywords `left`/`right` |
| `place-items` / `place-content` / `place-self` | [place-items](https://developer.mozilla.org/en-US/docs/Web/CSS/place-items) | Shorthands for the align/justify pair (one value applies to both axes) |
| `gap` / `row-gap` / `column-gap` | [gap](https://developer.mozilla.org/en-US/docs/Web/CSS/gap) | Full support on grid containers (the same property family as flexbox's `gap`) |

The grid engine is fixed to a left-to-right, top-to-bottom writing mode — `direction: rtl` and vertical writing modes are not honored for track order or logical placement. The following are also **not** supported: named lines *inside* `repeat()` and `span <name>` (and a named line declared *after* an `auto-fill`/`auto-fit` repeat is positioned as if the repeat expanded to a single track); masonry; and inline-axis (`justify-*`) baseline alignment (meaningless without a vertical writing mode). Block-axis `baseline` item alignment shifts items to a shared baseline and grows the row to the baseline group's max-ascent + max-descent (CSS Box Alignment 3 §9.3), so a group with a disproportionately large descent no longer overflows the row. `auto`/`fr` row tracks do not stretch to fill a definite container height, and a `%`-bearing `calc()` track resolves its percentage against 0 in a row or `auto-fill`-count context (as a plain percentage does there). `subgrid` *is* supported (see the row above) with the noted limitations.

### Multi-column Layout

CSS Multi-column Layout (`column-count`/`column-width`/`columns`) is supported, and a column is a real fragmentainer in the sense [CSS Fragmentation Level 3 §2](https://www.w3.org/TR/css-break-3/#fragmentainer) means it — "a column in multi-column layout, or a page in paged media". The column a piece of content is in is what its break decisions are made against, so the break machinery works inside a column without being told about columns:

- `break-before: column` / `break-after: column` force a break at the column boundary, so the content after them starts the next column and the one before them is left short.
- `break-inside: avoid` (and `avoid-column`) keeps a box off a column boundary, moving it whole to the next column.
- `break-after: avoid` / `break-before: avoid` (and `avoid-column`) keep a heading with the content it introduces at a column boundary, so a heading is not left at the foot of the column the content it belongs to has moved out of.
- `break-before: page` (and the four directional values) forces a break to the next **page**, not the next column: none of a container's columns is on another page.
- [Monolithic content](#monolithic-content) — a replaced element or a scroll container — moves whole to the next column rather than being cut by the boundary.
- `orphans` and [margin truncation](#page-breaks) apply at a column boundary the same way they apply at a page boundary (`widows`, which is enforced after the fact by re-running the pass that filled a fragmentainer, is a page-context rule only).
- A container whose content outlasts its columns continues on the next page, resuming where its last column stopped rather than starting over, and balances the fragment that holds the end of the flow.
- A child continues **into** the next column rather than moving to it whole: a paragraph's first lines stay in one column and the rest flow into the next, each fragment carrying its own background, border and padding.
- The break need not fall between two of the container's own children. A block nested any distance below one is split at the column boundary too, and the content after the break starts the next column flush at that column's top, inside the continuing block's own fragment there. The continuing block's top border and padding are re-opened above it only under `box-decoration-break: clone`; under `slice` the box is cut at the break and nothing is inserted there — and this holds however deep the nesting goes, so a continuing block's own fragment begins where its content does. The multi-column container's own border and padding are not re-opened at a column boundary, since a container is not fragmented by its own columns. See [Decorations at a break](#decorations-at-a-break).

`column-fill: balance` (the default) aims for equal-height columns, and `column-fill: auto` fills each column before starting the next.

<a id="column-breaks"></a>
**Column breaks.** The break values that name the column fragmentation context — `column` on `break-before`/`break-after`, and `avoid-column` on `break-inside` — apply inside a multi-column container and nowhere else, which is what [§3.1](https://www.w3.org/TR/css-break-3/#break-between) means by naming a context. Outside one there is no column boundary for them to speak about, and they are ignored rather than being treated as their page-context equivalents.

Each value covers exactly the context it names, and `avoid` covers both:

| Value | At a page boundary | At a column boundary |
|-------|--------------------|----------------------|
| `break-before: page` | forces a break | forces one too — a column cannot span pages |
| `break-before: column` | ignored | forces a break |
| `break-inside: avoid` | keeps the box whole | keeps the box whole |
| `break-inside: avoid-page` | keeps the box whole | ignored |
| `break-inside: avoid-column` | ignored | keeps the box whole |

A forced column break carries the same [propagation](#page-breaks) rule a forced page break does: declared on a container's first in-flow child it is the break point before the *container*, so the container starts the next column along with the element. And an `avoid` that cannot be satisfied is relaxed rather than obeyed pointlessly — a box too tall for a whole column is split, since moving it to the next one would leave it splitting there instead.

<a id="keep-with-next-at-a-column-boundary"></a>
**Keep-with-next at a column boundary.** The [keep-with-next](#page-breaks) chain works at a column boundary as it does at a page one, with `avoid` and `avoid-column` naming the column context (and `avoid-page` naming only the page). Since the UA default stylesheet applies `h1-h6 { break-after: avoid }`, a heading inside a multi-column container is kept with the content below it out of the box: where that content starts the next column, the heading starts it too. §4.3's relaxation applies as it does on the page grid — a run too tall to fit the destination column alongside the content it is chained to is trimmed from its *front*, so the subheading immediately above travels even where the chapter heading above that cannot.

Two details follow from a column having no lower coordinate to move a run *to*. The break is stated before the head of the run and the next column lays the whole run out there, rather than the members being shifted; and the head has to be a box the column being filled actually placed — a run reaching back into a column that is already filled, or one whose head is the very box this column began with, is left where it is, since starting the next column at the same place would put the identical question to it again.

The chain applies where the content is moved *whole* by the column boundary. A box that asks not to be broken by one (`break-inside: avoid-column`) moves alone: it has begun splitting, so its full height is not yet known, and §4.3 gives a constraint up rather than acting on one it cannot show to be satisfiable — moving a heading into a column of its own while the content it introduces breaks again in the next is worse than leaving it. `column-fill: balance` also rarely leaves room for the pull, since a balanced column's height is derived from the content it holds; a page-bounded column (`column-fill: auto`, or a container taller than its page) is where a heading and its content travel together.

**A forced page break escapes the container.** `break-before: page` (and `left`/`right`/`recto`/`verso`) inside a multi-column container starts the next *page*: it names the page grid, and none of the container's columns is on another page. The columns it has opened so far end there, the content after the break opens the page the break names, and the container continues in its columns on that page. A directional value reaches the side it asks for here, exactly as it does outside a container ([Directional page breaks](#directional-page-breaks)) — but the page it skips to get there is **not** materialized: outside a container that skipped page is emitted as a real blank page, and inside one it is dropped, so the document is a page shorter than the same break would produce elsewhere. `column-fill: balance` balances the columns *before* the break over the content that precedes it, since the break is where this fragment's flow ends.

The above holds regardless of how deep inside the container the forced break is declared — on the container's own child or on a descendant further down a wrapper's subtree.

**A column is at least as tall as its unbreakable content.** A child a multi-column container cannot fragment — a `<table>`, or another layout engine's container — is placed in one column whole, and where it is taller than the height balancing chose, the column it lands in grows to hold it rather than the overflow being clipped away. A `<table>` nested in a multi-column container therefore renders in full.


A multi-column container whose content is only text, with no block-level child of its own, is not columnized at all — its content needs a block box to be moved between columns as.

A **floated** child of a multi-column container is not laid out at all — it is excluded from the set of boxes distributed into columns and, unlike an absolutely/fixed-positioned child, nothing positions it afterward either. A container whose children are only floats therefore renders with no visible content and no real height from them.

- A box split at a column boundary leaves that edge open, per `box-decoration-break: slice` — no border is drawn where the break is, only where the box really starts and ends. See [Decorations at a break](#decorations-at-a-break).

- A decoration defined over the *whole* box — a `border-radius`, a gradient or image background layer, a `box-shadow` — is measured against the whole box under `slice`, so a rounded, gradient-filled box divided between two columns rounds only at its true ends and its gradient runs on from one column into the next. See [Decorations at a break](#decorations-at-a-break).

A multi-column container nested inside another one splits its own children per inner column, independently in each outer column it is filled in.

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `column-count` | [column-count](https://developer.mozilla.org/en-US/docs/Web/CSS/column-count) | Full support |
| `column-width` | [column-width](https://developer.mozilla.org/en-US/docs/Web/CSS/column-width) | Full support; resolves to as many columns as fit at at-least this width |
| `columns` | [columns](https://developer.mozilla.org/en-US/docs/Web/CSS/columns) | Shorthand for `column-width` and `column-count`, in either order |
| `column-gap` | [column-gap](https://developer.mozilla.org/en-US/docs/Web/CSS/column-gap) | Same underlying property as flexbox/grid's `gap` (initial value `normal`). Multicol resolves `normal` to `1em` (matching real-world browser behavior); flexbox/grid resolve it to `0` |
| `column-rule` | [column-rule](https://developer.mozilla.org/en-US/docs/Web/CSS/column-rule) | Shorthand for `column-rule-width`/`column-rule-style`/`column-rule-color`; renders as a real vertical line between columns, one segment per page-row the container spans |
| `column-rule-width` | [column-rule-width](https://developer.mozilla.org/en-US/docs/Web/CSS/column-rule-width) | Full support, including `thin`/`medium`/`thick` |
| `column-rule-style` | [column-rule-style](https://developer.mozilla.org/en-US/docs/Web/CSS/column-rule-style) | `solid`, `dashed`, `dotted`; `double`/`groove`/`ridge`/`inset`/`outset` render as `solid`. `none` and `hidden` both draw no rule at all, and force the used [`column-rule-width`](https://developer.mozilla.org/en-US/docs/Web/CSS/column-rule-width) to 0. Either way the column gap is unaffected — a rule is painted in the middle of the gap rather than out of it, so its width never changes where the columns fall |
| `column-rule-color` | [column-rule-color](https://developer.mozilla.org/en-US/docs/Web/CSS/column-rule-color) | Full support, including `currentcolor` |
| `column-fill` | [column-fill](https://developer.mozilla.org/en-US/docs/Web/CSS/column-fill) | `balance` (the default) is solved per row via a binary search for the minimum column height that still packs as many children into the row as the full page budget would, floored at an even share of the content so that splittable content balances too — tighter than a single closed-form estimate, especially with unevenly-sized children. `auto` fills each column to capacity before starting the next |
| `column-span` | [column-span](https://developer.mozilla.org/en-US/docs/Web/CSS/column-span) | `all` breaks the column flow: a spanning element renders at the container's full content width, splitting the columns before and after it into independently-balanced runs. Only a **direct** child of the multi-column container is recognized as spanning — a `column-span: all` declared on a deeper descendant has no effect |

### Positioning

Used with `position: relative`, `absolute`, or `fixed`.

An absolutely (or `fixed`) positioned box is **blockified** ([CSS Display 3 §2.7](https://developer.mozilla.org/en-US/docs/Web/CSS/display) / CSS 2.1 §9.7): an inline-level computed display (`inline`, `inline-block`, `inline-flex`, `inline-table`) is coerced to its block-level equivalent, so e.g. a `::before` with no explicit `display` still becomes a real out-of-flow block. An absolute box's **containing block** is the padding box of the nearest positioned ancestor ([CSS 2.1 §10.1](https://www.w3.org/TR/CSS21/visudet.html#containing-block-details)), which is what percentage `width`/`height`/`top`/`left` resolve against (not the nearest in-flow block container, when those differ); a **fixed** box's is the page area of the page it is drawn on, so its offsets and percentages are measured from that page's content corner and can differ from page to page when the pages do. With `width: auto` and both `left` and `right` set, the box fills the space between them ([§10.3.7](https://www.w3.org/TR/CSS21/visudet.html#abs-non-replaced-width)); likewise `height: auto` with both `top` and `bottom` set ([§10.6.4](https://www.w3.org/TR/CSS21/visudet.html#abs-non-replaced-height)). When `top`, `height` and `bottom` are all set instead, an `auto` block-axis margin absorbs whatever space is left over — one `auto` margin takes all of it, and `margin-top: auto` with `margin-bottom: auto` splits it evenly, which is the usual way to centre an absolute or fixed box vertically. Content-sized containing blocks use their final height for this calculation. A negative margin on the opposite edge is honored the same way, so a box can be pushed entirely past its containing block's edge. Out-of-flow children of a **flex or table** container are laid out too (they don't participate in flex/table layout but still get positioned and sized).

An absolutely positioned box inside **inline content**, such as a badge in a `<span>` in the middle of a sentence, stays out of the line. The text before and after it stays on the same line, and the inline around it is not split (as it would be around an in-flow block). An absolutely positioned box contributes nothing to the shrink-to-fit width of a float, table cell or flex item around it, nor to a table cell's content height, so a badge hanging below a cell's text does not move that text. When the nearest positioned ancestor is an **inline** element, such as a `<span style="position: relative">`, that inline is the containing block. For an inline on one line, [CSS 2.1 §10.1](https://www.w3.org/TR/CSS21/visudet.html#containing-block-details) defines it as the box around the inline's padding edges. For an inline that wraps, PeachPDF takes each edge from the line fragment [CSS Positioned Layout 3](https://www.w3.org/TR/css-position-3/#def-cb) assigns it to, and measures it at that fragment's padding edge as Chromium does (that specification names the content edge): the top and left edges come from the inline's first line fragment, and the bottom and right edges from its last fragment; in `rtl` text, left and right come from the last and first fragments instead. So `top: 0; left: 0` sits at the start of a highlighted word, and `bottom: 100%` puts a label just above it. An inline that holds nothing but the positioned box, such as an icon or tooltip wrapper, is a zero-width box at its place in the line, one line of its font tall, and the positioned box is placed from there.

Known limitations:

- **Static position is not used.** An absolutely positioned box whose offsets are all `auto` sits at its containing block's top-left padding corner. It does not sit where it would have been in the normal flow ([CSS 2.1 §10.3.7](https://www.w3.org/TR/CSS21/visudet.html#abs-non-replaced-width)). Give it an explicit `top`/`left` to place it. In a table cell with `vertical-align: middle` or `bottom`, such a box still moves with the cell's aligned content, so it stays near the text.
- **A positioned inline that a page break falls inside forms its containing block from the fragments placed so far.** An absolutely positioned box in such an inline resolves `bottom` and `right` against the last fragment on the page the box is laid out on, rather than against the inline's true last fragment on a later page. `top` and `left` are unaffected, and so is an inline that does not cross a page break.
- **An empty positioned inline's own `vertical-align` is not applied.** When the inline holds nothing but the positioned box, its zero-width box sits on the line's baseline even if the inline is raised or lowered with `vertical-align` (`super`, `top`, a length). Offset the positioned box with `top`/`bottom` instead.
- **An empty positioned inline between runs of opposite direction is placed at the far end of one run.** In mixed-direction text, such as an `rtl` paragraph with an English phrase, a zero-width inline at the boundary between the two directions goes to the far end of the neighbouring run instead of sitting between them. Within a run of one direction it is placed correctly.

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
| `list-style-type` | [list-style-type](https://developer.mozilla.org/en-US/docs/Web/CSS/list-style-type) | `disc`, `circle`, `square`, `none`. Numeric (positional digit-substitution) styles: `decimal`, `decimal-leading-zero`, `arabic-indic`, `bengali`, `cambodian`/`khmer`, `cjk-decimal`, `devanagari`, `gujarati`, `gurmukhi`, `kannada`, `lao`, `malayalam`, `mongolian`, `myanmar`, `oriya`, `persian`, `tamil`, `telugu`, `thai`, `tibetan`. Alphabetic/additive styles: `lower-alpha`/`lower-latin`, `upper-alpha`/`upper-latin`, `lower-roman`, `upper-roman`, `lower-greek`, `armenian`/`upper-armenian`, `lower-armenian`, `georgian`, `hebrew` (including the 15/16 Tetragrammaton-avoidance substitution), `hiragana`/`hiragana-iroha`, `katakana`/`katakana-iroha`. Fixed styles: `cjk-earthly-branch`, `cjk-heavenly-stem` (each falls back to `decimal` past its 10/12-item range). `ethiopic-numeric`. Symbolic (fixed-glyph, uncounted) styles: `disclosure-open`, `disclosure-closed`. A literal `<string>` marker (e.g. `list-style-type: "→ "`) is also supported, with no automatic suffix. An unknown/unsupported named style falls back to `decimal` per [CSS Counter Styles Level 3 §2](https://www.w3.org/TR/css-counter-styles-3/). The East Asian "longhand" counter styles (`cjk-ideographic`, `japanese-formal`/`informal`, `korean-hangul-formal`, `korean-hanja-formal`/`informal`, `simp-chinese-formal`/`informal`, `trad-chinese-formal`/`informal`) and the `symbols()` function / author-defined `@counter-style` are not yet supported |
| `list-style-position` | [list-style-position](https://developer.mozilla.org/en-US/docs/Web/CSS/list-style-position) | `inside`, `outside` |
| `list-style-image` | [list-style-image](https://developer.mozilla.org/en-US/docs/Web/CSS/list-style-image) | URL, data URI, and all CSS gradient functions supported; same image types as `background-image`, including SVG url() sources rendering as real vector content |

### Page Breaks

These properties control how content breaks across PDF pages. Both the legacy `page-break-*` names and the modern `break-*` names are recognized.

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `break-before` | [break-before](https://developer.mozilla.org/en-US/docs/Web/CSS/break-before) | The full value set is accepted: `auto`, `avoid`, `avoid-page`, `page`, `left`, `right`, `recto`, `verso`, `avoid-column`, `column`, `avoid-region`, `region`. `page`, `left`, `right`, `recto` and `verso` all force a page break, the four directional values additionally inserting a blank page where the requested side needs one (see [Directional page breaks](#directional-page-breaks) below); `avoid` and `avoid-page` participate in keep-with-next (see the note below the table). `column` forces a break at the next column boundary inside a [multi-column container](#multi-column-layout), and is ignored outside one — there is no column boundary for it to name (see [Column breaks](#column-breaks) below). `region` and `avoid-region` parse for conformance but have no layout effect — see the accepted-values note below. `always` and `all` are **not** accepted: they are [Level 4](https://www.w3.org/TR/css-break-4/) additions, outside the value set above. Write `break-before: page`, or use the legacy `page-break-before: always` |
| `break-after` | [break-after](https://developer.mozilla.org/en-US/docs/Web/CSS/break-after) | Same value set, same behavior, on the trailing side of the box. A directional `break-after` on the **last** box of the document pads it with a blank page where one is needed for the page that would follow to fall on the requested side — so a volume can be made to end such that the next one opens recto |
| `break-inside` | [break-inside](https://developer.mozilla.org/en-US/docs/Web/CSS/break-inside) | `auto`, `avoid`, `avoid-page`, `avoid-column`, `avoid-region`. `avoid` and `avoid-page` are honored — they move a box that would straddle a page boundary to the top of the next page. `avoid` and `avoid-column` are honored at a **column** boundary, moving a box that would split across one to the top of the next column (see [Column breaks](#column-breaks) below). Each targeted value covers exactly the context it names, so `avoid-page` never suppresses a column break and `avoid-column` never suppresses a page break. `avoid-region` names a context PeachPDF does not establish and has no layout effect. On a `<thead>` or `<tfoot>` the property has a second job — it is one of the two conditions [CSS Tables Level 3](https://www.w3.org/TR/css-tables-3/#repeated-headers) puts on repeating the group across pages, and the user-agent stylesheet supplies `avoid` for both elements so that repetition is the default; see [When a `<thead>` or `<tfoot>` repeats](#when-a-thead-or-tfoot-repeats) |
| `page-break-before` / `page-break-after` | [page-break-before](https://developer.mozilla.org/en-US/docs/Web/CSS/page-break-before) | The legacy aliases of `break-before`/`break-after`, with their own (smaller) value set: `auto`, `always`, `avoid`, `left`, `right`. They set the same underlying property, with `always` treated as `page`, `avoid` as `avoid`, and `left`/`right` carrying the same directional behavior as their `break-*` spellings |
| `page-break-inside` | [page-break-inside](https://developer.mozilla.org/en-US/docs/Web/CSS/page-break-inside) | The legacy alias of `break-inside`: `auto`, `avoid` |
| `box-decoration-break` | [box-decoration-break](https://developer.mozilla.org/en-US/docs/Web/CSS/box-decoration-break) | Both values honored, at page breaks and at the line breaks of an inline box alike — see [Decorations at a break](#decorations-at-a-break) below |
| `orphans` | [orphans](https://developer.mozilla.org/en-US/docs/Web/CSS/orphans) | Enforced **at the break point**: where a fragment would keep fewer than `orphans` lines of a paragraph-like box, the break falls *before* the box instead, so those lines travel with the rest of its content. This holds for a box of any height — including one taller than the fragmentainer, which the whole-box push below cannot help — and at a column boundary as well as a page boundary. A run of preceding siblings chained to the box by `break-after: avoid` travels with it, as at every other relocation. Two limits: the break is moved at most once per box (moving it again cannot give the box more room, and repeated it would walk the box down the document), and in a document with per-page left/right `@page` margins the decision is taken only once the per-page measures have settled, and never where it would move the box onto a page of a different measure |
| `widows` | [widows](https://developer.mozilla.org/en-US/docs/Web/CSS/widows) | Enforced after the fact rather than at the break point, because how many lines follow a break is not known until the rest of the content has been flowed. Where too few lines would follow, **only the minimum number of lines the spec asks for is moved**: the fragment before the break keeps fewer, and the box itself does not move. That needs the fragment before the break to be laid out again, so it applies where the box has exactly two fragments — the common paragraph-across-one-page-break case. Where it cannot be arranged — a box spread over three or more fragmentainers, or a budget that would leave fewer than `orphans` lines behind — it falls back to laying the whole box out on the next page, which is in turn skipped for a box taller than one fragmentainer where moving it whole would recreate the violation. Like `orphans`, in a document with per-page left/right `@page` margins it is applied only once the per-page measures have settled, and never across a change of measure |

**Break values with no layout effect.** PeachPDF accepts the complete [CSS Fragmentation Level 3 §3](https://www.w3.org/TR/css-break-3/) value set on every break property, so a stylesheet written for a browser parses unchanged and its other declarations are unaffected. The values that change the output are `page`, `left`, `right`, `recto` and `verso` on `break-before`/`break-after` (forced page breaks — see [Directional page breaks](#directional-page-breaks) below), and `avoid` and `avoid-page` on `break-before`/`break-after` (keep-with-next, below) and on `break-inside`. `column` and `avoid-column` are honored inside a multi-column container, which is the only place a column boundary exists ([Column breaks](#column-breaks), below). The remainder are **completely** inert — not a weaker version of the behavior they name, but no effect at all: `region` and `avoid-region` are permanently inert, since PeachPDF has no CSS Regions fragmentation context for them to break into, and `column`/`avoid-column` say nothing about a document with no multi-column container in it. A value naming one context deliberately does **not** fall back to bare `avoid` or to `page` in another: a hint about column or region breaks must not change pagination, and a request for a column break must not become a page break. All of these are stored and cascade normally.

<a id="directional-page-breaks"></a>
**Directional page breaks (`left`, `right`, `recto`, `verso`).** Per [CSS Fragmentation Level 3 §3.1](https://www.w3.org/TR/css-break-3/#break-between) these force one *or two* page breaks, so that the content following the break begins on a page of the requested side. Pages alternate right, left, right, … from the first page — the same left-to-right progression `@page :left` / `@page :right` select on — so a `break-before: right` whose content would otherwise land on a left page inserts one blank page ahead of it. `recto` and `verso` are `right` and `left` in this progression.

A page inserted this way is an ordinary page: it takes its `@page` context's canvas background and margin boxes, and it counts toward `counter(page)` and `counter(pages)`. There is no `@page :blank` to style or suppress it separately.

Two limitations. Where a document also uses per-page `@page` left/right *margin* overrides, which re-wrap each page's content to its own width, the page a break lands on (and therefore whether a blank page is needed) depends on that re-wrapping, which is resolved iteratively rather than exactly.

And the side is resolved against the page's position in the full page grid, while the printed page number counts only the pages actually produced. Those agree for an ordinary document, but they drift apart after a page that was dropped for having no content on it — most plausibly a tall empty gap between two blocks. Past such a gap, a `break-before: right` can land on a page that prints as an even number, and `@page :right` will not style it. Keep large vertical gaps out of a document that relies on which side its pages fall on.

**Keep-with-next (`break-after: avoid` / `break-before: avoid`).** Per CSS Fragmentation §3.1, an `avoid` (or `avoid-page`) on either side of a sibling break point forbids an unforced break between the two boxes. PeachPDF honors this wherever it relocates content to the next page: when a box is relocated wholesale (a table whose body would cross a page boundary, a `break-inside: avoid` box, an `orphans`/`widows` push) or when ordinary word flow pushes a block's *first line* to the next page, the maximal run of preceding siblings chained to it by `avoid` values goes with it, so a heading is never stranded at the bottom of the page its content just left. The run is laid out again at its destination rather than shifted there, so each of its members reads as though it had started on that page — including where the heading was placed on an earlier page than the one the decision is taken on, which is the ordinary case whenever the content's *own* text breaks across the boundary: the page that placed the heading is laid out again too, and the fragments it had already produced are withdrawn. The UA default stylesheet applies `h1-h6 { break-after: avoid }` (under `@media print`, which PeachPDF always uses), so headings get this behavior out of the box. Chains are transitive (e.g. `h2` + `h3` + paragraph move as a group), a forced break value on either side of a pair takes precedence over `avoid` per §5.2, and an `avoid` that cannot be satisfied in full is relaxed progressively per §4.3 rather than abandoned: the run is trimmed from its *front* until what remains fits the destination — the subheading immediately above the content travels even where the chapter heading above it cannot — and only where no member can travel does the content move alone, exactly as it would without the `avoid`. Two shapes keep the older behavior of *moving* the group rather than laying it out again — a run whose members include a table that repeats a header, which does not survive a second layout, and a heading on a page that no single layout pass was filling (a forced break can step over a page without ending the pass). The heading still travels with its content in both; only the way it gets there differs, and the paragraph below it can then show a little extra space where the page boundary fell inside it. The run is found at the break point the content really names, so a heading is chained to it whether it is a sibling of the moving box or of a container that box begins (see [break propagation](#page-breaks) above). The same chain applies at a **column** boundary, where `avoid-column` joins `avoid` in naming it — see [Keep-with-next at a column boundary](#keep-with-next-at-a-column-boundary) — and between two flex lines or grid rows, where the chain runs over lines rather than siblings — see [Breaks in flex and grid containers](#breaks-in-flex-and-grid-containers).

**Margin truncation at page breaks:** per [CSS Fragmentation Level 3 §5.2](https://www.w3.org/TR/css-break-3/#break-margins), when a vertical margin between two elements is large enough that it alone would push the following element onto a later page (an *unforced* break — no explicit `break-before`/`break-after`/`page` involved), that margin is discarded entirely and the following element starts flush at the top of the very next page, rather than the margin paginating through as literal blank pages. A margin-top large enough to visually separate content across a deliberate page boundary should use an explicit `break-before: page` (or `page-break-before: always`) instead — margins after a *forced* break are preserved, not truncated, per the same spec section. This applies to normal block flow, including inside a multi-column container, whose columns are fragmentainers; flex, grid and table position their own children and are not covered.

The element need not have a preceding sibling: the break point before a container's *first* in-flow child is a real break point too, and its margin is truncated at one. This holds when something on the container — a border or padding — keeps that margin from collapsing out of the child and into the container. Where nothing does, the margin collapses all the way up the ancestor chain and belongs to the outermost box it reaches rather than to the child, and a margin that reaches the root that way still paginates as blank space.

**Forced breaks before a first child, and break propagation.** `break-before` (and the legacy `page-break-before`) is honored on a container's first in-flow child, which is the common `section > h1 { break-before: recto }` idiom. Per [§3.1](https://www.w3.org/TR/css-break-3/#break-between) the break point before a first in-flow child *is* the break point before its container, and likewise a `break-after` on a last in-flow child is the break point after the container — so the value travels outward through every box the element begins or ends, and the outermost such container is what takes the break and moves with it. Its own background, border, padding and margin therefore begin the new page along with the element, rather than being left spanning the boundary with an empty copy on the page the content left. A heading chained to the moved content by `break-after: avoid` is carried along too, whether it is a sibling of the element or of the container, so the two shapes behave the same way.

Propagation stops before the fragmentation context itself, and before any box whose children are positioned by a layout engine of their own — a flex or grid item, a table cell, a multi-column child. A break declared on the very first thing in a document therefore has nothing before it to break from: no break is taken and no blank page is manufactured in front of it, which is [§4.4](https://www.w3.org/TR/css-break-3/#break-between)'s "no empty fragmentainer" rule. For the same reason, a forced break that is not taken still counts as one for margin purposes: there is no break point in front of such a box for a margin to adjoin, so its margin is preserved rather than truncated.

**Combining break values at one break point.** Where more than one forced value applies at a single break point — this box's `break-before`, the preceding box's `break-after`, and anything either of them propagates outward — §3.1 asks that all of them be honored. A directional value forces a page break as well as naming a side, so it subsumes a plain `page`. Two *conflicting* directional values cannot both be honored, and there the spec is explicit: the value on the latest element in flow wins, which for a value travelling outward is the deeper one.

The same applies to the breaks that are decided *after* content has been placed rather than declared in advance — `break-inside: avoid`, [monolithic content](#monolithic-content), and an `orphans` push. When one of those relocates a container's first child, the container goes with it, so its border, background and padding open the new page rather than being cut in two. The exception is a container too tall for the destination page: moving it there would leave it not fitting either, so it stays and only the element moves — the same progressive relaxation an unsatisfiable `avoid` gets.

<a id="breaks-in-flex-and-grid-containers"></a>
**Breaks in flex and grid containers.** A flex container's break points are between its **lines**, and a grid container's between its **rows** ([CSS Fragmentation Level 3 §3.1](https://www.w3.org/TR/css-break-3/#break-between)) — not between the items sharing one, which the cross-axis alignment holds together. So `break-before` on an item, and `break-inside: avoid` (or `avoid-page`) or [monolithic content](#monolithic-content) among its items, move the **whole line or row** to the next page rather than the item that asked. An item spanning several grid rows travels with the first of them.

A forced break is taken from **either** side of the break point, as §3.1 requires: `break-after: page` (or the legacy `page-break-after: always`) on an item starts the line or row *after* it on the next page, exactly as `break-before` on that following line's own items would. A directional value (`left`, `right`, `recto`, `verso`) is honored in full here, as it is in block flow: the line or row opens a page of the requested side, and a blank page is inserted where the side needs one — see [Directional page breaks](#directional-page-breaks). Where more than one value applies at one break point they combine by §3.1's rule, so a directional value on either side subsumes a plain `page` on the other. The line that declared the `break-after` stays where the flow put it — the break is after it. A `break-after` on the **last** line or row names the break point after the container itself, which a flex or grid container does not act on: a break travelling out of an item would name a position the engine is about to overwrite. An item spanning several grid rows is the earlier sibling of the row after the *last* row it covers, so its `break-after` is taken there rather than at a boundary running through the middle of the item.

A forced break at a point that already **is** a page boundary is already satisfied, per [§4.4](https://www.w3.org/TR/css-break-3/#break-between)'s "no empty fragmentainer": a line whose top is flush on a page's content top stays there rather than being moved a further page on. A directional value asks for more than a boundary, though — it asks that the content *begin* on a page of the named side — so a flush line satisfies it only where the page it already begins is that side, and otherwise still moves.

**Keep-with-next between two lines or rows.** `break-after: avoid` (or `avoid-page`) on one line's items, and `break-before: avoid` on the next line's, both forbid an unforced break at the point between them, and PeachPDF honors it by moving the **earlier** line: where a page boundary would otherwise fall there — because the later line was relocated, or simply because the flow put it on the next page — the earlier line travels with it, so a heading that is a flex item is not stranded at the foot of the page its section body just left. The UA default stylesheet's `h1-h6 { break-after: avoid }` makes this the ordinary case. Chains are transitive over consecutive lines, a forced break at the same point takes precedence per §3.1, and an `avoid` that cannot be satisfied is relaxed progressively per [§4.3](https://www.w3.org/TR/css-break-3/#possible-breaks) rather than abandoned: the chain is trimmed from its *front* until what travels fits the destination page, and a chain reaching the top of its own page is dropped altogether, since moving all of it would leave that page blank and ask the same question again on the next one.

An item with no break value of its own is left where it is, and the page boundary cuts it — the same answer ordinary block content gets when it has no line to break at. A line or row taller than a whole page is also left alone, since moving it could not help.

**Which line is above the boundary is a question about the page, not about the source.** The two orders agree in a grid and in a flex container that wraps the ordinary way, but [`flex-wrap: wrap-reverse`](https://developer.mozilla.org/en-US/docs/Web/CSS/flex-wrap) puts the first line in the source *last* down the page. PeachPDF reads such a container down the page: the lines that follow one that moves are the lines physically below it — there, the *earlier* ones in the source — so a relocated line is never drawn over the top of one that stayed put. Identifying the break point is still §3.1's question about source order, so `break-after` on the first line's items and `break-before` on the second's name the same point, which under `wrap-reverse` is the boundary *above the first line* — and it is therefore that line which opens the next page.

The break point before the container's *first in-flow child* is the break point before the container itself, and that boundary sits above the container's topmost content whichever line happens to be there — so under `wrap-reverse` a `break-before` on the first item still starts the whole container on a new page, rather than tearing off the one line that sits at the bottom.

**A column container** — `flex-direction: column` or `column-reverse`, wrapping or not — stacks its *lines* along the inline axis instead: two side-by-side lines sit in one range down the page, so no page boundary falls between them and a break value declared at that point has nothing to act on (unchanged from before). What such a container's lines *do* have are real break points between the **items within** one line, since those are genuinely stacked in the block axis — exactly the shape a row container has between its lines, one level down. So `break-before`/`break-after` between two items of the same column line is honored (taken from either side, per §3.1), and `break-inside: avoid` or [monolithic content](#monolithic-content) on one item moves only that item — and whatever follows it in the same line's block-axis order — rather than the line's earlier items or any other line. Only the container's own first in-flow child still speaks for the boundary *above the container itself*: every other line's own leading item starts at that same block-axis position with nothing genuinely before it in flow order, so a break value declared there is inert, the same way the point between two side-by-side lines is. Two side-by-side lines are independent of each other for this purpose: moving an item in one because of its own break value never displaces the other line, which is exactly what "no break point between lines" already implied.

Two limits are worth knowing. A flex or grid container **inside a table** is positioned against the table's own row grid, so its lines are left to the table engine rather than moved against the page grid. And an `inline-flex` or `inline-grid` box is an atomic inline — where it sits is the line it is on to decide.

**Item content fragments across pages** for every flex and grid shape: a `flex-direction: row`/`row-reverse` line's items (whether the container has one line or wraps into several), a `flex-direction: column`/`column-reverse` line's items (walked in sequence — see below), and every grid row's items (including a row-spanning item's own content, and a `subgrid` item's). Each item's own content — a line of text, or a nested block-level layout such as a `column-count`/`column-width` container — continues on the next page rather than being moved whole or cut when it does not fit. This composes with everything above: the whole-line/row relocation described earlier in this section still decides which page a line or row begins on, and each item's own content is then laid out for real once it is there.

A row/row-reverse line's items are [css-break-3 §2.1](https://www.w3.org/TR/css-break-3/#parallel-flows) "parallel flows" (the spec's own example is exactly this shape) — they commit together, one item's own content stopping partway through a page does not stop a line-mate beside it from continuing or finishing on its own. Grid rows read the same way. A column/column-reverse line's items are instead a **sequential** flow along the block axis: each is walked and committed in turn, so a later item's content continues onto the next page only once an earlier item has finished; a `flex-wrap` column container's several lines, which sit side by side sharing no block-axis range, each run this sequence independently of the others. This composes with the break-*value* behavior described above: a forced `break-before`/`break-after` between two items of one line, or a `break-inside: avoid` item that straddles a boundary, decides which page each item *begins* on, and each item's own content then fragments across pages from there exactly as it does anywhere else in a column line.

A line or item content taller than the page's whole content band is left alone wherever it appears, the same [monolithic content](#monolithic-content) answer ordinary block flow gets, since there is no page that could hold it — moving it could only repeat the same overflow on the next page. It is clipped to the page it begins on rather than continued: give such content a `break-inside: avoid` (or size it so a line cannot straddle) where that would be unacceptable.

<a id="breaks-in-tables"></a>
**Breaks between table rows.** The break point between two rows of a table is an ordinary break point ([CSS Fragmentation Level 3 §3.1](https://www.w3.org/TR/css-break-3/#break-between)), and a forced value declared there is honored: `break-before: page` (or one of the directional values) on a `<tr>` starts that row on the next page even where it would have fitted, and a `break-after` on the row above it does the same. A value on a `<tbody>`, `<thead>` or `<tfoot>` is a value at the break point before its first row or after its last, so it is honored at that row. A table repeating a header carries the header onto the page such a break opens, exactly as it does at a break the row heights themselves forced.

`break-inside: avoid` on a row needs nothing to be honored: a row that would straddle a boundary is carried onto the next page whole rather than split, wherever the engine can move it.

**A cell whose `rowspan` crosses a page break is split at the break.** [CSS Tables Level 3](https://www.w3.org/TR/css-tables-3/#breaking-rules) asks for a row to be kept unfragmented only "if the cells spanning the row do not span any subsequent row", so the row that *ends* a span is carried onto the next page whole like any other row, while the spanning cell itself is fragmented: its box closes at the foot of the page it began on and opens again at the head of the next, its borders and background running the depth of each fragment, and — as the spec requires — no border is drawn at the break on either side of it. The column-context values (`column`, `avoid-column`) name a fragmentation context a table does not establish; inside a [multi-column container](#multi-column-layout) they are the container's to act on.

This holds even where the spanning cell's own content is taller than the page it began on: the cell's content stops and resumes across pages the same way any other content does, and the cell's box closes and reopens with it rather than being stretched across every page in between.

> **Limitation:** a spanning cell is not split inside a [multi-column container](#multi-column-layout), where a table's rows are left to the table's own grid rather than moved against the column; and the cell's `vertical-align` resolves against the fragment on the page it began on rather than against the whole cell, so `middle` centres its content in that fragment.

**A row taller than the page is left where it is, and the rows after it continue below it.** Where a row's cell holds a block taller than the whole content band — a fixed-height figure or chart, say — there is no page that could hold that row, so it stays where it is and overflows across as many pages as it needs ([CSS Fragmentation Level 3 §4.3](https://www.w3.org/TR/css-break-3/#possible-breaks): moving it could only recreate the straddle). The rows after it are placed immediately below where it *ends*, on the page its bottom actually landed on.

**A repeating header is carried onto every page the table spans, and the row resumes below it.** [CSS Tables Level 3](https://www.w3.org/TR/css-tables-3/#repeated-headers) repeats a `<thead>`/`<tfoot>` on each page a table *spans*, not on each page it *broke* on, and asks the user agent to "leave room" for the group there. A page that a too-tall row merely overflows through is one of those pages — and the only way to leave room on a page nothing breaks on is to slice the row itself, which [CSS Fragmentation Level 3 §4.3](https://www.w3.org/TR/css-break-3/#possible-breaks) allows in as many words: "the UA may also fragment the contents of monolithic elements by slicing the element's graphical representation".

So the row is neither resized nor moved. Each page draws it from a different origin and shows the strip belonging to that page, and the strips meet exactly — a background, a gradient or an image runs continuously from one page to the next, picking up below the repeated header rather than underneath it. This applies to a block taller than the page, to a replaced element such as an `<img>` or an inline `<svg>`, and to the second and later pages of a single-row table taller than one page. A `<tfoot>` closes each of those pages the same way, from the other end of the band.

> **Limitation:** the room is left on the pages a row *overflows through*, which are the pages no move could help with. A row that would fit the next page is moved there whole instead, as before, and a table that did not fragment at all is moved whole by the rule below — neither is sliced. Where a page's own band is short enough that the repeated groups would leave the row no room at all, that page gives the row the whole band, since a page that took no content could never finish the document — the repeated group is still drawn there, so on that page alone it overlaps the row rather than sitting above it. Reachable only under per-page `@page` geometry, since the quarter-of-the-page conditions above otherwise keep both groups well inside a band.

**A table that did not break moves whole.** A table breaks between two of its rows when the next row does not fit, and where no row break was taken at all — most often a single-row table, or one whose only row is taller than the page — the table did not fragment, and it is carried onto the next page in one piece rather than painting sliced across the boundary. This applies to every table, including one repeating a `<thead>` or a `<tfoot>`: the header follows it onto the page it lands on. A table taller than one page is left where it is, since moving it would only recreate the straddle. A heading chained to the table by `break-after: avoid` — the user-agent print default for `h1`–`h6` — travels with it, as at every other relocation.

**A cell's text breaks between lines, not through one.** Where a cell holds more text than the page has room for, the text continues on the next page and each line lands whole on one page or the other — a line is never cut through the middle and drawn on both. This holds for the cell's own text and for text inside a block within it alike.

**A row whose cell runs out of room continues on the next page.** A table is fragmented like any other content: where a cell cannot fit the rest of its content on the page, the row it is in continues on the next one, and the rows after it are placed there rather than being left unplaced. The cells of one row are fragmented **independently** ([CSS Fragmentation Level 3 §2.1](https://www.w3.org/TR/css-break-3/#parallel-flows), [css-tables-3 §6.1](https://www.w3.org/TR/css-tables-3/#fragmentation)): a short cell beside a long one finishes on the first page and none of its content is drawn again on the continuation, while the long one picks up exactly where it stopped. A table repeating a `<thead>` carries it onto every page such a row reaches, and room is left for it there, so the continuing cell resumes *beneath* the repeated header rather than being overlapped by it — the same as at a break between two rows. A repeating `<tfoot>` closes each of those pages the same way, read from the other end of the band: [CSS Tables Level 3](https://www.w3.org/TR/css-tables-3/#repeated-headers) asks for room to be left for a repeated header and says "the same applies for footer rows and the table bottom border", so the continuing cell's own text stops *above* the repeated footer rather than running underneath it. The footer on the table's last page is a different footer — the one that closes the table, under its final row.

> **Limitation:** the room left for a repeated `<tfoot>` is left for the cell's own text. Content in a cell that runs its own fragmentation — a multi-column container — and a block inside a cell that is moved whole rather than split are both placed against the full page instead, so either can end up underneath the repeated footer.

**A finished cell's box continues with its row.** §6.1 continues the row's box into the next page and every cell's box with it, so the cell that finished is drawn there as its own background and borders running the full depth of the row's continuing fragment, with no content in them — which is what a browser draws. That depth is set by the cells that do continue into the page, since a cell with nothing left to place asks for no height of its own. The edges the page break itself made carry no border, because [`box-decoration-break`](https://developer.mozilla.org/en-US/docs/Web/CSS/box-decoration-break) defaults to `slice`: the cell closes with a border at its real top and its real bottom only, exactly as a block spanning pages does.

<a id="when-a-thead-or-tfoot-repeats"></a>
**When a `<thead>` or `<tfoot>` repeats.** [CSS Tables Level 3](https://www.w3.org/TR/css-tables-3/#repeated-headers) makes repetition conditional, and PeachPDF applies both conditions:

- **The group must carry an avoiding `break-inside`.** The user-agent stylesheet supplies `thead, tfoot { break-inside: avoid }` under `@media print` (which PeachPDF always uses), so this is what every table gets by default — repetition out of the box, as in a browser. Writing `break-inside: auto` (or `break-inside: avoid-column`, which names a context a table does not establish) on the group is the way to opt **out**: the group is then laid out once, in flow, at the table's top or under its last row, and the pages after the first are given wholly to the rows.
- **The group must be shorter than a quarter of the page** — a quarter each for the header and the footer, per the spec's *"up to one quarter for header rows, and up to one quarter for footer rows"*. A taller group is laid out once instead. The room a repeated group takes is genuinely reserved on every page the table spans (see above), so without the cap a tall group is charged its own height out of every one of them — and a group taller than the page's content band would leave no page able to make progress at all. The quarter is measured against the whole page box, margins included, which is what "the page height" means here — not against the smaller content band the group is actually drawn in.

Both are decided once per table, and a page a row continues onto inherits that decision rather than re-taking it. A table laid out with no page grid at all — a measurement pass, or an unpaginated container — has no page to take a quarter of, so only the `break-inside` condition applies there.

A group that is not repeated still has to fit the page it is drawn on. No room is reserved for it — reserving room on every page is exactly what not repeating avoids — so where the last row runs to the foot of its page, the closing `<tfoot>` is carried onto the next one whole rather than drawn across the boundary. It moves as a unit, since a row group cannot be split; a footer taller than a whole page is left where it is, because moving it could only recreate the straddle.

Two limits. Keep-with-next (`break-after: avoid`) between two rows is not honored — the chain is read among block-flow siblings, and a table's rows are placed by the table itself. And a break value on a box *inside* a cell is likewise the table's row grid to answer, not the page grid's.

<a id="monolithic-content"></a>
**Monolithic content moves whole rather than being split** ([CSS Fragmentation Level 3 §2](https://www.w3.org/TR/css-break-3/#monolithic)). Some content may not be broken at all, and where it would straddle a page boundary it is carried onto the next page in one piece. Two kinds qualify:

- **Replaced elements** — `<img>`, `<svg>`, `<iframe>`, and an `<object>`/`<video>` whose resource resolved to an image. These have no inner structure to fragment.
- **Scroll containers** — any box whose `overflow` is not `visible` or `clip`. The root element is excluded, because its `overflow` propagates to the viewport rather than making it a scroll container ([CSS Overflow Level 3 §3.3](https://www.w3.org/TR/css-overflow-3/#overflow-propagation)); a paginated document has no viewport for it to propagate to, so the common `html { overflow: hidden }` idiom does not declare a whole document unbreakable. `<body>` is excluded on the same grounds, but only while the root's own `overflow` is `visible` — once the root declares one, the body is a scroll container in its own right. (`overflow: clip` is not currently implemented and computes as `visible`, so it is excluded either way.)

One boundary is worth knowing: the rule applies to normal block flow, and inside a multi-column container, whose columns are fragmentainers. Inside a flex or grid container it applies to the whole **line** or **row** the box is in — see [Breaks in flex and grid containers](#breaks-in-flex-and-grid-containers). A monolithic box inside a table is placed against the table's own row grid, so it can still be split.

A box that had already begun flowing its own text across the boundary is **laid out again** at its new position rather than moved to it, so it reads exactly as it would have if it had started there — no blank band inside it, and no unused height. The same is true of every relocation described in this section: `break-inside: avoid`, monolithic content, and the `widows` fallback below all share one mechanism. Neither of the two [§5.4](https://www.w3.org/TR/css-break-3/#widows-orphans) line minimums relocates anything in the ordinary case: an `orphans` violation makes the break fall before the box (see [`orphans`](#page-break-properties) above), and a `widows` violation makes the fragment before the break keep fewer lines, so in both the box is laid out once, where it belongs. A scroll container taller than every page's own content band cannot be helped by moving it, so it is not moved at all: its content simply lays out unbroken and overflows across as many pages as it needs, each page showing its own slice — the same way any other oversized content already does.

One case still moves the box instead of laying it out in place: a keep-with-next run whose heading was already settled on an earlier page — which happens when the box's own text broke across the boundary and it finished later — moves as a group: the heading still travels with its content, but the content keeps the gap.

**A line box is never split across pages** ([CSS Fragmentation Level 3 §4.1](https://www.w3.org/TR/css-break-3/#possible-breaks)). When a line does not fit on the page it started on, the whole line moves to the next one — including any part of it that would have fitted.

**Content continues at the page's content edge.** Where content flows onto a following page it starts at that page's content edge, rather than fractionally below it.

**Layout gives up pagination before it gives up content** ([CSS Fragmentation Level 3 §4.3](https://www.w3.org/TR/css-break-3/#possible-breaks)). Every rule above may decline to place content where it would rather not go — `break-inside: avoid`, a keep-with-next chain, monolithic content, `orphans` and `widows` — and §4.3 asks that such constraints be given up progressively rather than at the cost of content. The last thing given up is pagination itself: if a page is reached that layout cannot get past, the rest of the document is laid out from that page on in one piece, with no further break decisions, so it runs past the page's edge and is cut at each boundary rather than broken at one. Those pages will look wrong, but the document renders.

#### Decorations at a break

When a break splits a box, `box-decoration-break` ([CSS Fragmentation Level 3 §6.2](https://www.w3.org/TR/css-break-3/#break-decoration)) decides whether its border, padding, background, `border-radius` and `box-shadow` belong to the box as a whole or to each piece. Both values are honored, and — as the spec requires — at **every** kind of break: the page breaks of a block box, the column breaks of a box in a [multi-column container](#multi-column-layout), and the line-box breaks of an inline box that wraps.

`slice`, the initial value, renders the box as though it were never broken and then cuts it at the break. So no border and no padding is inserted at the break, no shadow is drawn along a broken edge, and the background, gradient layers and corner radii follow the whole box's geometry. A wrapping `<span>` with a background gradient and rounded corners therefore renders as **one continuous shape** whose gradient runs on across each wrap and whose only rounded corners are at its true start and end — the same as a browser.

`clone` wraps every piece independently: its own border on all four sides, its own padding, its own radii and shadow, and its own background, so a `no-repeat` background image is drawn once **per piece** rather than once per box. This is what to reach for when every line of a highlighted inline should be a self-contained rounded pill, or when a block crossing a page boundary should be closed off with its own border on each page. Space is reserved for the cloned border and padding, so they push the box's content rather than overlapping it — and where boxes are nested, each piece opens inside its ancestors' own re-opened border and padding.

Margin is cloned at a line break, alongside the border and padding, so each line of a margined inline keeps its gap on both sides. It is not cloned at a page break, because a margin adjoining one is already truncated to zero (see [Margin truncation at page breaks](#page-breaks) above), leaving nothing there to clone.

Limitations:

- Room is reserved for `clone`'s border and padding in normal block and inline flow, and at a column break — including for a box whose own content is blocks rather than text, which is split at a column boundary like any other. Inside a flex, grid or table, whose own engine positions its children, a cloned decoration at a break is painted but no room is made for it, so it can overlap content.
- A page break is decided against the text itself rather than the line box around it, so the **half** of a large `line-height`'s leading that falls below the last line on a page is not counted. A cloned bottom border can therefore sit within that half-leading. Ordinary line heights leave this invisible.
- An inline box that wraps *and* is split at a column boundary has its unbroken width measured within each column rather than across both, so a gradient on such a box restarts in the second column. A block-level box is unaffected, and so is an inline box that only crosses page breaks.
- Where a line box of an inline straddles a page boundary, that one line's decoration slice is drawn on both pages: once in its own place, and once near the top of the following page. Under `slice` the second copy is usually a thin band, partly clipped by the page edge; under `clone` it is a complete closed frame, drawn entirely inside the following page's content.

### Tables

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `empty-cells` | [empty-cells](https://developer.mozilla.org/en-US/docs/Web/CSS/empty-cells) | `show`, `hide` |
| `caption-side` | [caption-side](https://developer.mozilla.org/en-US/docs/Web/CSS/caption-side) | `top` (default), `bottom` — a `table-caption` box is stacked above or below the table's row grid and stretched to the table's own content width, outside the `<table>` element's own border/background per [CSS 2.1 §17.4](https://www.w3.org/TR/CSS21/tables.html#caption-position) |
| `table-layout` | [table-layout](https://developer.mozilla.org/en-US/docs/Web/CSS/table-layout) | `auto` (default) and `fixed`. `fixed` sizes each column from its `<col>`/`<colgroup>` element if one states a width, otherwise from that column's cell **in the first row only** (divided evenly across a `colspan`) — no cell content is ever measured and no row after the first affects a column's width, so overflowing text wraps inside its column rather than widening it — and text that cannot wrap overflows the column visibly, as it does in a browser, since a cell does not clip unless it declares `overflow: hidden` itself. Any remaining columns split the leftover space evenly. Requires the table's own `width` to be a specified (non-`auto`) length — with `width: auto`, `fixed` has no effect and the table uses the automatic, content-based algorithm instead, matching real browsers. `max-width` on the table still clamps the space being divided; a `<col>` width is read from `px`, unitless, or `%` values only (the same limitation `auto` layout already has for `<col>`) |
| `height` / `min-height` on `<table>`/`<tr>` | [height](https://developer.mozilla.org/en-US/docs/Web/CSS/height) | Per [CSS 2.1 §17.5.3](https://www.w3.org/TR/CSS21/tables.html#height-layout): a `<table>`'s used height is the *maximum* of its specified `height`/`min-height` and the rows' own natural total — an explicit value smaller than the rows' content never clips the table, only a larger one grows it (spec-mandated). Any surplus above the natural total is distributed across the body rows proportional to each row's own natural height — the same rule this engine already applies to column-width surplus, since CSS 2.1 itself leaves the exact distribution undefined. A `<tr>`'s own `height`/`min-height` works the same way, one level down — a per-row minimum, never a clip. A repeating `<thead>`/`<tfoot>` group's own rows keep their natural height and never receive a share of the surplus. On a `writing-mode: vertical-rl`/`vertical-lr` table, the row axis is physical *width* rather than height, so this same minimum-and-redistribute behavior is driven by `width`/`min-width` on the `<table>`/`<tr>` instead — `height`/`min-height` there keep their own, separate role sizing the table's columns (the inline axis). This still applies, on a best-effort basis, even when the table's own row loop must continue across a page boundary onto a separate, later page — the table's total natural row-axis extent across every page is tracked as it goes, and the final page's own remaining rows receive their own proportional share of the surplus computed against that running total. Rows already drawn on an earlier page are never touched or redrawn, so in this specific case the table's overall realized size can still fall a little short of the declared `height`/`min-height` (by however much of the surplus those earlier, already-committed rows would otherwise have taken) — an ordinary table, including one spanning many pages via per-row breaks within a single pass, reaches the declared size exactly. This continuation case is also less precise when the table additionally declares non-default `border-spacing` or a `<caption>`, tending to under-distribute the surplus rather than reaching the fully correct proportional share |

### Generated Content

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `content` | [content](https://developer.mozilla.org/en-US/docs/Web/CSS/content) | Used with `::before` / `::after` pseudo-elements; string, counter, `attr()`, and `none` values supported; `url()` (including an SVG source, rendered as real vector content) and all CSS gradient functions (`linear-gradient`, `radial-gradient`, `conic-gradient`, and repeating variants) are supported — image values require `display: inline-block` with explicit `width`/`height` on the pseudo-element. `counter()` accepts an optional counter-style argument (`counter(name, <style>)`, e.g. `counter(line, decimal-leading-zero)`) using the same named styles as [`list-style-type`](#lists) (a literal `<string>` marker isn't one of them, since it isn't a counter style); the style defaults to `decimal`, and an unknown/unsupported style falls back to `decimal` per [CSS Counter Styles Level 3 §2](https://www.w3.org/TR/css-counter-styles-3/). `counters(name, <separator> [, <style>])` is supported too, joining every value of that counter in the current scope chain (outermost first) with the separator — the usual way to render nested list numbering like `2.3.1`. `target-counter(<target>, <counter-name> [, <style>])` and `target-text(<target> [, content | before | after | first-letter])` ([CSS Content Module Level 3 §5, cross-references](https://www.w3.org/TR/css-content-3/#cross-references)) resolve a counter's value or an element's text at another element rather than the current one — the standard mechanism for a hand-authored table of contents with real (non-stale) page numbers. `<target>` accepts `<string>` (a literal id), `url(#id)`, or `attr(href)`/`attr(id)` (read off the declaring element, or its parent for a `::before`/`::after`). `target-counter(<target>, page)` resolves against the actual page the target element lands on after layout — the same mechanism `counter(page)` uses inside `@page` margin boxes, extended to flow content; a target inside repeated running/margin-box content or a repeated table header/footer is not resolved (see [Known limitation](#known-limitation--bookmarks-inside-repeated-runningmargin-box-content-or-repeated-table-headersfooters-are-not-collected) below — the same box-tree walk both features share). `target-text()`'s optional second argument selects `content` (the target's own text, the default), `before`/`after` (the target's `::before`/`::after` pseudo-element content), or `first-letter` (the first character of the target's text). `leader(<type>?)` ([CSS Content Module Level 3 §6](https://www.w3.org/TR/css-content-3/#leader-function), `<type> = dotted \| solid \| space \| <string>`, defaulting to `dotted` when omitted — `leader()` alone requests a dotted leader) fills the remaining space on the line with a repeating pattern — the classic `Chapter One .......... 12` table-of-contents idiom. When more than one `leader()` appears on the same line (including across separate elements), the remaining space is shared equally between them, per spec. `open-quote`, `close-quote`, `no-open-quote`, and `no-close-quote` ([CSS 2.1 §12.2](https://www.w3.org/TR/CSS21/generate.html#quotes-insert)) insert a quotation mark from the `quotes` property (below) or adjust the nesting depth without inserting one; PeachPDF's UA stylesheet declares `q::before { content: open-quote }` / `q::after { content: close-quote }`, so a bare `<q>` renders quote marks with no author CSS, matching browsers |
| `quotes` | [quotes](https://developer.mozilla.org/en-US/docs/Web/CSS/quotes) | Pairs of open/close strings consumed by the `open-quote`/`close-quote`/`no-open-quote`/`no-close-quote` `content` keywords above ([CSS 2.1 §12.3.1](https://www.w3.org/TR/CSS21/generate.html#quotes-insert)). The UA default is guillemets (`« »`), not curly quotes. Nesting depth is the count of `open-quote`/`no-open-quote` occurrences minus `close-quote`/`no-close-quote` occurrences preceding a given occurrence in document order (independent of the source document's own element nesting); a depth beyond the number of declared pairs repeats the last pair, and an unmatched `close-quote`/`no-close-quote` is ignored (rather than going negative) while the rest of `content` still inserts. `quotes: none` suppresses the glyphs while still tracking depth |
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
| `@font-palette-values` | Supported ([CSS Fonts Module Level 4](https://developer.mozilla.org/en-US/docs/Web/CSS/@font-palette-values)). Defines a named custom palette (a `<dashed-ident>`) that [`font-palette`](#fonts) can select for a `COLR`/`CPAL` color font. Descriptors: **`font-family`** (required — the family the palette applies to, matched against the element's used family), **`base-palette`** (a palette index, or the `light`/`dark` keywords), and **`override-colors`** (a comma-separated list of `<index> <color>` pairs that replace individual palette entries). A rule with no `font-family` is ignored |
| `@font-feature-values` | Supported ([CSS Fonts Module Level 4](https://developer.mozilla.org/en-US/docs/Web/CSS/@font-feature-values)). Associates named OpenType feature-value aliases with one or more font families (a `<family-name>#` prelude, e.g. `@font-feature-values Font One, "Font Two" { … }`), so [`font-variant-alternates`](#fonts) can reference a readable name instead of a raw feature index. The block holds one or more nested `@styleset`/`@character-variant`/`@swash`/`@ornaments`/`@annotation`/`@stylistic` rules, each a list of `<custom-ident>: <integer>+;` declarations (e.g. `@styleset { nice-style: 1 7; }` — a `styleset()` name may list several integers, turning on that many numbered features at once). A name is scoped to the block kind and family list it was declared under; a later rule declaring the same `(name, family)` pair for the same block kind overrides an earlier one. HTML text only — an inline or standalone SVG `<text>`/`<tspan>` does not consult this registry (applies only to HTML text) |
| `@page` | Full support; see [CSS Paged Media](#css-paged-media) below |
| `@property` | Supported ([CSS Properties & Values API Level 1](https://developer.mozilla.org/en-US/docs/Web/CSS/@property)). Registers a typed custom property with its three descriptors. **`initial-value`** supplies the value a `var()` reference resolves to when the property is otherwise unset (so a palette or default defined only via `@property` works with no author declaration). **`inherits`** governs propagation: `inherits: false` means a descendant that doesn't set the property resolves it to the `initial-value` rather than the parent's value. **`syntax`** is validated at computed-value time — a set value that doesn't match the declared syntax falls back to the `initial-value` — for the single-type grammars `<length>`, `<number>`, `<integer>`, `<percentage>`, `<length-percentage>`, `<color>`, `<angle>`, `<ratio>`, `<image>`, `<url>`, `<time>`, `<resolution>`, `<transform-function>`, `<transform-list>`, `<custom-ident>`, `<string>`, ident literals, the universal `*`, `\|` alternation, and the `+`/`#` list multipliers. A `syntax` naming an unsupported data type is invalid and the whole rule is ignored. `calc()` is accepted in a numeric slot when its resolved type matches (e.g. `calc(50%)` for `<length-percentage>` but not `<length>`); an `initial-value` `calc()` must additionally be [computationally independent](https://developer.mozilla.org/en-US/docs/Web/CSS/@property/initial-value) — it may use absolute lengths, numbers, angles, times, resolutions, and percentages, but not font-relative (`em`/`rem`/`ex`) or viewport lengths. `<image>` accepts `url()`, gradients, and `image-set()`/`cross-fade()`/`element()` (the latter three are validated for registration but not rendered). The registry also applies to SVG styling (see [SVG support](supported-svg-features.md)): an inline `<svg>` uses the host document's registrations, and a standalone SVG uses `@property` rules from its own `<style>` |
| `@media` | Supported; `print` and `all` media types apply, `screen` is ignored, and **feature queries** (`min-width`/`max-width`/range syntax, `orientation`, `resolution`, `prefers-color-scheme`, …) are evaluated against the page box and renderer characteristics; see [CSS Media Queries](#css-media-queries) below |
| `@keyframes` | Not supported |
| `@supports` | Supported ([CSS Conditional Rules 3/4](https://developer.mozilla.org/en-US/docs/Web/CSS/@supports)). `(property: value)`, `not (…)`, `(…) and (…)`, `(…) or (…)`, and arbitrary nesting are evaluated against PeachPDF's actual implemented property set, not just whether the value parses — `animation-name` parses but reports unsupported (never rendered), while SVG-only properties like `fill`/`stroke` correctly report supported. A shorthand is supported only when the CSS-OM expands it into longhands that are all themselves supported, not merely by recognizing the shorthand's name. The five CSS-wide keywords (`inherit`/`initial`/`unset`/`revert`/`revert-layer`) are always accepted. Two known, narrow gaps: a property with no dedicated grammar falls back to CSS-OM parse-validity rather than a full layout/paint-verified check, and a few hand-dispatched properties (`mask`, `marker`/`marker-start`/`marker-mid`/`marker-end`, SVG `clip-path`) aren't covered by this oracle and report unsupported despite working. Any at-rule may appear inside a true-condition block and is collected normally |
| `@container` | Supported ([CSS Containment 3](https://developer.mozilla.org/en-US/docs/Web/CSS/@container)). Size queries (`@container (min-width: 300px) { … }`) and `style()` queries (`@container style(--theme: dark) { … }`) are both evaluated, against the nearest ancestor query container's real resolved size/style — see [CSS Container Queries](#css-container-queries) below for the full support table and behavior |
| `@layer` | [Cascade layers](https://developer.mozilla.org/en-US/docs/Web/CSS/@layer) are supported. Both the block form (`@layer name { … }`, including anonymous `@layer { … }`) and the statement form (`@layer a, b, c;`, which declares layer order) are parsed and honored. Layer precedence follows CSS Cascade 5: for **normal** declarations an unlayered rule beats any layered rule, and among layers a later-declared layer beats an earlier one — this ordering is applied **ahead of specificity**, so a low-specificity rule in a later layer wins over a high-specificity rule in an earlier layer. Specificity then source order break ties within a single layer. For **`!important`** declarations the layer order **reverses**: an earlier layer beats a later one, and a layered `!important` rule beats an unlayered one. **Nested layers** are ordered as a tree — a parent layer's whole subtree is contiguous and its sub-layers are ordered by first appearance within that parent (so `@layer a.b; @layer c; @layer a.d;` ranks `a.b`, `a.d`, then `c`). An `@font-face`/`@property`/`@page` rule nested inside an `@layer` block (or an `@media`/`@supports` within one) is collected. Remaining simplification: when a single layer mixes direct rules with its own nested sub-layers, the parent's direct rules are ranked before those sub-layers (the exact interleaving is not modeled) |
| `@import` | Full support; the imported stylesheet is fetched and its rules merged in place, including transitively nested `@import`s (with circular-import protection). Relative `url()` references inside an imported stylesheet — including `@font-face src` — resolve against that stylesheet's own location, not the document's |

---

## CSS Selectors

PeachPDF evaluates a subset of CSS selectors. Selectors that are parsed but not implemented are silently ignored — rules using them will not apply.

CSS comments (`/* ... */`) are supported anywhere in a stylesheet, including between selectors and declarations, and are stripped before parsing.

### Recognized but unmatchable selectors

A selector list is only as valid as its least-understood member: per [CSS Selectors 3 §4](https://www.w3.org/TR/selectors-3/#Conformance), an unknown pseudo-class or pseudo-element invalidates the **whole** list it appears in, discarding the declarations for the selectors alongside it too. PeachPDF therefore **recognizes** the selectors it can never match, rather than treating them as unknown — they parse, they select nothing, and the rest of their list still applies:

```css
/* The :root half applies; :host selects nothing. Both declarations survive. */
:root, :host { --brand: #2563eb; --spacing: .25rem; }
```

This covers the state-based pseudo-classes a static PDF has no state for (`:hover`, `:focus`, `:visited`, `:checked`, `:disabled`, `:enabled`, `:valid`, `:user-invalid`, `:autofill`, `:target`, `:open`, `:popover-open`, `:modal`, `:fullscreen`, `:picture-in-picture`, the media-playback set `:playing`/`:paused`/`:seeking`/`:buffering`/`:stalled`/`:muted`/`:volume-locked`, …), the Shadow DOM selectors PeachPDF builds no shadow trees for (`:host`, `:host()`, `:host-context()`, `:defined`, `:state()`, `::part()`, `::slotted()`), the pseudo-elements it generates no box for (`::backdrop`, `::file-selector-button`, `::details-content`, `::selection`, `::target-text`, `::spelling-error`, `::grammar-error`, `::cue`, `::highlight()`, `::view-transition*`), and **any vendor extension** — a leading hyphen ([CSS Syntax 3 §2](https://www.w3.org/TR/css-syntax-3/)) marks a UA-internal box or state, so `::-webkit-file-upload-button`, `::-moz-focus-inner`, `::-webkit-input-placeholder`, `:-moz-focusring` and their kin are all accepted wholesale. `:-webkit-any()`/`:-moz-any()` are the legacy vendor spellings of `:is()` and behave exactly like it.

Pseudo-class and pseudo-element names are matched ASCII case-insensitively, so `:HOVER` and `::Before` are recognized too.

This is deliberately **not** blanket acceptance of anything unknown: a genuine typo (`:bogus-pseudo`) still invalidates its selector list, exactly as it does in a browser.

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

`::before`, `::after`, `::marker`, `::first-letter`, `::first-line`, `::placeholder`, and (css-gcpm-3's) `::footnote-call`/`::footnote-marker` are supported. All other pseudo-elements are parsed but have no effect — see [Recognized but unmatchable selectors](#recognized-but-unmatchable-selectors) for which names are recognized and why that matters for the rest of their selector list.

| Pseudo-element | Notes |
|----------------|-------|
| `::before` | Full support; use with the `content` property |
| `::after` | Full support; use with the `content` property |
| `::marker` | Full support for every property the spec allows on markers — see below |
| `::first-letter` | Full support — see below |
| `::first-line` | Full support for every property CSS2.1 allows — see below |
| `::placeholder` | Styles the hint in an empty interactive text field — see below |
| `::footnote-call` | The in-flow numbered footnote reference; see [Footnotes](#footnotes-float-footnote) |
| `::footnote-marker` | The leading number inside a footnote's own body; see [Footnotes](#footnotes-float-footnote) |
| All others | Parsed but ignored |

Both the single-colon legacy syntax (`:before`, `:after`) and the modern double-colon syntax (`::before`, `::after`) are accepted. `::marker` has no legacy single-colon form, matching the spec.

A pseudo-element is generated on an **element** ([Selectors 4 §3.3](https://developer.mozilla.org/en-US/docs/Web/CSS/Pseudo-elements)), so a blanket rule such as `* ::before { … }` or `.wrapper *::after { … }` reaches the elements inside the subtree and nothing else — a bare text node is not an element and gets no pseudo-element of its own.

**`::marker`** is a real, independently laid-out and painted box (the same as `::before`/`::after`), matching CSS2.1 §12.5.1 / CSS Lists Level 3. It's generated for any element whose *computed* `display` is `list-item` — not just `<li>` — so `<div style="display: list-item">` gets a real marker and numbering too, exactly like a `<li>` would.

- **`content`** — `normal` (the default; the marker shows the automatic bullet/number/image driven by `list-style-type`/`list-style-image` on the list item) or an explicit override: a string, `counter()`/`counters()`, `attr()`, `url()`/gradient (rendered as a real image, same as `content: url()` on `::before`/`::after`), or `none` (no marker at all). An explicit override fully replaces the automatic bullet/number/shape — unlike the automatic case, no `"."` suffix is added, so include any trailing punctuation/spacing in the string yourself.
- **`color`**, **font properties** (`font`, `font-family`, `font-size`, `font-style`, `font-weight`, `font-variant`, to the extent PeachPDF supports them generally) and **`direction`** all take effect on the marker's own glyph/shape, independent of the list item's own styling.
- List numbering (the default, non-overridden case, and any `content: counter(list-item)` override) is backed by the real CSS `list-item` counter (see [Generated Content](#generated-content) below), so both always agree — including with `<ol start>`/`<ol reversed>`/`<li value>` in play.
- An `outside` marker (the default) sits beside its list item's **first** line, so it belongs to the fragmentainer that line is in — a list item whose own text carries on onto the next page, or into the next column of a [multi-column container](#multi-column-layout), keeps its bullet or number where the item starts and gets no second one where it resumes.
- An `outside` marker sits on the **baseline** of its item's first line, not at that item's top edge, so a `::marker { font-size }` override makes the marker bigger without leaving its digits hanging below the text they number — and that first line grows to hold the taller marker, the same way a browser's does (by the marker's ascent only: it hangs outside the item's principal box, so its descender does not push the line down). ([CSS Lists Level 3 §3.5](https://www.w3.org/TR/css-lists-3/) leaves both of these expressly undefined; PeachPDF follows what browsers do.) An item whose content is block-level rather than inline keeps the older placement at the item's own content-box top, which is indistinguishable from the baseline placement unless the marker's font differs from the item's.
- The marker sits beside the item's principal block box whether the item's content is inline or block-level, so `<li><p>…</p></li>` is numbered exactly like `<li>…</li>`, and an item mixing the two (`<li>text<p>…</p></li>`) is numbered once. One limitation: an item whose content is block-level and which sits inside a [multi-column container](#multi-column-layout) may show its marker beside a later column's continuation rather than beside its first line — give such an item inline content if it has to be numbered inside columns.
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

Known narrowing: an inline element on the first line, such as a `<span>`, still sizes the line box from its own font rather than the font it inherits from `::first-line`. So a `::first-line` that makes the text smaller than the paragraph's own font leaves a first line holding a `<span>` as tall as the paragraph's font would make it. The text itself is drawn in the `::first-line` font.

**`::placeholder`** styles the hint drawn in an empty interactive PDF text field. `color`, `opacity`, and font properties such as `font-family`, `font-size`, `font-style`, and `font-weight` affect the baked hint appearance; box-model properties have no useful target because the hint is text inside the field's PDF appearance stream, not an independently laid-out box. PeachPDF's UA style gives an unstyled hint an opaque grey (`#7f7f7f`). Author-specified alpha in `color` or `opacity` uses the normal PDF transparency path: it works in ordinary PDF output and is rejected by `PdfATransparencyGuard` for PDF/A-1, just like translucent text anywhere else, rather than being silently discarded.

```css
input::placeholder { color: #64748b; font-style: italic; }
```

### Pseudo-classes

Because PeachPDF renders a static PDF with no live browsing state, most state-based pseudo-classes are parsed but not evaluated. `:placeholder-shown` is the useful exception: its answer is fixed by the control's source state when the PDF is generated. Structural pseudo-classes are fully supported, including the CSS "An+B" formula.

| Pseudo-class | Notes |
|--------------|-------|
| `:link` | Matches `<a>` elements that have an `href` attribute |
| `:any-link` | The union of `:link` and `:visited`. Since a static PDF has no browsing history, `:visited` never matches, so `:any-link` selects exactly the same elements `:link` does |
| `:root` | Matches the document's root element (the `<html>` element) |
| `:scope` | With no scoping root in play, this is the document's root element — the same element `:root` matches ([Selectors 4 §6.6](https://www.w3.org/TR/selectors-4/#the-scope-pseudo)), so `:scope p` and `:root p` behave identically |
| `:empty` | Matches an element with no children other than white-space-only text. Comments do not count as children, and neither does generated content — an element with a `::before`/`::after`/`::marker` box is still `:empty`. A non-breaking space (`&nbsp;`) *is* content, so it is not. Also evaluated for SVG, in both an inline `<svg>` and a standalone one |
| `:placeholder-shown` | Matches a placeholder-capable `<input>` when the `placeholder` attribute is present and its generation-time `value` is empty. It can style the field itself (for example, a lighter border while empty). The result is not live after the PDF is generated: AcroForm has no selector state that can re-run CSS when the user types. |
| `:lang(C)` | [CSS 2.1 §5.11.4](https://www.w3.org/TR/CSS21/selector.html#lang). Matches an element whose language — the `lang` attribute of the nearest element that has one, checking the element itself before any ancestor — is `C`, or has `C` as an ASCII-case-insensitive hyphen-delimited prefix (`:lang(en)` matches `lang="en"` and `lang="en-US"`, but not `lang="english"`). Only the HTML `lang` attribute is consulted; XML's `xml:lang` is not. Also evaluated for SVG (both inline and standalone) — an inline `<svg>` still sees an enclosing HTML ancestor's `lang` |
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
| `:has(S)` | Matches an element with a descendant matching `S`; `S` may be a comma-separated, forgiving relative-selector list. Each alternative may carry its own leading combinator: `:has(S)`/`:has(> S)`/`:has(+ S)`/`:has(~ S)` match a descendant/direct child/next sibling/any following sibling matching `S`, respectively (e.g. `:has(> .a, + .b)` mixes forms across alternatives) |
| `:-webkit-any(S)`, `:-moz-any(S)` | The legacy vendor spellings of `:is(S)`, with identical behaviour |
| All others | Parsed but not matched — rules are silently ignored |

Known gap: `:nth-column()`/`:nth-last-column()`'s same-row-only limitation described above.

State-based pseudo-classes other than `:link`/`:any-link` and the generation-time `:placeholder-shown` state (`:hover`, `:focus`, `:active`, `:visited`, `:checked`, `:disabled`, etc.) are parsed but not applied — PeachPDF renders a static PDF with no browsing history or interaction state, so `:visited`/`:active` never match by design. Crucially they are *recognized*, so they do not invalidate a selector list they share with a selector that does match; see [Recognized but unmatchable selectors](#recognized-but-unmatchable-selectors) for the full set and for the vendor-extension rule.

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

## CSS Viewport Units

PeachPDF renders to a page, not a browser window, so the "viewport" a viewport-relative unit resolves
against is the PDF page box — the same page-box dimensions [CSS Media Queries](#css-media-queries)' own
`width`/`height` features already use.

```css
.hero {
  width: 100vw;    /* the full page width */
  height: 50vh;    /* half the page height */
  font-size: 5vmin; /* 5% of whichever of width/height is smaller */
}
```

| Unit | Resolves to |
|---|---|
| `vw`, `vi` | 1% of the page box's width |
| `vh`, `vb` | 1% of the page box's height |
| `vmin` | 1% of the smaller of the page box's width and height |
| `vmax` | 1% of the larger of the page box's width and height |

`vi`/`vb` (the logical inline/block axis forms) follow the **root element's own** `writing-mode` (CSS
Values and Units 4 §6.2), the same way [`cqi`/`cqb`](#css-container-queries) follow a query container's
own `writing-mode`: under `horizontal-tb` (the default) `vi`/`vb` are identical to `vw`/`vh`, but under a
`vertical-rl`/`vertical-lr` root element `vi` tracks the page box's **height** and `vb` tracks its
**width** — the inline/block axis actually rotates with the root's own writing mode, matching the [real
vertical-writing-mode layout](#text-layout) supported elsewhere in the engine.

The small (`sv*`), large (`lv*`), and dynamic (`dv*`) viewport variants (`svw`, `svh`, `svi`, `svb`,
`svmin`, `svmax`, and the equivalent `lv*`/`dv*` forms) are all fully supported, and all resolve
identically to their plain counterpart above. Browsers distinguish these because a mobile browser's UI
chrome (address bar, toolbars) can show or hide, changing the usable viewport size — a PDF page has no
such chrome, no scrollbar, and no way to resize once layout starts, so there is only one viewport size to
report.

The font-measured units (`ch`, `ex`, ...) are described under [Font-relative units](#font-relative-units).

---

## CSS Container Queries

Supported ([CSS Containment 3](https://developer.mozilla.org/en-US/docs/Web/CSS/CSS_containment/Container_queries)). An element opts in to being a query container with the `container-type`/`container-name` properties (or the `container` shorthand); `@container` rules then apply based on that container's own resolved size or style rather than the page. Both forms — size queries and `style()` queries — evaluate against the **nearest eligible ancestor** query container, walking up from the matched element; an unnamed query uses the nearest eligible ancestor regardless of its name, and a named query (`@container sidebar (...)`) skips straight to the nearest ancestor declaring that name in its own `container-name`, even if a nearer ancestor is eligible but unnamed.

```css
.card-list {
  container-type: inline-size;
  container-name: cards;
}

/* applies once .card-list's own content-box width reaches 400px */
@container cards (min-width: 400px) {
  .card { display: flex; }
}

/* style queries read a container's own resolved value, most commonly a custom property */
@container style(--theme: dark) {
  .card { background: black; color: white; }
}
```

| Property/at-rule | Notes |
|---|---|
| `container-type` | `normal` (initial — no size containment), `size` (tracks both the inline and block axis), `inline-size` (tracks only the inline axis) |
| `container-name` | `none` (initial) or a space-separated list of `<custom-ident>`s an `@container` rule can name to target this container specifically |
| `container` shorthand | `container: <name> / <type>` — expands to the two longhands above; the `/ <type>` half is optional and resets `container-type` to `normal` when omitted |
| `@container <condition> { }` | Unnamed size query — applies against the nearest ancestor with `container-type: size` or `inline-size` |
| `@container <name> <condition> { }` | Named size query — applies against the nearest ancestor whose own `container-name` includes `<name>` and whose `container-type` is `size`/`inline-size` |
| `width`/`min-width`/`max-width` (and the `min-`/`max-`/range-syntax forms) | Evaluated against the container's own resolved **physical width**, regardless of its `writing-mode` |
| `inline-size` (and the `min-`/`max-`/range-syntax forms) | Evaluated against the container's own resolved **inline-axis** size (CSS Writing Modes 4 §7.1) — identical to `width` under the container's default `horizontal-tb`, but its physical height under `vertical-rl`/`vertical-lr` |
| `height`/`min-height`/`max-height`, `aspect-ratio`, `orientation` | Evaluated against the container's own resolved **physical height**, regardless of its `writing-mode` — **only meaningful against a `container-type: size` container**; against an `inline-size`-only container these never match (it tracks only its own inline axis), regardless of the container's actual physical height |
| `block-size` (and the `min-`/`max-`/range-syntax forms) | Evaluated against the container's own resolved **block-axis** size — identical to `height` under `horizontal-tb`, but its physical width under `vertical-rl`/`vertical-lr`. Same `container-type: size`-only restriction as `height` above |
| `style(<property>: <value>)` | Matches when the nearest eligible ancestor query container's own resolved value for `<property>` equals `<value>`. Unlike size queries, **any** query container is eligible regardless of `container-type` — `container-type: normal` (the initial value, so effectively any element) still qualifies, since a style query needs no layout containment. `and`/`or`/`not` combine multiple `style()` feature checks; a combined operand needs its own parenthesized declaration (`style((--a: 1) and (--b: 2))`), matching `@supports`'s own grammar. Comparison is a trimmed, literal-text match against the container's resolved value — reliable for custom properties (values are opaque, author-controlled text) and for a standard property whose authored value already matches PeachPDF's own canonical serialization (e.g. keyword values like `style(display: block)`), but not full computed-value equivalence (`style(color: red)` won't match a container that resolves to `rgb(255, 0, 0)`) |
| `cqw` | 1% of the nearest ancestor query container's own resolved **physical width**, regardless of its `writing-mode` |
| `cqh` | 1% of the nearest ancestor query container's own resolved **physical height** (only meaningful against a `size` container — `0` against an `inline-size`-only one, which doesn't track that axis), regardless of its `writing-mode`. When the container's own `height` is a percentage whose own base isn't itself resolved yet at the point this is evaluated, this also falls back to `0` rather than the real value — a definite (explicit-length) container `height` is unaffected |
| `cqi` | 1% of the nearest ancestor query container's own resolved **inline-axis** size (CSS Writing Modes 4 §7.1) — identical to `cqw` under the container's default `horizontal-tb`, but its **physical height** under `vertical-rl`/`vertical-lr` (the same `vi`/`vb`-style rotation described under [CSS Viewport Units](#css-viewport-units)). Shares `cqh`'s percentage-height caveat above when the container is vertical (its inline axis is then the physical height) |
| `cqb` | 1% of the nearest ancestor query container's own resolved **block-axis** size (only meaningful against a `size` container) — identical to `cqh` under `horizontal-tb`, but its **physical width** under `vertical-rl`/`vertical-lr`. Shares `cqh`'s percentage-height caveat above when the container is horizontal (its block axis is then the physical height) |
| `cqmin`, `cqmax` | The smaller/larger of `cqi` and `cqb` |
| Container-relative unit with no eligible ancestor container | Falls back to the corresponding small-viewport unit (`cqw`→`svw`, `cqh`→`svh`, `cqi`→`svi`, `cqb`→`svb`, `cqmin`→`svmin`, `cqmax`→`svmax`) — see [CSS Viewport Units](#css-viewport-units) |
| Container-relative unit in `font-size` (e.g. `font-size: 10cqw`) | Resolves to `0` for any element with text content — a font-caching order limitation, not the general no-ancestor-container fallback above; a real ancestor container is found, but its size isn't available yet at the point text content first resolves the font. Works only for the uncommon case of an element with no text content of its own |

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

**Per-page margin variation:** when a pseudo-selector or named-page rule sets `margin-top`, `margin-left`, etc., those values override the base margins for that page. **All four margins are layout-affecting**, per the CSS Paged Media page-box model. Top and bottom overrides give each page its own content-band *height*, so content paginates against those variable bands. Left and right overrides give each page its own content-box *width*: top-level (main-column) block content re-wraps to that page's own measure, because [the edges of the page area act as a containing block for the layout that occurs between page breaks](https://www.w3.org/TR/css-page-3/#page-model). So `@page :first { margin-left: 0 }` gives a first page whose text genuinely flows into the wider measure, and mirrored `@page :left` / `@page :right` margins (for binding gutters) re-wrap each page's text to its own width. Zero is a valid override — `@page :first { margin: 0 }` gives a first page whose content band is the entire physical sheet, enabling a true four-edge full-bleed cover (size the cover element to the full sheet, e.g. `width: 8.5in; height: 11in`). An element ending exactly on a page boundary with a forced `page` break after it continues on the very next page — no blank page is manufactured for an exact-fit cover (css-break-3's forced-break-at-boundary rule). A *directional* break (`left`/`right`/`recto`/`verso`) is the one case that does manufacture a blank page, and only when the following content would otherwise land on the wrong side; see [Directional page breaks](#directional-page-breaks).

Known boundaries of per-page margins:

- **A block spanning a page boundary re-wraps its text mid-way, and its own outer border box now matches.** A single paragraph (or other box holding direct text/inline content) that begins on one page and continues onto a differently-margined (or differently-sized) page has its continuation lines re-wrapped to that later page's own measure, per [css-break-3 §5.1](https://www.w3.org/TR/css-break-3/#fragment-sizing) ("a fragment ... is sized ... using the size of its own fragmentainer") — `text-align: left`/`right`/`center`/`justify` all flush against each line's own (per-page) measure too. The block's own outer border box (its background/border rectangle, and any non-text content sized against its own width) matches this on each page too, catching up to content that already reflowed.
- **Any plain, unconstrained block reflows — not just the main column.** Per-page width applies to an ordinary in-flow block-level box whose entire containing-block chain, up to the page area, is auto-width with no `max-width` clamp — the document's main column (`<html>`/`<body>`), or any plain wrapper `<div>` (or similar) nested arbitrarily deep below it. A percentage `width` resolves against its own containing block's page-aware measure the same way, tracking whichever page it lands on rather than a single value computed once. An explicit-length `width` still doesn't vary by page (correctly — an author's fixed measurement shouldn't), and a `max-width` on a box disqualifies both that box and everything nested inside it, since a capped box can no longer be assumed to genuinely span the page area. A table cell, a flex/grid item, a float, or an absolutely/fixed-positioned box each shares its containing block's width with siblings (or is sized against its own placement) rather than claiming all of it, so none of these reflow, though an absolutely/fixed-positioned box's own **percentage** width still resolves against its (now page-aware) containing block. A multi-column *container's* own width now reflows too — each page it spans lays out its own columns at that page's own width. A table's own width now resolves against whichever page the table itself starts on too, though (unlike multicol) it does not vary further as the table continues across later pages — a table's columns are one shared grid spanning every row, so varying the resolved width per continuation would misalign a row's cells against a sibling row's on a different page of the same table. A flex *container's* own width remains a separate, still-open limitation — see the `size` property section below. Named-page (`page: <name>`) left/right overrides reflow the same way an ordinary page margin override does.
- **The initial containing block's width, like its height, resolves against the first page's own area.** A percentage width (or an absolutely-positioned box's `left`/`right`-filled auto width) that bottoms out at the initial containing block itself resolves against the FIRST page's own content band, per [CSS Paged Media 3 §3](https://www.w3.org/TR/css-page-3/#page-model) — reflecting a `@page :first` margin or `size` override, never a later page's.
- **The `orphans`/`widows` line minimums are applied after the measures settle, and never across a change of measure.** Deciding which page a box is on takes several layouts here, because a box's width depends on the page it landed on last time. A [§5.4](https://www.w3.org/TR/css-break-3/#widows-orphans) correction moves content between pages, so taking one against an assignment that is still settling feeds back into that very question. Both minimums are therefore applied in a final layout, once the assignment has stopped changing. That final layout cannot re-wrap what it moves, so a correction that would move a box onto a page of a *different* measure is declined rather than leaving the box wrapped for the page it left — the line minimum is given up instead. Where the pages involved share a measure, which is the ordinary case, both minimums apply in full.
- **An ordinary percentage-height box already resolves correctly** — against its own containing block's already-computed height, the same ancestor a per-page-width main-column block already resolves against — with no special-casing needed. Only the *true* initial containing block (a box whose own containing block is the root itself — the top of the chain any percentage-height resolution eventually reaches, if nothing definite is found sooner) is a distinct case: **it resolves against the first page's own content band**, per [CSS Paged Media 3 §3](https://www.w3.org/TR/css-page-3/#page-model) ("the edges of the page area on the first page establish the rectangle that is the initial containing block of the document") — reflecting a `@page :first` margin override, but never a later page's, even a differently-margined one reached further into the document. Size a full-bleed element that must span every page with absolute units, not `height: 100%`, since only page 1's own band feeds the ICB.
- **Viewport units (`vw`/`vh`/`vi`/`vb`/`vmin`/`vmax`, and their `sv*`/`lv*`/`dv*` variants) resolve against the document's base/configured page area**, not any per-page override — [CSS Values and Units 4 §6.2](https://developer.mozilla.org/en-US/docs/Web/CSS/CSS_Values_and_Units) defines viewport units against one viewport for the whole rendering, with no per-page concept to vary against.
- **Named-page bands begin at the forced break.** An element whose used `page` name differs from the active one always forces a page break, so a named page's own margins take effect exactly from that fresh page onward (through the element's own content and descendants), and revert to the enclosing page's band once content leaves that subtree — see [Named pages](#named-pages).
- `position: fixed` elements and `background-attachment: fixed` layers keep positioning against the base page box on margin-only-overridden pages (they ride the page's content shift rather than re-resolving against that page's own margins). A `position: fixed` box's own **percentage** `left`/`top`/`height` resolves against each page's own area (CSS2.1 §10.1 / CSS Position 3: "in paged media, the page area of each page") whenever the document mixes physical page sizes (a named/pseudo `@page` rule's own `size`); its percentage **`width`** now resolves against each page's own content-right edge unconditionally — a margin-only override re-wraps it exactly as a size override does — since the initial containing block a fixed box's width is defined against (CSS2.1 §10.1) is itself page-aware (see above). An absolute-length offset or dimension is unaffected either way, since it doesn't depend on the basis at all. A resized fixed box's own frame (its background/border/clip, and any replaced content such as an `<img>`/inline `<svg>`, which has no wrapping algorithm to invalidate) is genuinely correct on every page; its **content is not re-flowed** to the new size — [CSS Position 3 §3](https://www.w3.org/TR/css-position-3/#fixed-positioning) is explicit that user agents must not paginate the content of fixed-positioned boxes, so a non-replaced fixed box's text keeps the wrapping its single, global layout pass produced, which may not fill (or may overflow) its resized frame on a page whose measure differs.
- When content-empty pages are skipped (see pagination), a kept page's own geometry (margins/sheet size) corrects itself to match its renumbered output position — even when a `:first`/`:left`/`:right` rule's margins or `size` genuinely change the page's own content-box width or height, provided the change is confined to a single dimension (width alone, or height alone): the affected page is silently laid out again, once, against the corrected dimension. Two narrower cases still resolve geometry against the underlying page sequence rather than the renumbered output, when a content-empty gap precedes them: a rule that changes **both** the content-box's width and height at once (an asymmetric margin override combined with a `size` override), and any page under an active named page (`page: <name>`).

**Units in `@page` margins:** base and per-page rules resolve margins through the same conversion, so a textually identical margin always produces identical page geometry whether it sits in the base rule or a selector-carrying rule. All absolute units (including spec-correct `px` at 0.75pt — see [Length units](#length-units)), `em`/`ex`/`ch` (against the `@page` context's own `font-size` when a base or matching `@page` rule sets one, per [css-page-3 §7.1](https://www.w3.org/TR/css-page-3/#page-size-prop), else the root element's font — `ex`/`ch` take their `0.5em` fallback here, since a page context has no font to measure; see [Font-relative units](#font-relative-units)), `rem` (always the root element's font), `%` (against the layout page width, for all four sides, per CSS's margin-percentage rule), and `calc()` expressions over those units are supported in both base and per-page rules. Viewport units (`vw`/`vh`/`vmin`/`vmax`, and their logical/small/large/dynamic variants) are not supported in `@page` margins: a page rule defining its own geometry in terms of the viewport is self-referential, so such a declaration is invalid and is dropped ([CSS Syntax error handling](https://www.w3.org/TR/css-syntax-3/#error-handling)), leaving that side at its previously-cascaded value — the base margin for a per-page rule, or the configured (`PdfGenerateConfig`) / UA-default margin for a base rule. Base and per-page rules are fully symmetric here.

**Page-box background:** the `background`/`background-color`/`background-image`/`background-position`/`background-size`/`background-repeat`/`background-attachment` properties are supported directly on `@page` (and per pseudo-selector/named-page rule, cascaded the same per-declaration way `margin`/`size` are):

```css
@page {
  background-color: #5588ff;
}
@page :first {
  background-image: linear-gradient(to bottom, #222, #555);
  background-size: cover;
}
```

Per [css-page-3 §3.1](https://www.w3.org/TR/css-page-3/#painting), a page's layers paint bottom-to-top in a fixed order: **the page box's own background, then the document canvas** (the `<html>`/`<body>` fill described above), **then the page box's own border, then the document's content, then the page-margin boxes**. In practice this means the page background is the one thing an opaque canvas fill (or opaque page content) can completely cover — set a page background to give one page (`@page :first`) or a named page (`@page chapter { ... }`) its own tint or cover image, and leave `<html>`/`<body>` without a background of their own so it actually shows through. `background-origin`/`background-clip` pick between the page box's own border-box, padding-box (the default), and content-box — see [Page-box border and padding](#page-box-border-and-padding) below — the same three areas an ordinary element's or a page-margin box's background already distinguishes; a page background never extends into the page's own margin area (background never extends into any box's margin), so `border-box` is the full sheet only when `margin` is `0`. `background-attachment: fixed` is unaffected by any of this — its own positioning area is always the full physical sheet, matching CSS Paged Media's page-anchored convention for fixed backgrounds.

### Page-box border and padding

Per [css-page-3 §3](https://www.w3.org/TR/css-page-3/#page-model), the page box has the same box model as any other: margin, then optionally border and padding, around the page area (the content area actual content flows into). `border-*` and `padding-*` are supported directly on `@page` (and per pseudo-selector/named-page rule, cascaded per-declaration the same way the page-box background above is):

```css
@page {
  margin: 20mm;
  border: 2pt solid #333;
  padding: 10mm;
}
```

This is **layout-affecting**: the resolved border width plus padding genuinely narrows the content area on every side, exactly as it does for an ordinary element — a document's main content, multi-column layout, footnote areas, and everything else that paginates against the page's content band all reflow into the smaller area. A page-margin box's own position is unaffected — its containing block already spans the full margin-to-margin extent regardless of how that extent subdivides into border/padding/content (css-page-3 §5.3.1's "available width"/height already includes the page box's own border/padding).

`border-*-width` never accepts a percentage, matching ordinary elements. `padding-*` percentages resolve against the page box's own overall sheet dimensions — horizontal against the width, vertical against the height — the most direct reading of [css-page-3 §6](https://www.w3.org/TR/css-page-3/#page-margin-properties)'s wording in the absence of page-box-specific guidance.

A per-page (`:first`/`:left`/`:right`/named-page) border/padding override behaves exactly like a per-page margin override (see [Known boundaries of per-page margins](#page-rule) above): it narrows only the pages that rule applies to, and — like an over-large margin override — an override that would leave no room at all for content on a page falls back to the base margin with no border/padding on that page, rather than stalling pagination.

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

When `@page { size: ... }` is present on the base rule it overrides the `PageSize` or `ManualPageWidth`/`ManualPageHeight` configured via `PdfGenerateConfig`, for the whole document. `size` is also honored on a pseudo-selector (`:first`/`:left`/`:right`) or named-page rule (`@page <name> { size: ... }` combined with `page: <name>` on an element, see [Named pages](#named-pages)) — the page(s) that rule matches get their own genuinely independent physical PDF page (their own `/MediaBox`), letting one document mix page sizes and orientations, e.g. a landscape page for a wide table inside an otherwise-portrait report. Vertical pagination adapts to a differently-sized page's own content band, and main-column text re-wraps to that page's own width the same way it does under a per-page margin override — see [Known boundaries of per-page margins](#page-rule) above, including what still does not (flex/grid containers).

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
| `element(name)` | `content: element(chapter-title)` | The current `position: running(chapter-title)` element, laid out for real and shown complete with its own formatting and descendant elements — not just captured text (contrast with `string()` above). See [Running elements](#running-elements-position-running--element). Not combinable with text/counter/string/image content in the same declaration |
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
| `width` / `min-width` / `max-width` | Controls the width of top/bottom margin boxes; boxes with explicit widths are honoured; remaining space is distributed equally among `auto` boxes. Relative units resolve per [css-page-3 §8](https://www.w3.org/TR/css-page-3/#margin-dimension): `%` against the margin area the box sits in (the content-box width shared by a top/bottom row), `em`/`ex`/`ch` against the box's own computed font size (`ex`/`ch` take their `0.5em` fallback, see [Font-relative units](#font-relative-units)), `rem` against the root. Viewport units (`vw`/`vh`/`vmin`/`vmax`, and their logical/small/large/dynamic variants) have no page context and size the box as `auto` |
| `height` / `min-height` / `max-height` | Controls the height of left/right margin boxes; relative units resolve as for `width`, with `%` against the content-box height shared by a left/right column |
| `margin` / `padding` / `border` (all four edges, and the longhands) | Inset the box's content from its slot in the margin area, per [css-page-3 §5.1](https://www.w3.org/TR/css-page-3/#margin-boxes) — a page margin box is a block-level box that accepts the whole box model, border included. A declared `width`/`height` is the **content-box** dimension, with margin, border and padding all additive on top of it, so `width: 200pt; border: 1pt solid; padding: 0 5pt` occupies 212pt of the row and draws its content 200pt wide. Percentages resolve against the box's own **containing block** per axis, following [css-page-3 §6](https://www.w3.org/TR/css-page-3/#page-properties), which overrides CSS 2.1's uniform width rule here: `left`/`right` against that block's **width**, `top`/`bottom` against its **height**. The containing block ([§5.3.1](https://www.w3.org/TR/css-page-3/#margin-box-layout)) is the content band's width by the page margin's thickness for a top/bottom row, the margin's thickness by the content band's height for a left/right column, and the rectangle where two margins meet for a corner — so on a 40pt top margin, `padding-top: 5%` is 2pt, not 5% of the page width. A **negative margin** is honoured and grows the box past its band, which is how a footer can keep a fixed inset from the page edge on a page margin shallower than that inset; padding and border may not be negative and are clamped to zero. A box padded/bordered past its own extent is skipped rather than drawn inside out. Border paints as four independent edge strokes rather than a mitred box — a margin box never fragments across pages the way an ordinary box can, so it has no shared corner with a neighboring fragment to mitre into; `border-radius` is not supported on a margin box |
| `background` / `background-color` / `background-image` / `background-position` / `background-size` / `background-repeat` / `background-clip` / `background-origin` | Same support as an ordinary element's background (including multi-layer `background-image`, gradients, and `background-attachment: fixed` positioning against the page box), painted before the box's own `content` — so a margin box with no `content` at all can still be a purely decorative band. Unlike `@page`'s own background (see [`@page` rule](#page-rule) above), `background-origin`/`background-clip` genuinely distinguish the box's border-box, padding-box and content-box rects here, since a margin box's own border/padding are real |

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

**Limitation:** named-page activation and reversion are honored for normal block-flow content. A `page` change or reversion among the children of a flex container or table is not — those layout modes position their own children independently of the page-break machinery, so a named page applied deep inside them may not begin (or revert) on a fresh page. Apply `page` to a block-level element in the normal flow for reliable results.

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

### Running elements (`position: running()` / `element()`)

Running elements are [css-gcpm-3](https://www.w3.org/TR/css-gcpm-3/#running-syntax)'s richer alternative to named strings: instead of capturing an element's plain text, `position: running(<custom-ident>)` removes the whole element from normal flow and makes it available to a page margin box via `content: element(<custom-ident>)`, which lays it out for real — complete with its own formatting (fonts, colors, borders) and any descendant elements (a `<span>`, an inline image), not just its text content.

```css
h1.chapter { position: running(chapter-title); }

@page {
  @top-center { content: element(chapter-title); }
}
```

```html
<h1 class="chapter">Chapter One <span style="color: #c00;">— Draft</span></h1>
```

The `<h1>` no longer appears at its original position in the document; instead, its full rendered form — including the red "— Draft" span — appears in the `@top-center` margin box of every page until a later `position: running(chapter-title)` element replaces it.

**`element()` keyword variants** (identical selection rules to `string()` above, applied to the whole element rather than a captured string):

| Keyword | Behavior |
|---------|---------|
| `element(name)` / `element(name, first)` | First `running(name)` element that appears on this page; if none, the last one from a previous page |
| `element(name, last)` | Last `running(name)` element that appears on this page; if none, the last from a previous page |
| `element(name, start)` | Last `running(name)` element that started before this page (running header — the element in effect at the top of the page) |
| `element(name, first-except)` | Empty on the page where `name` is first assigned; otherwise same as `first` |

**Limitations:**
- `position: running()` is honored for elements in normal block flow and for flex/grid/multi-column item children. It is not currently honored on a table row or cell.
- Content painted via `content: element()` does not currently respect its own internal stacking order (`z-index`) among its descendants — it paints in document order. This only matters for a running element that itself contains absolutely-positioned, `z-index`-stacked content, not for ordinary text/image headers and footers.

### Footnotes (`float: footnote`)

[css-gcpm-3](https://www.w3.org/TR/css-gcpm-3/#footnotes) footnotes: give an inline-level element `float: footnote` and PeachPDF pulls it out of normal flow, leaves a numbered in-flow reference in its place, and renders the element's own content in a note area at the bottom of the page the reference landed on. Unlike a fixed-height `@page` margin box, the note area's height is reserved dynamically, based on how many footnotes actually land on a given page.

```html
<p>PeachPDF renders footnotes<sup style="float: footnote">
  This note's content moves to the bottom of this page automatically.
</sup> without any manual positioning.</p>
```

- **Source element** — the element carrying `float: footnote` must be inline-level (`inline`, `inline-block`, `inline-table`, `inline-flex`, or `inline-grid`) — the common case is a `<sup>` or `<span>` reference inside running text. A block-level source is left alone (behaves as `float: none`).
- **Numbering** — footnotes are numbered automatically, in document order, through a real `footnote`
  counter you can control from `@page` and `@footnote`. By default each page restarts at 1, because the
  user-agent stylesheet declares `@page { counter-reset: footnote }` and
  `@footnote { counter-increment: footnote }`; since `counter-reset` is a single property, declaring it
  yourself on an applicable `@page` replaces that default outright:

  | Declared on an applicable `@page` | Effect |
  |---|---|
  | nothing | Each page restarts at 1 (the default) |
  | `counter-reset: none` | Nothing resets the counter — numbering runs continuously through the document |
  | `counter-reset: chapter 1` (any set that omits `footnote`) | Also continuous, for the same reason |
  | `counter-reset: footnote 10` | Each page restarts at 10, so its first note is 11 |

  `@footnote { counter-increment: footnote <n> }` sets the step (default 1); `counter-increment: none`,
  or a set that omits `footnote`, steps by zero so every note on the page shares one number. A reset
  declared on a page carrying no footnotes of its own still takes effect there.
- **The call** (`::footnote-call`) — the numbered in-flow reference left at the footnote's original position. By default it renders as a small superscript number; style it like any other pseudo-element:
  ```css
  ::footnote-call { color: #2563eb; }
  ```
- **The marker** (`::footnote-marker`) — the leading number PeachPDF prepends to the footnote's own body once it's shown in the note area (e.g. "1."). Style it the same way:
  ```css
  ::footnote-marker { font-weight: bold; }
  ```
  Both pseudo-elements support the same style properties `::marker` does (`color`, font properties, `direction`) — see [`::marker`](#pseudo-elements) above. An explicit `content` override on either replaces the automatic number: a literal string, `attr()` or an image all work, and `counter(footnote)`/`counters(footnote, <sep>)` resolve to the live number — including a counter style, e.g. `content: counter(footnote, lower-roman)` for `i`, `ii`, `iii`.
- **The note area** — a thin divider rule above the stacked footnote bodies, in document order.
- **The `@footnote` area rule** — style the note area itself with an `@footnote` block nested inside `@page`, the same way you'd declare `@top-center` or another page margin box:
  ```css
  @page {
    @footnote {
      border-top: 2pt solid #2563eb;
      margin-top: 8pt;
      padding-top: 6pt;
    }
  }
  ```
  `border-top` styles the divider rule (only a solid divider is painted — a declared `border-top-style` other than `none`/`hidden` still paints as a solid line, and `none`/`hidden` removes the divider entirely); `margin-top` is the space above the divider; `padding-top` is the gap between the divider and the first footnote body. Any longhand left undeclared falls back to PeachPDF's own default (a 1pt solid black divider, 4pt above it, 4pt below it) exactly as an ordinary CSS cascade fills in an unset property — declaring only `border-top` still gets the default margin/padding. `height` sets a fixed height for the band the footnote bodies stack in — the area's *content* box, so the divider and its surrounding space are added on top of it. A stack shorter than the declared height still reserves the whole band, with the slack below the last body (exactly as a fixed-height block box top-aligns its content); a taller one overflows it. `height: auto` (the default) sizes the area to its content. `max-height` is also accepted, and refers to the same content box as `height`; on its own (under the default `footnote-policy: auto`) it does not shrink or clip the note area — see `footnote-policy` below for what makes it and an overflowed `height` take effect. Percentages on `height`/`max-height` resolve against the page's own content band, since they are block-axis lengths; percentages on `margin-top`/`padding-top` resolve against the content width, as the CSS box model specifies. `counter-increment` controls how much the footnote counter advances per note — see **Numbering** above. `float` and `column-span` (both present in the spec's own default UA stylesheet for `@footnote`) are parsed and ignored: PeachPDF's note area is always at the bottom of the page and always spans the full content width, so declaring `float: none` or `column-span: none` neither moves nor narrows it.
- **`footnote-display: block | inline | compact`** — how a footnote's body stacks in the note area, set on the `float: footnote` source element itself (the same element `float`/`footnote-policy` are declared on):
  ```css
  sup { footnote-display: compact; }
  ```
  - `block` (initial) — every footnote body starts its own row, stacked in document order (PeachPDF's original, only behavior).
  - `inline` — a body runs alongside the previous one on the same row, wrapping to a new row only when it no longer fits the page's content width. A body whose own content already needs more than one line at the full content width still takes a full-width row of its own (there is no narrower re-wrap of multi-line content).
  - `compact` — PeachPDF places a body inline when it is short enough to fit on a single line at the full content width, and as its own block row otherwise. This is the concrete rule behind the spec's own UA-discretion wording ("the user agent determines whether a given footnote element is placed as a block element or an inline element").
- **`footnote-policy: auto | line | block`** — what happens when a page's note area can't hold everything that landed on it, set the same way as `footnote-display`:
  ```css
  sup { footnote-policy: block; }
  ```
  - `auto` (initial) — no forced break; a note area that doesn't fit overflows, same as today.
  - `block` — forces a page break before the paragraph (containing block) carrying the footnote's call, so both the call and its note move to the next page together.
  - `line` — forces a page break at the start of the specific line carrying the call, moving only that line and what follows in the same paragraph — earlier lines of the paragraph stay on the current page. Widows/orphans are still honored, so the actual break may land on an earlier line than the call's own.

  Both values only react to the note area's fit on the *current* page — its own `@footnote` `max-height`, content overflowing a fixed `@footnote` `height`, or its natural height alone already exceeding the whole page. They don't first check whether the *destination* page will actually have room either. If it doesn't, PeachPDF forces another break from there and keeps retrying, bounded by the same small pass limit the note area's own height-reservation convergence already uses; an unsatisfiable case (e.g. a `max-height` too small for any page) settles wherever that limit is reached rather than searching indefinitely.

- **Column-scoped areas** (`float-reference: column`) — a footnote whose reference lands inside a
  multi-column container can have its note placed at the foot of *that column* rather than the foot of
  the page, using [css-page-floats](https://www.w3.org/TR/css-page-floats-3/)' `float-reference`:
  ```css
  sup { float: footnote; float-reference: column; }
  ```
  Each column then gets its own note area — its own divider, its own width, and its own reserved strip
  that the column's content stops above — and a page can carry both kinds at once, since the property is
  set per footnote rather than per page. The same `@footnote` rule styles both, with percentage lengths
  resolving against the column the area actually occupies.

  Numbering stays page-scoped: `float-reference` decides *where* a note goes and says nothing about
  counters, so two notes in two columns of one page are 1 and 2, not 1 and 1. A `column` reference on a
  footnote that is not inside a multi-column container after all falls back to the page's area, which is
  the spec's own "if the float anchor is not inside a column" rule applied to a footnote.

**Limitations:**
- `float: inline-footnote` is not supported, and is not a CSS feature — it is a PrinceXML extension
  with no equivalent in css-gcpm-3, which defines `footnote` as the only footnote `float` value.
- A column-scoped note area taller than the column it belongs to is declined rather than reserved, and
  overflows the column instead — reserving the whole column would leave no room for the content the
  note is attached to.
- `float: footnote` inside a table cell, flex item, or grid item is untested and not a supported combination in this version.
- A footnote authored inside another footnote's body is inert — it renders as ordinary text rather than becoming a second footnote.
- A footnote body taller than a whole page's content band overflows the note area rather than splitting across pages.
- Links and bookmark-candidate headings inside a footnote body are not collected into the PDF's outline or link annotations.

### Page floats (`float: top`/`bottom`/`top-bottom`/`snap`/`inside`/`outside`)

[CSS Page Floats](https://www.w3.org/TR/css-page-floats-3/) — the module the `css-gcpm-3` floats section
now points to — extends `float` past the inline `left`/`right` edges to the page (or column) itself: a
figure or callout floated to the top or bottom edge of whichever page its source position lands on,
rather than positioned inline with the text around it. This is the CSS-only way to pin content to a
page edge without manual, page-aware absolute positioning.

```html
<div style="float: top; border: 1pt solid; padding: 8pt;">
  This box floats to the top of whichever page it lands on.
</div>
<p>The surrounding text flows around it, starting below the reserved strip.</p>
```

- **`top` / `bottom`** — floats to the block-start or block-end edge of the page the element's own
  (ordinary, block-flow) position falls on. Unlike `left`/`right`, this removes the element from the
  inline formatting context entirely — nothing wraps beside it — and instead reserves room at that
  edge of the page, the same "reserve space, shrink the usable content band" mechanism `float: footnote`
  already uses for its note area (see [Footnotes](#footnotes-float-footnote)). Multiple floats on the
  same edge of the same page stack in document order, the first nearest the flow content.
- **`top-bottom`** — tries `top`; if the float doesn't fit in the room still available at the page's top
  edge, falls back to `bottom`.
- **`snap`** — floats to whichever edge (top or bottom) the element's own natural position is nearer to.
- **`inside` / `outside`** — like `left`/`right`, but resolved against a two-sided document's binding
  rather than a fixed physical side: `inside` is the edge nearest the spine (left on a right-hand/recto
  page, right on a left-hand/verso page — the same page-parity rule `@page :left`/`:right` uses),
  `outside` is the mirror. These two behave exactly like `left`/`right` otherwise — text wraps beside
  them, and every `left`/`right` rule above (shrink-to-fit, containment, overflow) applies unchanged.
- **`top-bottom`/`snap` are this implementation's own interpretation.** No currently published
  specification defines these two keywords' disambiguation rule under this name — they come from the
  historical keyword vocabulary this feature targets, not from a rule this implementation can cite.

**Limitations:**
- A page float taller than the whole page's content band overflows the page rather than splitting
  across pages, the same accepted limitation `float: footnote` and `position: running()` already carry.
- `float-reference: column` is not honored for a page float — it always resolves against the page, even
  inside a multi-column container, and a page float placed directly as a multi-column container's own
  child is not laid out at all.
- Prince's own extensions on top of this keyword set (`-prince-float-reference`, `-prince-float-policy`,
  `top-corner`/`bottom-corner`, `sidenote`/`wide` reference targets, `align-top`/`align-bottom`,
  `inline-footnote`) are not implemented.

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

PeachPDF supports [`calc()`](https://developer.mozilla.org/en-US/docs/Web/CSS/calc), [`min()`](https://developer.mozilla.org/en-US/docs/Web/CSS/min), [`max()`](https://developer.mozilla.org/en-US/docs/Web/CSS/max), and [`clamp()`](https://developer.mozilla.org/en-US/docs/Web/CSS/clamp) anywhere a `<length>`, `<percentage>`, `<angle>`, `<integer>`, or plain `<number>` is accepted — width, height, margin, padding, inset (`top`/`left`/`right`/`bottom`), border-width, border-spacing, border-radius, gap, flex-basis, font-size, line-height, text-indent, the length/number arguments of `transform` functions like `translateX()`/`scale()`, the angle argument of `rotate()`/`skewX()`/`skewY()` and gradient direction angles, the hue component of `hsl()`/`hsla()`, and the `<integer>`-typed properties `z-index`, `order`, and `widows` (e.g. `z-index: calc(3 - 2)`).

```css
.card {
  width: calc(100% - 40px);
  padding: clamp(8px, 5%, 24px);
  transform: rotate(calc(45deg + 10deg));
  margin-left: min(5vw, 10px); /* resolves against the page box - see CSS Viewport Units above */
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
| Viewport units (`vw`/`vh`/`vmin`/`vmax`, and their logical/small/large/dynamic variants) inside a math function | Full | Resolved against the page box, the same basis the plain (non-`calc()`) unit form uses — see [CSS Viewport Units](#css-viewport-units) |
| Container-relative units (`cqw`/`cqi`/`cqb`/`cqmin`/`cqmax`) inside a math function | Full | Resolved against the nearest ancestor query container's own size, the same basis the plain (non-`calc()`) unit form uses — see [CSS Container Queries](#css-container-queries) |
| A math function inside CSS Grid track sizing | Full | A `calc()`/`min()`/`max()`/`clamp()` length resolves as a grid track size — bare, and inside `minmax()`/`fit-content()`/`repeat()` arguments (a wrong-category math function, e.g. an angle, drops the whole track list at parse time) |
| A math function for an `<integer>`-typed property (`z-index`, `order`, `widows`) | Full | Must type-check as a plain `<number>` (a length/percentage/angle-category expression is rejected); the result is rounded to the nearest integer, ties away from zero, and folded to a literal at parse time — the same as a `<number>`-typed property, calc() is resolved eagerly rather than kept symbolic, since an integer-category expression has no relative unit to defer to layout |

Note: `background-position` and `background-size` are not listed in the table above because they're not part of the math-function-specific test matrix — both fully support `calc()` in their length/percentage values (e.g. `background-position: calc(50% - 10px) center`), resolved via the same length parser used everywhere else in this table.

---

## Tagged PDF (PDF/UA) Support

PeachPDF can optionally produce a *tagged* PDF — one with a logical structure tree (`/StructTreeRoot`) exposing the document's headings, paragraphs, lists, tables, links, and images to assistive technology (e.g. screen readers), per ISO 32000-1's tagged-PDF conventions and in the direction of PDF/UA conformance.

Tagging is **off by default** and enabled with a single `PdfGenerateConfig` flag — see [Enabling tagged PDF (PDF/UA) output](usage-examples.md#enabling-tagged-pdf-pdfua-output) in Usage Examples for the configuration snippet and everything that turning it on does (automatic `/Lang` from `<html lang>`, `alt`-attribute `/Alt` entries, `/Link` structure elements cross-referenced with their annotations, and `/Lbl`/`/LBody` list-item splitting). When `EnableTaggedPdf` is left at its default (`false`), none of this runs — output is byte-for-byte the same as if tagging didn't exist in the codebase at all.

This same structure tree is also what the accessible "A" levels of PDF/A conformance (`PdfA1A`/`PdfA2A`/`PdfA3A`) build on — see [Generating PDF/A-conformant output](usage-examples.md#generating-pdf-a-conformant-output) in Usage Examples.

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
| `math` | `Formula` |
| `code`, `kbd`, `samp`, `var` | `Code` |
| `a[href]` | `Link` (a bare `<a>` with no `href` is not a hyperlink and does not default to `Link`) |
| `html`, `body` | `none` (transparent — children attach to the synthetic document root) |

Any tag not listed here (e.g. `<cite>`, `<mark>`, `<time>`) falls through to the `auto` fallback: block-level elements resolve to `Div`, inline-level elements to `Span`.

#### Known limitation — anonymous (CSS-generated) table structure cannot be tag-overridden

A table assembled purely through CSS (`display: table` / `table-row` / `table-cell` on arbitrary elements, rather than real `<table>`/`<tr>`/`<td>` markup) gets its row/cell/group tagging (`TR`/`TH`-or-`TD`/`THead`/`TBody`/`TFoot`) from a hardcoded fallback based on the computed `display` value, **not** from `-peachpdf-pdf-tag-type` — the synthesized anonymous boxes PeachPDF creates to complete the table model have no source HTML element for any selector, author or default stylesheet, to match against. Authors who need override control over table structure tagging (e.g. distinguishing header cells from data cells, which the anonymous fallback cannot do — it always tags anonymous cells `TD`) must use real `<table>`/`<tr>`/`<th>`/`<td>`/etc. markup rather than relying on CSS's table display model to synthesize the structure implicitly.

### MathML Associated Files

When [tagged PDF output](#tagged-pdf-pdfua-support) is enabled, a `<math>` element's own `Formula`
structure element additionally carries the element's original MathML source as a PDF 2.0
([ISO 32000-2](https://www.iso.org/standard/75839.html)) Associated File (`/AF`, §14.13) — the standard
mechanism accessible-math tooling and assistive technology use to recover the exact semantic markup
behind a rendered formula, rather than trying to reconstruct it from drawn glyph positions. The
associated file is:

- Named `formula.mml`, with MIME type `application/mathml+xml`.
- Marked `/AFRelationship /Supplement` — the ISO 32000-2 relationship value for content that supplements
  (rather than replaces) the structure element's own visible content.
- Attached both on the `Formula` structure element itself and indexed in the document's catalog-level
  `/AF` array.

This works regardless of which [`PdfVersion`](usage-examples.md#pdf-20-output) the document targets, but
is only fully spec-conformant under `PdfVersion.Pdf20` — `/AF` on a structure element is a PDF 2.0
addition to ISO 32000-1's structure element dictionary. If an author retargets `<math>`'s own
`-peachpdf-pdf-tag-type` away from `Formula` (e.g. to `P`), the MathML source is still attached to
whichever structure element the override produces. No `/Alt` or `/ActualText` (a spoken-math description)
is synthesized — generating one requires real math-to-speech, which is out of scope; the attached exact
MathML source is itself a complete, spec-conformant accessible representation on its own.

---

## Interactive PDF Forms Support

PeachPDF can optionally produce a *fillable* PDF — real AcroForm fields (ISO 32000-1 §12.7) a reader can type into, check, or select, instead of the default static rendering. There is no W3C CSS spec for this (it's inherently PDF-specific); PeachPDF exposes it as its own CSS extension, the same shape as [`-peachpdf-pdf-tag-type`](#-peachpdf-pdf-tag-type-tagged-pdf-structure-type) above.

Interactive forms are **off by default** and enabled with a single `PdfGenerateConfig` flag — see [Enabling interactive PDF forms](usage-examples.md#enabling-interactive-pdf-forms) in Usage Examples for the configuration snippet. When `EnableInteractivePdfForms` is left at its default (`false`), no `-peachpdf-pdf-form-field*` property is ever read, no AcroForm object is created, and the page's own static rendering is unchanged — output is byte-for-byte the same as if the feature didn't exist in the codebase at all.

Only real `<input>`/`<select>` elements are ever eligible to become a field — `<textarea>`, `<button>`, and any other element are always rendered as static boxes, regardless of the flag. This is a deliberate scope boundary for the initial implementation, not a bug. A `<select multiple>` is likewise not yet supported as a multi-select list box — it becomes a single-value combo box, honoring only the first `selected` `<option>`.

### Field appearance and styling

A field's `border`, `background-color`, `padding`, `color`, and `font` (family, weight, style, size — including a custom `@font-face`) are all ordinary CSS, read from the element exactly like any other box's, and drive the actual widget appearance a reader shows — not just the flag-off static look:

```css
input[type=text] {
  border: 1pt solid #8a2be2;
  background-color: #f5e9ff;
  padding: 4pt 8pt;
  color: #4b0082;
  font: italic 12pt Georgia, serif;
}
```

`input`/`select` get a plain default appearance (a thin solid black border, white background, small horizontal/vertical padding) from PeachPDF's own UA stylesheet when the author sets none of these — the same look every field had before per-field styling existed. A checkbox or radio takes no padding from that default (a browser's own UA stylesheet zeroes it there too) and gets a small margin instead, so an unstyled one is a 13×13px square — or circle — with room around it, the way a browser draws it. `width`/`height` size the *content* box like any other element, so the drawn control is that size plus its own border and padding; give a checkbox or radio equal `width` and `height` and it stays square. A checkbox/radio's circular shape and check-mark/dot glyph are fixed (not stylable via `border-radius` or similar); its border/background/glyph color still follow `border`/`background-color`/`color`.

The value a text or combo-box field draws is enclosed in the `/Tx` marked-content sequence ISO 32000-1 §12.7.3.3 defines for variable text, so a reader that regenerates the field replaces exactly that region and keeps the CSS border and background around it. This is what makes editing a field behave: the previous value disappears instead of staying visible behind the new one, and clearing a field leaves an empty box rather than a ghost of what was there.

Once a user actually starts typing into a text/select field, a reader regenerates its look from `/DA` (PDF's own "default appearance" string) rather than the baked-in widget appearance above — `/DA` always uses the PDF standard Helvetica font (Latin-1/WinAnsi only), regardless of the field's own `font-family`. This is deliberate: PeachPDF, like most PDF generators, embeds only the glyphs a document's text actually used, so a custom embedded font has no glyph ready for a character the user types that never appeared in the original value — Helvetica's complete, no-embedding-needed WinAnsi coverage avoids that failure mode for live editing. A field's *initial* appearance (what the PDF shows before anyone edits it) always uses the real font, including for non-Latin-1 text.

### Form-control attributes

These HTML attributes map to their PDF equivalents on the generated field, with no CSS involved:

| HTML | PDF | Effect in a reader |
|---|---|---|
| `readonly` | `/Ff` ReadOnly | The value can be seen but not changed. |
| `disabled` | `/Ff` ReadOnly + NoExport | Not editable, and not included when the form is submitted — HTML defines a disabled control as both. |
| `required` | `/Ff` Required | The reader flags the field as one that must be filled in before submitting. |
| `maxlength` | `/MaxLen` | Caps how many characters can be typed. Ignored when it is absent, zero, or not a valid integer. |
| `type="password"` | `/Ff` Password | Typing is echoed unreadably, and a prefilled `value` is drawn as asterisks rather than legible text. |
| `placeholder` | `/TU` and the empty field's appearance | Drawn automatically as hint text while the field is empty, matching HTML without a CSS opt-in, and used as the tooltip on hover and the name assistive technology reads in place of the `name` attribute. Style the hint with [`::placeholder`](#pseudo-elements) and the empty field itself with [`:placeholder-shown`](#pseudo-classes). |

A `maxlength` that disagrees with [`-peachpdf-pdf-form-field-comb`](#text-field-sub-settings)
loses to it: a comb field's cell count *is* its maximum length, so the two cannot both be honoured.

`required` is worth one warning, because it looks like a bug the first time you see it: Adobe Acrobat
and Reader draw a **red outline around every required field and keep it there**, filled in or not.
That is the reader's own "Required Fields Highlight Color" preference marking the field as required,
not a validation error about its current value, and it is under each viewer's control rather than the
document's. Use `required` when the field genuinely must be filled in, not as a styling hint.

`type="password"` masks the field's *appearance* only — the `value` attribute still becomes the
field's real `/V`, in readable form, because the document asked for the field to be prefilled with
it. A password that should not be in the PDF should not be in the HTML either.

An applicable `placeholder` is drawn automatically when the field's generation-time value is empty,
as [HTML defines](https://html.spec.whatwg.org/multipage/input.html#the-placeholder-attribute); no
PeachPDF-specific CSS switch is required. The hint is appearance only: `/V` stays empty, so the field
still submits as unfilled. It lives inside the field's replaceable `/Tx` region, which lets a PDF
reader remove it on the first keystroke instead of leaving it behind the user's text. Use
[`::placeholder`](#pseudo-elements) to style the hint and
[`:placeholder-shown`](#pseudo-classes) to style the empty control itself; the latter is evaluated
once during generation because AcroForm cannot re-run CSS after an edit.

### `-peachpdf-pdf-form-field`

| | |
|---|---|
| Initial value | `auto` |
| Applies to | `input`, `select` |
| Inherited | No |
| Percentages | N/A |

```css
/* Force a styled <div>-like custom control to become a checkbox field (only works on a real <input>/<select> - a <div> never becomes a field regardless) */
input.agree-checkbox { -peachpdf-pdf-form-field: checkbox; }

/* Opt an input out of interactive forms even though EnableInteractivePdfForms is on */
input[readonly] { -peachpdf-pdf-form-field: none; }
```

Accepted values (case-insensitive): `auto`, `none`, `text`, `checkbox`, `radio`, `select`.

- **`auto`** (the initial value) — the field kind is inferred from the element: an `<input>` with `type="checkbox"` becomes a checkbox field, `type="radio"` becomes a radio field, a `<select>` becomes a select (combo-box) field, and every other `<input>` type (`text`, `email`, `password`, `number`, `tel`, `url`, `search`, `date`, or no `type` at all) becomes a text field. `type="hidden"`, `"submit"`, `"reset"`, `"button"`, `"image"`, and `"file"` are not text-like and resolve to `none` under `auto`.
- **`none`** — the element never becomes an AcroForm field, however it classifies under `auto`; it keeps its static-box rendering.
- **`text`**, **`checkbox`**, **`radio`**, **`select`** — forces the field kind regardless of the element's own tag/`type` (e.g. an `<input type="text">` forced to `checkbox` still becomes a checkbox field).

Radio buttons sharing the same `name` attribute become one AcroForm field with mutually exclusive states, exactly like an HTML radio group's own mutual-exclusivity rule.

### Text-field sub-settings

Three more longhand properties, meaningful only on a field that resolves to `text` (directly or via `auto`):

| | |
|---|---|
| Initial value | `none` (`-peachpdf-pdf-form-field-comb` also accepts an integer) |
| Applies to | `input` |
| Inherited | No |
| Percentages | N/A |

- **`-peachpdf-pdf-form-field-auto-font-size: auto \| none`** — when `auto`, the field's font size auto-fits its box height instead of using a fixed size.
- **`-peachpdf-pdf-form-field-comb: none \| <integer>`** — divides the field into the given number of evenly spaced character cells (ISO 32000-1's "comb" field), the classic boxed layout for things like a one-character-per-box confirmation code input.
- **`-peachpdf-pdf-form-field-do-not-scroll: auto \| none`** — when `auto`, tells the PDF reader not to scroll the field's text when it overflows the box.

```css
input.confirmation-code { -peachpdf-pdf-form-field-comb: 6; }
```

### Default field-kind inference

| HTML | `-peachpdf-pdf-form-field: auto` resolves to |
|------|------------------------------------------------|
| `select` | `select` |
| `input[type=checkbox]` | `checkbox` |
| `input[type=radio]` | `radio` |
| `input[type=text]`, `input[type=email]`, `input[type=password]`, `input[type=number]`, `input[type=tel]`, `input[type=url]`, `input[type=search]`, `input[type=date]`, `input` (no `type`) | `text` |
| `input[type=hidden]`, `input[type=submit]`, `input[type=reset]`, `input[type=button]`, `input[type=image]`, `input[type=file]` | `none` |
| `textarea`, `button`, any other element | `none` (fixed — cannot be overridden) |

---

## PDF Bookmarks (Outline) Support

PeachPDF automatically generates a PDF outline (the navigable bookmark sidebar most PDF readers show) from a document's headings, per [CSS Generated Content Module Level 3's bookmark properties](https://www.w3.org/TR/css-content-3/#bookmarks). This needs no configuration: `h1`–`h6` get a bookmark by default, and a document with no headings simply produces no outline at all.

### `bookmark-level`

Determines whether an element generates a bookmark, and its nesting depth (`1` is shallowest).

| | |
|---|---|
| Initial value | `none` |
| Applies to | All elements |
| Inherited | No |
| Percentages | N/A |

```css
/* Give every top-level <section>'s heading a bookmark of its own, one level deeper than its
   parent section's heading */
section h1 { bookmark-level: 1; }
section section h1 { bookmark-level: 2; }

/* Exclude a heading from the outline entirely */
h2.no-bookmark { bookmark-level: none; }
```

Accepted values: `none`, or a positive `<integer>` (`1` and up). Nesting is not required to follow a strict hierarchy — a bookmark's parent is the nearest *preceding* bookmark (in document order) with a shallower level, or the outline root if none exists; a level with no shallower ancestor ever declared still generates a valid, root-level bookmark.

### `bookmark-label`

The bookmark's text, as a `<content-list>` — the same string/`counter()`/`attr()`/`content()`/`string()` grammar the [`content`](#generated-content) property accepts (`url()`, gradients, and the quote/`element()` functions are not accepted here, since none of them produce text).

| | |
|---|---|
| Initial value | `content(text)` |
| Applies to | All elements |
| Inherited | No |
| Percentages | N/A |

```css
/* Default: the heading's own rendered text becomes the bookmark label */
h1 { /* bookmark-label: content(text) is the initial value - no rule needed */ }

/* A generated label combining a counter, a literal string and an attribute */
h1 { counter-increment: chapter; bookmark-label: "Chapter " counter(chapter) ": " attr(data-title); }
```

### `bookmark-state`

The bookmark's initial expansion state in the reader's outline panel.

| | |
|---|---|
| Initial value | `open` |
| Applies to | Block-level elements |
| Inherited | No |
| Percentages | N/A |

```css
/* Collapse deep subsection bookmarks by default */
h3 { bookmark-state: closed; }
```

Accepted values: `open`, `closed`.

### `-peachpdf-bookmark-target` (bookmark link target)

By default a bookmark links to the element that generated it. `-peachpdf-bookmark-target` overrides that — this is PeachPDF's own extension (not part of css-content-3, which defines no such property); it follows the same naming convention as [`-peachpdf-pdf-tag-type`](#-peachpdf-pdf-tag-type-tagged-pdf-structure-type) for a PDF-output feature with no real spec-track name.

| | |
|---|---|
| Initial value | `self` |
| Applies to | All elements |
| Inherited | No |
| Percentages | N/A |

```css
/* Point a table-of-contents entry's bookmark at the chapter it lists, not the ToC row itself */
.toc-entry { -peachpdf-bookmark-target: attr(href); }

/* Link straight to an external URL instead of anywhere in this document */
h1.appendix-ref { -peachpdf-bookmark-target: url(https://example.com/appendix); }
```

Accepted values: `self`, `url(<url>)`, or `attr(<attribute-name>)`. A value resolving to `#fragment` targets the element with that `id` in the same document; any other resolved value is treated as an external URL. A target that can't be resolved (e.g. a missing `id`) leaves that bookmark without a destination rather than removing the bookmark itself.

### Default `bookmark-level` mapping

| HTML | `bookmark-level` |
|------|-------------------|
| `h1`–`h6` | `1`–`6` |
| Everything else | `none` (this property's own initial value) |

### Known limitation — bookmarks inside repeated running/margin-box content or repeated table headers/footers are not collected

A heading that only ever appears as [running element](usage-examples.md) content in an `@page` margin box (repeated per page, with no single canonical position) does not get a bookmark, even if it would otherwise have a non-`none` `bookmark-level` — only the main document content tree is walked for bookmark candidates. The same applies to a `<thead>`/`<tfoot>` repeated across a page break: a heading placed there produces no bookmark either.

---

## Unsupported CSS Features

The following CSS features are not supported:

- **Grid: masonry** — the core grid formatting model (tracks, placement, named lines, `grid-template-areas`, auto-flow, alignment, `subgrid`, and the `grid`/`grid-template` mega-shorthands) is supported; see [Grid](#grid). Masonry, named lines inside `repeat()`, and `span <name>` are not
- **Transitions and animations** — `transition`, `animation`, `@keyframes`
- **CSS selectors** — see the [CSS Selectors](#css-selectors) section above for what is and is not supported
