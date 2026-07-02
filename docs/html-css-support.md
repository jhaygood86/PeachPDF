# HTML & CSS Support

PeachPDF renders a subset of the HTML and CSS specifications. This page documents exactly what is and is not supported. Where a feature is only partially supported, the specific gaps are noted.

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
| `a` | [a](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/a) | `href` links are embedded as clickable PDF hyperlinks. Anchor links (`href="#id"`) for in-document navigation are also supported. Elements with `id` or `name` attributes act as anchor targets only |
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
| `img` | [img](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/img) | Full support; images are loaded via the configured network loader. Data URIs are supported |
| `iframe` | [iframe](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/iframe) | Rendered as a placeholder box with a gray border. For YouTube and Vimeo embed URLs, a video thumbnail image is displayed |

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
| `height` | [height](https://developer.mozilla.org/en-US/docs/Web/CSS/height) | Full support |
| `max-width` | [max-width](https://developer.mozilla.org/en-US/docs/Web/CSS/max-width) | Full support, including replaced elements (images), absolutely/fixed positioned boxes, flex items, and table cells (a cell's `max-width` caps its column) |
| `min-width` | [min-width](https://developer.mozilla.org/en-US/docs/Web/CSS/min-width) | Full support, including replaced elements (images), absolutely/fixed positioned boxes, flex items, and table cells (a cell's `min-width` widens its column) |
| `max-height` | [max-height](https://developer.mozilla.org/en-US/docs/Web/CSS/max-height) | Full support, including replaced elements (images), absolutely/fixed positioned boxes, and flex items. When content is taller than `max-height`, the box does not grow to fit it — content overflows past the box's bottom edge (same behavior as `overflow: hidden` elsewhere in this engine) |
| `min-height` | [min-height](https://developer.mozilla.org/en-US/docs/Web/CSS/min-height) | Full support, including replaced elements (images), absolutely/fixed positioned boxes, and flex items |
| `margin` | [margin](https://developer.mozilla.org/en-US/docs/Web/CSS/margin) | Shorthand and all four longhands (`margin-top`, `margin-right`, `margin-bottom`, `margin-left`) are supported |
| `padding` | [padding](https://developer.mozilla.org/en-US/docs/Web/CSS/padding) | Shorthand and all four longhands (`padding-top`, `padding-right`, `padding-bottom`, `padding-left`) are supported |
| `box-sizing` | [box-sizing](https://developer.mozilla.org/en-US/docs/Web/CSS/box-sizing) | `content-box` and `border-box` are supported |

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

### Backgrounds

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `background` | [background](https://developer.mozilla.org/en-US/docs/Web/CSS/background) | Shorthand supported; all longhand components are parsed and applied |
| `background-color` | [background-color](https://developer.mozilla.org/en-US/docs/Web/CSS/background-color) | Full support |
| `background-image` | [background-image](https://developer.mozilla.org/en-US/docs/Web/CSS/background-image) | URL, data URI, and all CSS gradient functions: `linear-gradient()`, `radial-gradient()`, `conic-gradient()`, `repeating-linear-gradient()`, `repeating-radial-gradient()`, and `repeating-conic-gradient()`; all accept multi-stop gradients with absolute-length or percentage stop positions, two-position hard-stop shorthand, color hints, and `rgba()`/alpha transparency; radial gradients support `circle`/`ellipse` shape, `at <position>` centering, explicit length radii, and all four size keywords; conic gradients support `from <angle>` and `at <position>`; all gradient functions support CSS Color Level 4 color-space interpolation (`in oklab`, `in hsl`, `in oklch`, `in lab`, `in lch`, `in srgb-linear`, `in display-p3`, `in xyz`, `in xyz-d50`) with polar hue-interpolation methods (`shorter hue`, `longer hue`, `increasing hue`, `decreasing hue`) |
| `background-position` | [background-position](https://developer.mozilla.org/en-US/docs/Web/CSS/background-position) | Full support |
| `background-size` | [background-size](https://developer.mozilla.org/en-US/docs/Web/CSS/background-size) | Full support; `cover`, `contain`, lengths, and percentages |
| `background-repeat` | [background-repeat](https://developer.mozilla.org/en-US/docs/Web/CSS/background-repeat) | Full support |
| `background-origin` | [background-origin](https://developer.mozilla.org/en-US/docs/Web/CSS/background-origin) | Full support; `border-box`, `padding-box`, `content-box` |
| `background-attachment` | [background-attachment](https://developer.mozilla.org/en-US/docs/Web/CSS/background-attachment) | Parsed and accepted but has no effect |
| `background-clip` | [background-clip](https://developer.mozilla.org/en-US/docs/Web/CSS/background-clip) | Full support; `border-box`, `padding-box`, `content-box` |

### Color & Typography

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `color` | [color](https://developer.mozilla.org/en-US/docs/Web/CSS/color) | Full support including named colors, hex, `rgb()`, `rgba()` |
| `font` | [font](https://developer.mozilla.org/en-US/docs/Web/CSS/font) | Shorthand supported; all components are parsed |
| `font-family` | [font-family](https://developer.mozilla.org/en-US/docs/Web/CSS/font-family) | Full support; generic families (`serif`, `sans-serif`, `monospace`) resolve to system fonts |
| `font-size` | [font-size](https://developer.mozilla.org/en-US/docs/Web/CSS/font-size) | Full support including absolute sizes (`medium`, `large`, etc.), relative sizes (`smaller`, `larger`), lengths, and percentages |
| `font-style` | [font-style](https://developer.mozilla.org/en-US/docs/Web/CSS/font-style) | `normal`, `italic`, `oblique` |
| `font-variant` | [font-variant](https://developer.mozilla.org/en-US/docs/Web/CSS/font-variant) | `normal` and `small-caps` |
| `font-weight` | [font-weight](https://developer.mozilla.org/en-US/docs/Web/CSS/font-weight) | Keyword (`bold`, `normal`, `lighter`, `bolder`) and numeric (`100`–`900`) values |
| `line-height` | [line-height](https://developer.mozilla.org/en-US/docs/Web/CSS/line-height) | Full support |
| `vertical-align` | [vertical-align](https://developer.mozilla.org/en-US/docs/Web/CSS/vertical-align) | `baseline`, `sub`, `super`, `top`, `middle`, `bottom`, and length/percentage values |

### Text Layout

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `direction` | [direction](https://developer.mozilla.org/en-US/docs/Web/CSS/direction) | `ltr` and `rtl` |
| `text-align` | [text-align](https://developer.mozilla.org/en-US/docs/Web/CSS/text-align) | `left`, `right`, `center`, `justify` |
| `text-decoration` | [text-decoration](https://developer.mozilla.org/en-US/docs/Web/CSS/text-decoration) | Shorthand supported |
| `text-decoration-color` | [text-decoration-color](https://developer.mozilla.org/en-US/docs/Web/CSS/text-decoration-color) | Full support |
| `text-decoration-line` | [text-decoration-line](https://developer.mozilla.org/en-US/docs/Web/CSS/text-decoration-line) | `none`, `underline`, `overline`, `line-through` |
| `text-decoration-style` | [text-decoration-style](https://developer.mozilla.org/en-US/docs/Web/CSS/text-decoration-style) | `solid`, `dashed`, `dotted`, `double`, `wavy` |
| `text-indent` | [text-indent](https://developer.mozilla.org/en-US/docs/Web/CSS/text-indent) | Full support |
| `white-space` | [white-space](https://developer.mozilla.org/en-US/docs/Web/CSS/white-space) | `normal`, `nowrap`, `pre`, `pre-wrap`, `pre-line` |
| `word-break` | [word-break](https://developer.mozilla.org/en-US/docs/Web/CSS/word-break) | `normal`, `break-all`, `keep-all` |
| `word-spacing` | [word-spacing](https://developer.mozilla.org/en-US/docs/Web/CSS/word-spacing) | Full support |

### Display & Layout

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `display` | [display](https://developer.mozilla.org/en-US/docs/Web/CSS/display) | `block`, `inline`, `inline-block`, `none`, `flex`, `inline-flex`, `table`, `table-row`, `table-cell`, `table-header-group`, `table-footer-group`, `table-row-group`, `table-column`, `table-column-group`, `table-caption`, `list-item`. `grid` is not supported |
| `position` | [position](https://developer.mozilla.org/en-US/docs/Web/CSS/position) | `static`, `relative`, `absolute`, `fixed` (renders ignoring page margins), `sticky` (treated as `relative` in PDF output since there is no scroll) |
| `float` | [float](https://developer.mozilla.org/en-US/docs/Web/CSS/float) | `left`, `right`, `none` |
| `clear` | [clear](https://developer.mozilla.org/en-US/docs/Web/CSS/clear) | `left`, `right`, `both`, `none` |
| `overflow` | [overflow](https://developer.mozilla.org/en-US/docs/Web/CSS/overflow) | Affects clipping regions; there is no interactive scrolling in PDF output |
| `visibility` | [visibility](https://developer.mozilla.org/en-US/docs/Web/CSS/visibility) | `visible`, `hidden` |
| `z-index` | [z-index](https://developer.mozilla.org/en-US/docs/Web/CSS/z-index) | Full support for positioned elements |

### Flexbox

CSS Flexbox Level 1 (`display: flex` / `inline-flex`) is supported, including multi-line wrapping, all alignment properties, and auto margins on the main axis.

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `flex-direction` | [flex-direction](https://developer.mozilla.org/en-US/docs/Web/CSS/flex-direction) | `row`, `row-reverse`, `column`, `column-reverse` |
| `flex-wrap` | [flex-wrap](https://developer.mozilla.org/en-US/docs/Web/CSS/flex-wrap) | `nowrap`, `wrap`, `wrap-reverse` |
| `flex-flow` | [flex-flow](https://developer.mozilla.org/en-US/docs/Web/CSS/flex-flow) | Shorthand for `flex-direction` + `flex-wrap` |
| `justify-content` | [justify-content](https://developer.mozilla.org/en-US/docs/Web/CSS/justify-content) | `flex-start`, `flex-end`, `center`, `space-between`, `space-around`, `space-evenly` |
| `align-items` | [align-items](https://developer.mozilla.org/en-US/docs/Web/CSS/align-items) | `flex-start`, `flex-end`, `center`, `stretch`, `baseline` (aligns items by their first text baseline; only meaningful for row-direction flex — column-direction flex falls back to `flex-start`) |
| `align-content` | [align-content](https://developer.mozilla.org/en-US/docs/Web/CSS/align-content) | `flex-start`, `flex-end`, `center`, `space-between`, `space-around`, `space-evenly`, `stretch` |
| `align-self` | [align-self](https://developer.mozilla.org/en-US/docs/Web/CSS/align-self) | Same values as `align-items`, plus `auto` |
| `order` | [order](https://developer.mozilla.org/en-US/docs/Web/CSS/order) | Full support |
| `flex-grow` | [flex-grow](https://developer.mozilla.org/en-US/docs/Web/CSS/flex-grow) | Full support |
| `flex-shrink` | [flex-shrink](https://developer.mozilla.org/en-US/docs/Web/CSS/flex-shrink) | Full support |
| `flex-basis` | [flex-basis](https://developer.mozilla.org/en-US/docs/Web/CSS/flex-basis) | Length, percentage, `auto`, and `content` values are supported |
| `flex` | [flex](https://developer.mozilla.org/en-US/docs/Web/CSS/flex) | Shorthand for `flex-grow`, `flex-shrink`, `flex-basis`, including the `none` and `auto` keywords |
| `gap` / `row-gap` / `column-gap` | [gap](https://developer.mozilla.org/en-US/docs/Web/CSS/gap) | Full support on flex containers |
| `margin` auto values | [margin](https://developer.mozilla.org/en-US/docs/Web/CSS/margin) | `auto` on a main-axis margin absorbs free space on that line (e.g. `margin-left: auto` to push a flex item to the end) |

### Positioning

Used with `position: relative`, `absolute`, or `fixed`.

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
| `list-style-type` | [list-style-type](https://developer.mozilla.org/en-US/docs/Web/CSS/list-style-type) | `disc`, `circle`, `square`, `decimal`, `lower-alpha`, `upper-alpha`, `lower-roman`, `upper-roman`, `none` |
| `list-style-position` | [list-style-position](https://developer.mozilla.org/en-US/docs/Web/CSS/list-style-position) | `inside`, `outside` |
| `list-style-image` | [list-style-image](https://developer.mozilla.org/en-US/docs/Web/CSS/list-style-image) | URL, data URI, and all CSS gradient functions supported; same image types as `background-image` |

### Page Breaks

These properties control how content breaks across PDF pages. Both the legacy `page-break-*` names and the modern `break-*` names are recognized.

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `break-before` / `page-break-before` | [break-before](https://developer.mozilla.org/en-US/docs/Web/CSS/break-before) | `auto`, `always`, `page`, `avoid` |
| `break-after` / `page-break-after` | [break-after](https://developer.mozilla.org/en-US/docs/Web/CSS/break-after) | `auto`, `always`, `page`, `avoid` |
| `break-inside` / `page-break-inside` | [break-inside](https://developer.mozilla.org/en-US/docs/Web/CSS/break-inside) | `auto`, `avoid` |

### Tables

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `empty-cells` | [empty-cells](https://developer.mozilla.org/en-US/docs/Web/CSS/empty-cells) | `show`, `hide` |

### Generated Content

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `content` | [content](https://developer.mozilla.org/en-US/docs/Web/CSS/content) | Used with `::before` / `::after` pseudo-elements; string, counter, `attr()`, and `none` values supported; `url()` and all CSS gradient functions (`linear-gradient`, `radial-gradient`, `conic-gradient`, and repeating variants) are supported — image values require `display: inline-block` with explicit `width`/`height` on the pseudo-element |
| `counter-reset` | [counter-reset](https://developer.mozilla.org/en-US/docs/Web/CSS/counter-reset) | Full support |
| `counter-increment` | [counter-increment](https://developer.mozilla.org/en-US/docs/Web/CSS/counter-increment) | Full support |
| `counter-set` | [counter-set](https://developer.mozilla.org/en-US/docs/Web/CSS/counter-set) | Full support |
| `string-set` | [string-set](https://developer.mozilla.org/en-US/docs/Web/CSS/string-set) | CSS Paged Media property for running headers/footers |
| `page` | [page](https://developer.mozilla.org/en-US/docs/Web/CSS/page) | Activates a named `@page` rule for pages containing the element |

---

## CSS At-Rules

| At-rule | Notes |
|---------|-------|
| `@font-face` | Full support; see [Fonts](index.md#fonts) |
| `@page` | Full support; see [CSS Paged Media](#css-paged-media) below |
| `@media` | Partial support; `print` and `all` media types apply, `screen` is ignored; see [CSS Media Queries](#css-media-queries) below |
| `@keyframes` | Not supported |
| `@supports` | Not supported |
| `@layer` | Not supported |
| `@import` | Not supported |

---

## CSS Selectors

PeachPDF evaluates a subset of CSS selectors. Selectors that are parsed but not implemented are silently ignored — rules using them will not apply.

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

### Pseudo-elements

Only `::before` and `::after` are supported. All other pseudo-elements are parsed but have no effect.

| Pseudo-element | Notes |
|----------------|-------|
| `::before` | Full support; use with the `content` property |
| `::after` | Full support; use with the `content` property |
| All others | Parsed but ignored |

Both the single-colon legacy syntax (`:before`, `:after`) and the modern double-colon syntax (`::before`, `::after`) are accepted.

### Pseudo-classes

Because PeachPDF renders a static PDF with no interactive or dynamic state, almost all pseudo-classes are parsed but not evaluated and will not match any elements.

| Pseudo-class | Notes |
|--------------|-------|
| `:link` | Matches `<a>` elements that have an `href` attribute |
| `:nth-child(an+b)` | Partially supported: the step value `a` is ignored; only the offset `b` is checked against the element's position among siblings |
| All others | Parsed but not matched — rules are silently ignored |

State-based pseudo-classes (`:hover`, `:focus`, `:active`, `:checked`, `:disabled`) and structural pseudo-classes (`:first-child`, `:last-child`, `:nth-of-type()`, `:not()`, etc.) are not applied.

---

## CSS Media Queries

PeachPDF renders to PDF, so only media queries that target the `print` medium (or the universal `all` medium) are evaluated. Rules inside `@media screen` are ignored entirely, which lets web stylesheets that separate screen and print styles work correctly out of the box.

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
| Media features (`min-width`, `color`, etc.) | No | Features are parsed but not evaluated; the block is treated as if the feature condition were met |

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

**Per-page margin variation:** when a pseudo-selector or named-page rule sets `margin-top`, `margin-left`, etc., those values override the base margins for that page at render time. The content layout is computed once using the base margins, so changing left/right margins shifts the content position but does not reflow text to a different width.

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
| Explicit lengths | `210mm 297mm`, `595pt 842pt` | Any two CSS length values; units: `pt`, `px`, `in`, `cm`, `mm`, `pc` |

When `@page { size: ... }` is present it overrides the `PageSize` or `ManualPageWidth`/`ManualPageHeight` configured via `PdfGenerateConfig`.

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
| `width` / `min-width` / `max-width` | Controls the width of top/bottom margin boxes; boxes with explicit widths are honoured; remaining space is distributed equally among `auto` boxes |
| `height` / `min-height` / `max-height` | Controls the height of left/right margin boxes |

### Named pages

The CSS `page` property on an element activates a named `@page` rule for all PDF pages that contain that element. This lets different parts of a document use different page styles (e.g., wider margins for an appendix, or a different running header for each chapter).

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
| `page: <ident>` | Activates `@page <ident> { }` for pages containing this element |

If multiple elements with different `page` values appear on the same page, the last one in document order wins.

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
| `revert-layer` | Without `@layer` support, behaves identically to `revert`. |

All five keywords can be combined with `!important`.

---

## Unsupported CSS Features

The following CSS features are not supported:

- **Grid** — `display: grid` and all grid properties
- **Transforms** — `transform`, `transform-origin`
- **Transitions and animations** — `transition`, `animation`, `@keyframes`
- **Filters and effects** — `filter`, `backdrop-filter`, `mix-blend-mode`, `opacity`
- **CSS variables** — `var()` and `--custom-properties`
- **`calc()` expressions**
- **`background` shorthand** — use individual `background-*` properties
- **`letter-spacing`**
- **`text-transform`** — `uppercase`, `lowercase`, `capitalize`
- **`text-shadow`**
- **`word-wrap` / `overflow-wrap`**
- **`outline`** and `outline-*` properties
- **CSS selectors** — see the [CSS Selectors](#css-selectors) section above for what is and is not supported
- **Responsive design** — media queries and viewport units (`vw`, `vh`, etc.)
