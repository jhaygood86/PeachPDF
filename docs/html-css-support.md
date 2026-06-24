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
| `meta` | [meta](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/meta) | Ignored at render time |
| `title` | [title](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/title) | Ignored; the PDF document title must be set via `PdfGenerateConfig` |

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
| `max-width` | [max-width](https://developer.mozilla.org/en-US/docs/Web/CSS/max-width) | Full support |
| `min-height` | [min-height](https://developer.mozilla.org/en-US/docs/Web/CSS/min-height) | Full support |
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

### Border Radius (PeachPDF Extension)

PeachPDF uses custom property names for border radius rather than the standard `border-radius`. Standard `border-radius` is not recognized.

| Property | Equivalent standard CSS | Notes |
|----------|------------------------|-------|
| `corner-radius` | [`border-radius`](https://developer.mozilla.org/en-US/docs/Web/CSS/border-radius) | Sets all four corners |
| `corner-nw-radius` | [`border-top-left-radius`](https://developer.mozilla.org/en-US/docs/Web/CSS/border-top-left-radius) | Top-left corner |
| `corner-ne-radius` | [`border-top-right-radius`](https://developer.mozilla.org/en-US/docs/Web/CSS/border-top-right-radius) | Top-right corner |
| `corner-se-radius` | [`border-bottom-right-radius`](https://developer.mozilla.org/en-US/docs/Web/CSS/border-bottom-right-radius) | Bottom-right corner |
| `corner-sw-radius` | [`border-bottom-left-radius`](https://developer.mozilla.org/en-US/docs/Web/CSS/border-bottom-left-radius) | Bottom-left corner |

### Backgrounds

The `background` shorthand is not supported. Use individual properties.

| Property | MDN Reference | Notes |
|----------|--------------|-------|
| `background-color` | [background-color](https://developer.mozilla.org/en-US/docs/Web/CSS/background-color) | Full support |
| `background-image` | [background-image](https://developer.mozilla.org/en-US/docs/Web/CSS/background-image) | URL, data URI, and `linear-gradient()` supported; `radial-gradient()`, `repeating-linear-gradient()`, and other gradient functions are parsed but not rendered |
| `background-position` | [background-position](https://developer.mozilla.org/en-US/docs/Web/CSS/background-position) | Full support |
| `background-repeat` | [background-repeat](https://developer.mozilla.org/en-US/docs/Web/CSS/background-repeat) | Full support |
| `background-attachment` | [background-attachment](https://developer.mozilla.org/en-US/docs/Web/CSS/background-attachment) | Parsed and accepted but has no effect |
| `background-clip` | [background-clip](https://developer.mozilla.org/en-US/docs/Web/CSS/background-clip) | Parsed and accepted but has no effect |

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
| `display` | [display](https://developer.mozilla.org/en-US/docs/Web/CSS/display) | `block`, `inline`, `inline-block`, `none`, `table`, `table-row`, `table-cell`, `table-header-group`, `table-footer-group`, `table-row-group`, `table-column`, `table-column-group`, `table-caption`, `list-item`. `flex` and `grid` are not supported |
| `position` | [position](https://developer.mozilla.org/en-US/docs/Web/CSS/position) | `static`, `relative`, `absolute`, `fixed` (renders ignoring page margins), `sticky` (treated as `relative` in PDF output since there is no scroll) |
| `float` | [float](https://developer.mozilla.org/en-US/docs/Web/CSS/float) | `left`, `right`, `none` |
| `clear` | [clear](https://developer.mozilla.org/en-US/docs/Web/CSS/clear) | `left`, `right`, `both`, `none` |
| `overflow` | [overflow](https://developer.mozilla.org/en-US/docs/Web/CSS/overflow) | Affects clipping regions; there is no interactive scrolling in PDF output |
| `visibility` | [visibility](https://developer.mozilla.org/en-US/docs/Web/CSS/visibility) | `visible`, `hidden` |
| `z-index` | [z-index](https://developer.mozilla.org/en-US/docs/Web/CSS/z-index) | Full support for positioned elements |

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
| `list-style-image` | [list-style-image](https://developer.mozilla.org/en-US/docs/Web/CSS/list-style-image) | URL values supported |

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
| `content` | [content](https://developer.mozilla.org/en-US/docs/Web/CSS/content) | Used with `::before` / `::after` pseudo-elements; string, counter, and `none` values supported |
| `counter-reset` | [counter-reset](https://developer.mozilla.org/en-US/docs/Web/CSS/counter-reset) | Full support |
| `counter-increment` | [counter-increment](https://developer.mozilla.org/en-US/docs/Web/CSS/counter-increment) | Full support |
| `counter-set` | [counter-set](https://developer.mozilla.org/en-US/docs/Web/CSS/counter-set) | Full support |
| `string-set` | [string-set](https://developer.mozilla.org/en-US/docs/Web/CSS/string-set) | CSS Paged Media property for running headers/footers |

---

## CSS At-Rules

| At-rule | Notes |
|---------|-------|
| `@font-face` | Full support; see [Fonts](index.md#fonts) |
| `@media` | Not supported |
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

## Unsupported CSS Features

The following CSS features are not supported:

- **Flexbox** — `display: flex` and all flex properties (`flex-direction`, `align-items`, `justify-content`, etc.)
- **Grid** — `display: grid` and all grid properties
- **Transforms** — `transform`, `transform-origin`
- **Transitions and animations** — `transition`, `animation`, `@keyframes`
- **Filters and effects** — `filter`, `backdrop-filter`, `mix-blend-mode`, `opacity`
- **CSS variables** — `var()` and `--custom-properties`
- **`calc()` expressions**
- **`border-radius`** — use the [PeachPDF extension properties](#border-radius-peachpdf-extension) instead
- **`background` shorthand** — use individual `background-*` properties
- **`letter-spacing`**
- **`text-transform`** — `uppercase`, `lowercase`, `capitalize`
- **`text-shadow`**
- **`word-wrap` / `overflow-wrap`**
- **`outline`** and `outline-*` properties
- **`max-height`**, **`min-width`**
- **CSS selectors** — see the [CSS Selectors](#css-selectors) section above for what is and is not supported
- **Responsive design** — media queries and viewport units (`vw`, `vh`, etc.)
