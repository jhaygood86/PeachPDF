# PeachPDF Architecture

PeachPDF converts HTML and CSS into PDF documents entirely within .NET, with no external process dependencies. The pipeline passes through seven distinct phases: HTML parsing, DOM construction, CSS parsing, stylesheet application, layout, painting, and PDF rendering. SVG content is a cross-cutting subsystem that plugs into the DOM, layout, and painting phases — see [SVG Rendering](#svg-rendering) below.

```
HTML Input
    │
    ▼
HTML Parsing (MimeKit)
    │
    ▼
DOM Construction (CssBox tree)
    │
    ▼
CSS Parsing (ExCSS fork)
    │
    ▼
Stylesheet Application
    │
    ▼
Layout (CssLayoutEngine)
    │
    ▼
Painting (RGraphics)
    │
    ▼
PDF Rendering (PdfSharpCore fork)
    │
    ▼
PDF Output
```

## Lineage

The core rendering engine originally derived from [HtmlRenderer](https://github.com/ArthurHub/HTML-Renderer). Since then it has been substantially rewritten: modern CSS standards are much better supported, and features not relevant to static PDF output (interactive elements, JavaScript hooks, scroll handling) have been removed.

---

## Resource Loading

**Key types:** `RNetworkLoader` ([Network/RNetworkLoader.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/Network/RNetworkLoader.cs)), `RNetworkResponse` ([Network/RNetworkResponse.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/Network/RNetworkResponse.cs)), `RUri` ([Network/RUri.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/Network/RUri.cs))

Resource loading is a cross-cutting concern that spans multiple pipeline phases: the HTML document itself must be fetched before parsing begins, external stylesheets are fetched during stylesheet collection, and images are fetched during painting. All three use the same `RNetworkLoader` abstraction, which means the caller controls how every byte of external content enters the rendering pipeline.

### The abstraction

```csharp
public abstract class RNetworkLoader
{
    public abstract Task<string> GetPrimaryContents();
    public abstract Task<RNetworkResponse?> GetResourceStream(RUri uri);
    public abstract RUri? BaseUri { get; }
}
```

| Member | Purpose |
|---|---|
| `GetPrimaryContents()` | Returns the root HTML string. Called once at the start of the pipeline when `null` is passed to `PdfGenerator.GeneratePdf`. |
| `GetResourceStream(RUri)` | Returns a `RNetworkResponse` (stream + HTTP-style headers) for any external resource URI. Called for stylesheets and images. |
| `BaseUri` | The document's base URL. Used to resolve relative URIs in `href`, `src`, and `url()` values. If `null`, relative references are resolved against the file system. |

`RNetworkResponse` is a simple record: `(Stream? ResourceStream, Dictionary<string, string[]>? ResponseHeaders)`. The headers are inspected by `StylesheetLoadHandler` to validate the `Content-Type` is `text/css` before accepting the body as a stylesheet.

`RUri` wraps `System.Uri` with special-case handling for `data:` URIs, which `System.Uri` can parse but not round-trip correctly through `AbsoluteUri`. It also supports constructing absolute URIs from a base+relative pair, which is how the `<base href>` element and `BaseUri` are applied to relative resource references.

### How the pipeline uses it

**HTML document** — when `GeneratePdf` is called with a `null` HTML string, `GetPrimaryContents()` is called to obtain the document. The `HttpClientNetworkLoader` fetches the URL passed to its constructor; the `MimeKitNetworkLoader` extracts `MimeMessage.HtmlBody` from the MHTML archive.

**Stylesheets** — `StylesheetLoadHandler.LoadStylesheet` resolves the `href` on a `<link>` element against `BaseUri` (or the `<base href>` element if present), then:
- For `file://` URIs, opens the file directly from disk.
- For all other URIs, calls `adapter.GetResourceStream(uri)`. Only responses with a `Content-Type: text/css` header are accepted; anything else is silently discarded.

**Images** — `ImageLoadHandler.SetImageFromPath` resolves the image `src` the same way. File paths are opened directly; absolute non-file URIs go through `adapter.GetResourceStream(uri)`.

**`data:` URIs** — regardless of which `RNetworkLoader` is configured, `data:` URIs are always handled by `DataUriNetworkLoader` directly inside `PdfSharpAdapter.GetResourceStream`. This ensures inline base64 images and stylesheets work without any network loader needing to implement `data:` support.

### Built-in implementations

PeachPDF ships three concrete loaders:

#### `DataUriNetworkLoader` ([Network/DataUriNetworkLoader.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/Network/DataUriNetworkLoader.cs))

The default loader when no `NetworkLoader` is set in `PdfGenerateConfig`. It handles only `data:` URIs, decoding the base64 payload into a `MemoryStream`. Any other URI scheme returns `null`, meaning external resources (remote stylesheets, external images) are silently skipped. This is the safest default for server-side environments where network access must be controlled explicitly.

#### `MimeKitNetworkLoader` ([Network/MimeKitNetworkLoader.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/Network/MimeKitNetworkLoader.cs))

Wraps a MimeKit-parsed MHTML archive. `GetPrimaryContents()` returns `MimeMessage.HtmlBody`. `GetResourceStream(uri)` walks the MIME body parts and finds the part whose `Content-Location` matches the requested URI. This allows a fully self-contained `.mhtml` file to be rendered without any network access: all resources are read from the archive. `BaseUri` is `null` because the base URL is embedded in the document itself.

#### `HttpClientNetworkLoader` ([Network/HttpClientNetworkLoader.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/Network/HttpClientNetworkLoader.cs))

Fetches resources over HTTP using a caller-supplied `HttpClient`. The `primaryContentsUri` passed to the constructor sets `BaseUri` and is used by `GetPrimaryContents()` to download the root HTML. All subsequent resource requests (stylesheets, images) resolve against that base. The caller controls the `HttpClient` lifetime, so custom headers, authentication, proxies, timeouts, and `HttpMessageHandler` chains are all supported without any PeachPDF-specific API.

### Implementing a custom loader

To integrate PeachPDF into an environment with custom resource-resolution logic — for example, reading assets from a cloud blob store, a bundler manifest, or an in-memory dictionary — subclass `RNetworkLoader` and set the instance on `PdfGenerateConfig.NetworkLoader`:

```csharp
public class MyLoader : RNetworkLoader
{
    public override RUri? BaseUri => null;

    public override Task<string> GetPrimaryContents() =>
        Task.FromResult("<html>…</html>");

    public override Task<RNetworkResponse?> GetResourceStream(RUri uri)
    {
        var bytes = MyAssetStore.Load(uri.OriginalString);
        if (bytes is null) return Task.FromResult<RNetworkResponse?>(null);
        return Task.FromResult<RNetworkResponse?>(
            new RNetworkResponse(new MemoryStream(bytes), null));
    }
}
```

---

## 1. HTML Parsing

**Library:** [MimeKit](https://github.com/jstedfast/MimeKit)

**Entry point:** `HtmlParser.ParseDocument` ([Html/Core/Parse/HtmlParser.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/Html/Core/Parse/HtmlParser.cs))

Raw HTML is tokenized by MimeKit's `HtmlTokenizer`, which produces a flat stream of `HtmlTagToken` and `HtmlDataToken` objects. `HtmlParser` walks that stream and builds a `CssBox` tree in a single pass.

### Token handling

The parser maintains a _current box_ cursor that advances up and down the tree as tags open and close:

- **Opening tag** — a new `CssBox` is created as a child of the current box. If the tag is a container element the cursor descends into the new box; if it is a void element (`<br>`, `<img>`, `<hr>`, etc.) the cursor stays at the current level.
- **Closing tag** — the parser calls `DomUtils.FindParent` to walk back up to the matching open box, then moves the cursor there. This makes the parser resilient to mis-nested HTML.
- **Optional end tags** — HTML5 allows certain end tags to be omitted (e.g. `</li>`, `</p>`, `</td>`). `HtmlUtils.CanEndTagBeOmitted` checks the current tag name against the incoming tag name and automatically closes the implied parent before opening the new element.
- **Text data** — a lightweight anonymous `CssBox` is created to hold the raw text string; the text is later tokenised into individual words during the layout phase.
- **Ignored token kinds** — `Comment`, `DocType`, and `ScriptData` tokens are discarded; `CData` content is also silently dropped.
- **`<noscript>`** — its text content is recursively re-parsed as HTML so that the fallback markup is included in the box tree (PeachPDF never executes JavaScript).

### MHTML support

MimeKit also drives MHTML ingestion. `MimeKitNetworkLoader` opens the MIME multipart, extracts the root HTML part, and makes all embedded resources (stylesheets, images, fonts) available by their `Content-Location` URL before the rendering pipeline begins. This means the HTML parser and all subsequent phases see a normal document; the resource-loading layer transparently resolves references into the MIME archive.

---

## 2. DOM Model — CssBox

**Key types:** `CssBox` ([Html/Core/Dom/CssBox.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/Html/Core/Dom/CssBox.cs)), `CssBoxProperties` ([Html/Core/Dom/CssBoxProperties.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/Html/Core/Dom/CssBoxProperties.cs))

Each HTML element becomes a `CssBox`. The tree mirrors the source HTML structure and is the central data structure that all subsequent phases read from and write to. Every box is assigned a monotonically increasing `Id` so boxes can be identified and ordered throughout the pipeline.

### Properties

`CssBox` extends `CssBoxProperties`, which stores a field for every CSS property the engine understands. Properties are stored as raw strings (matching CSS value syntax) and parsed on demand into computed numeric values that are cached in parallel `_actual*` fields. This avoids re-parsing values on every access while keeping the raw strings available for inheritance and `currentColor` resolution.

Key computed values include actual border widths, padding, corner radii, word spacing, text indent, and border spacing — each resolved relative to the containing block width or the current font size.

### Box display and flow

`CssBox` exposes several boolean helpers that the layout and painting phases query constantly:

| Property | Meaning |
|---|---|
| `IsBlock` | `display: block` |
| `IsInline` | `display: inline`, `inline-block`, or `inline-table` |
| `IsFloated` | `float: left` or `float: right` |
| `IsOutOfFlow` | floated, absolutely, or fixed positioned |
| `IsFixed` | this box or any ancestor has `position: fixed` |
| `IsTableRowGroupBox` | `display: table-row-group/header-group/footer-group` |
| `IsTableCell` | `display: table-cell` |

The _containing block_ for a box is found by walking up the parent chain to the nearest block-level, table, or table-cell box.

### Inline content

Text inside a box is held in a `Words` list of `CssRect` objects:

- `CssRectWord` — a single word or whitespace run. Its dimensions are set during word measurement.
- `CssRectImage` — an inline-replaced image whose dimensions are set by `CssLayoutEngine.MeasureImageSize`.

Words are collected into `CssLineBox` instances during layout. After layout, each box records its per-line paint rectangles in a `Dictionary<CssLineBox, RRect> Rectangles` map so the painting phase knows exactly where each fragment of the box appears on each line.

### Specialised subtypes

| Subtype | Purpose |
|---|---|
| `CssBoxImage` | `<img>` — manages an `ImageLoadHandler` to load and decode the image |
| `CssBoxFrame` | `<iframe>` |
| `CssBoxHr` | `<hr>` — renders as a horizontal rule |
| `CssSpacingBox` | Anonymous spacing boxes injected into inline formatting contexts |
| `CssProxyBox` | Wrapper boxes created by the table layout engine to satisfy CSS anonymous box rules |

### Pseudo-elements and generated content

`::before` and `::after` pseudo-elements are represented as real `CssBox` instances with `IsBeforePseudoElement` / `IsAfterPseudoElement` flags set. The `CssContentEngine` evaluates the `content` CSS property on these boxes, resolving string literals, `counter()` references, `string()` named-string lookups, and `attr()` expressions into plain text that is injected as anonymous text boxes.

When the `content` value is an image — `url()` or any CSS gradient function — `CssContentEngine.ApplyContent` sets `CssBox.ContentImage` instead of `CssBox.Text`. The box is kept in the tree as a non-text box; its image is loaded via `EnsureLoadedAsync` during word measurement and painted via `CssImagePainter.Paint` into the box's client rectangle. This uses the same `CssImage` pipeline as `background-image` and `list-style-image`. Image content requires `display: inline-block` with explicit `width`/`height` on the pseudo-element.

CSS counters (`counter-reset`, `counter-increment`) are tracked per-box by `CssCounterEngine`. Named strings (`string-set`) used for running headers and footers are tracked by `CssNamedStringEngine`.

### Pagination metadata

Because the output is paginated, `CssBox` carries a `PageBreakBottoms` dictionary that `CssLayoutEngineTable` populates when a table row group breaks across pages. The painting phase uses this to clip table borders to the actual content height on each page rather than drawing them beyond the page break.

---

## 3. CSS Parsing

**Library:** ExCSS (forked and merged into PeachPDF)

**Entry point:** `CssParser.ParseStyleSheet` ([Html/Core/Parse/CssParser.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/Html/Core/Parse/CssParser.cs))

CSS is parsed by a fork of [ExCSS](https://github.com/TylerBrinks/ExCSS) merged directly into the PeachPDF source tree under `src/PeachPDF/CSS/`. The fork exists for two reasons:

1. **Internal API access.** ExCSS does not expose the internals of a parsed stylesheet through its public API. PeachPDF needs direct access to the parsed token and rule structures to efficiently resolve selectors, apply the cascade, and evaluate property values. Merging the library removes the public-API boundary and gives the rendering engine full access to those internals.

2. **Independent CSS property support.** Adding support for a new CSS property (e.g. `background`, gradients, `container`) requires changes to the parser. With ExCSS as an external dependency this would mean waiting on an upstream release; with the fork merged in, new properties can be added and shipped immediately.

### Rule types

The parsed output is a `Stylesheet` object containing a typed collection of at-rules and style rules. The rule types are defined under [src/PeachPDF/CSS/Rules/](https://github.com/jhaygood86/PeachPDF/tree/main/src/PeachPDF/CSS/Rules/):

| Rule class | CSS construct |
|---|---|
| `StyleRule` (via `IStyleRule`) | Regular selector + declaration block |
| `MediaRule` | `@media` — wraps child rules with a media query |
| `FontFaceRule` | `@font-face` |
| `ImportRule` | `@import` |
| `ContainerRule` | `@container` |
| `KeyframesRule` | `@keyframes` (parsed but not animated) |
| `ViewportRule` | `@viewport` |
| `DocumentRule` | `@document` |

### Property definitions

Every CSS property PeachPDF understands has a corresponding class under [src/PeachPDF/CSS/StyleProperties/](https://github.com/jhaygood86/PeachPDF/tree/main/src/PeachPDF/CSS/StyleProperties/), organised by category (Background, Border, Font, Text, etc.). Each property class declares the property name, whether it is inherited, its initial value, and a value converter that validates and normalises the parsed token stream.

### Value parsing

`CssValueParser` ([Html/Core/Parse/CssValueParser.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/Html/Core/Parse/CssValueParser.cs)) translates raw CSS value strings into the numeric/computed forms that `CssBoxProperties` stores in its `_actual*` fields. It handles length resolution (px, em, rem, %, vw/vh), colour parsing, and shorthand expansion.

---

## 4. Stylesheet Application

**Key types:** `CssData` ([Html/Core/CssData.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/Html/Core/CssData.cs)), `DomParser` ([Html/Core/Parse/DomParser.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/Html/Core/Parse/DomParser.cs))

`DomParser.GenerateCssTree` orchestrates both the HTML parse and the full stylesheet application in a single method call, returning the styled `CssBox` root ready for layout.

### Stylesheet collection

Stylesheets are collected and merged from four sources in cascade order:

1. **User-agent defaults** — `CssDefaults.DefaultStyleSheet` is a string constant that mirrors the [CSS 2.1 default stylesheet for HTML](https://www.w3.org/TR/CSS21/sample.html). It sets `display` values for all HTML structural elements, default margins, font sizes for headings, monospace for `<pre>` and `<code>`, and so on. It is always loaded first.
2. **Caller-supplied stylesheet** — before rendering, the caller can pre-parse a CSS string into a `PeachPdfCssContent` object using `PdfGenerator.ParseStyleSheet(css)` and pass it to `GeneratePdf` as the `cssData` parameter. When provided, this stylesheet becomes the starting `CssData` that document stylesheets are subsequently merged into, making it behave like an additional author stylesheet applied before any document-level styles. The `combineWithDefault` parameter on `ParseStyleSheet` controls whether the caller's stylesheet is merged on top of the W3 user-agent defaults (`true`, the default) or replaces them entirely (`false`).
3. **Author stylesheets** — `CascadeParseStyles` does a depth-first walk of the raw `CssBox` tree and accumulates parsed stylesheets from `<link rel="stylesheet">` and `<style>` elements in document order. External stylesheets are fetched through the configured `RNetworkLoader` (HTTP, file system, or MHTML archive). Each parsed `Stylesheet` is appended to `CssData.Stylesheets`.
4. **Inline styles** — handled per-box during `CascadeApplyStyles` (see below).

### @page rules

Before style cascading begins, `CascadeApplyPageStyles` reads `@page` rules from the collected stylesheets and writes their margin values (`margin-top`, `margin-right`, `margin-bottom`, `margin-left`) onto the `HtmlContainerInt`, overriding any margins specified in the `PdfGenerateConfig`.

### @font-face rules

`CascadeApplyStyleFonts` iterates every `@font-face` rule and resolves each font family name and source. If a `local()` source matches an installed system font it is used directly; otherwise the `url()` source is fetched through the adapter and the font file is loaded into the font subsystem before layout begins.

### Style cascade

`CascadeApplyStyles` applies styles to every box with a recursive tree walk:

1. **Initial values** — `CssDefaults.InitialValues` are written onto the box first so every property has a defined starting point.
2. **Inheritance** — `CssBox.InheritStyle` copies inheritable properties from the parent box.
3. **Matching rules** — `CssData.GetStyleRules` yields every `IStyleRule` from every stylesheet (filtered to the `print` media type) whose selector matches the current box.
4. **`!important` tracking** — property names marked `!important` are recorded in a `HashSet<string>`. Subsequent rules cannot overwrite them.
5. **`inherit` / `initial` keywords** — resolved at assignment time: `inherit` reads the property value from the parent box; `initial` reads from `CssDefaults.InitialValues`.
6. **Custom properties and `var()`** — `--foo` declarations are kept in a per-box dictionary, cloned (not shared) from the parent at inheritance time so a child's local override never leaks to its parent or siblings. Regular declarations whose value contains `var()` are deferred until the box's entire cascade (UA, author, inline) has finished, then resolved in one pass via a graph-based, memoized, cycle-safe substitution against the box's final custom-property values — this makes resolution correct regardless of declaration order and safely short-circuits cyclic references (`--a: var(--b); --b: var(--a);`) instead of looping.
7. **Presentational attributes** — `TranslateAttributes` maps HTML attributes such as `align`, `width`, `border`, `bgcolor`, `valign`, `cellspacing`, and `cellpadding` to their CSS equivalents so they participate in the cascade.
8. **Inline `style` attribute** — parsed on the fly and applied last (with highest author specificity).
9. **`currentColor`** — `CssUtils.ApplyCurrentColor` resolves any `currentColor` keyword references.
10. **Text decoration propagation** — because `text-decoration` does not inherit through the CSS `inherit` mechanism but does visually propagate to inline children, it is explicitly copied down to child boxes that contain actual text.

### Post-styling corrections

After the cascade, `DomParser` runs a series of correction passes to make the tree structurally valid for layout:

| Method | What it fixes |
|---|---|
| `CorrectTextBoxes` | Removes whitespace-only anonymous boxes that cannot affect layout |
| `CorrectImgBoxes` | Ensures `<img>` boxes have the correct display and size constraints |
| `CorrectLineBreaksBlocks` | Wraps `<br>` elements correctly in their inline context |
| `CorrectInlineBoxesParent` | Ensures block children of inline parents get a block wrapper (CSS anonymous block generation) |
| `CorrectAbsolutelyPositionedInlineElements` | Promotes absolutely positioned inlines to block |
| `CorrectBlockInsideInline` | Splits inline boxes that contain block-level descendants, as required by the CSS spec |
| `CorrectAnonymousTables` | Generates missing table wrapper boxes (anonymous `table`, `tbody`, `tr`) to satisfy the CSS table model |

---

## 5. Layout

**Key types:** `CssLayoutEngine` ([Html/Core/Dom/CssLayoutEngine.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/Html/Core/Dom/CssLayoutEngine.cs)), `CssLayoutEngineTable` ([Html/Core/Dom/CssLayoutEngineTable.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/Html/Core/Dom/CssLayoutEngineTable.cs)), `CssLayoutEngineFlex` ([Html/Core/Dom/CssLayoutEngineFlex.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/Html/Core/Dom/CssLayoutEngineFlex.cs))

Layout computes the position and size of every box. It runs in two sub-passes: word measurement and box layout.

### Word measurement

`CssLayoutEngine.MeasureWords` does a depth-first walk of the tree and calls `CssBox.MeasureWordsSize` on each box. This calls `RGraphics.MeasureString` to ask the PDF graphics context for the pixel width of each word using the box's resolved font. Image sizes are resolved by `MeasureImageSize`, which respects `width`, `height`, `min-width`, `max-width`, `min-height`, `max-height`, and aspect-ratio constraints.

### Box sizing constraints (min/max-width, min/max-height)

`min-width`/`max-width`/`min-height`/`max-height` are enforced differently for each axis, because width and height are resolved in opposite directions:

- **Width is resolved top-down, before children lay out.** `CssLayoutEngine.GetBoxWidth` computes a box's width and hard-sets `ActualRight` *before* recursing into its children, so a `max-width`/`min-width` clamp there (max applied first, then min — min wins on conflict per CSS §10.4) correctly constrains how children wrap and position themselves, matching real browsers.
- **Height is resolved bottom-up, after children lay out.** A box's natural height comes from its already-laid-out children. `CssLayoutEngine.ApplyHeight` (called for every box right after its children finish) only *grows* a box to fit `min-height`/explicit `height` via `Math.Max`. `max-height` is applied as a separate, final hard-set step that can shrink `ActualBottom` below the content's natural extent — content simply overflows past the box's bottom edge, the same behavior already used for `overflow: hidden` clipping elsewhere in this engine. `min-height` is re-applied afterward so it wins if it conflicts with `max-height`.
- **Percentages against an indefinite containing block are ignored** (treated as unset) via the box's `IsHeightCalculated` flag, avoiding resolving `min-height`/`max-height`/`height` percentages against a not-yet-known ancestor height.
- These constraints apply uniformly to general blocks, replaced elements (images, via `MeasureImageSize`), absolutely/fixed positioned boxes, and flex items (flex items are additionally clamped on the main axis by `CssLayoutEngineFlex.ClampMainAxis`). Table cells apply `min-width`/`max-width` to the table's auto column-width distribution in `CssLayoutEngineTable`.

### Block formatting context

Block boxes are stacked vertically. Adjacent margins collapse: the larger of the two touching margins wins, with the collapsed value stored in `_collapsedMarginTop`. Auto margins (`margin: auto`) are resolved by `GetActualMarginLeft` / `GetActualMarginRight` to centre block elements horizontally.

### Inline formatting context

`CssLayoutEngine.CreateLineBoxes` breaks inline content into `CssLineBox` instances. Each word is placed onto the current line until it would exceed `ClientRight`, at which point a new line box is started. The algorithm then applies:

- **Horizontal alignment** — `ApplyHorizontalAlignment` shifts words for `text-align: center`, `right`, or `justify`.
- **RTL support** — `ApplyRightToLeft` mirrors word order for right-to-left content.
- **Rectangle bubbling** — `BubbleRectangles` propagates each word's rectangle up through any inline ancestor boxes so that borders and backgrounds can be painted over the correct spans.
- **Vertical alignment** — `ApplyVerticalAlignment` adjusts each word's vertical position within the line for `vertical-align` values (`top`, `middle`, `bottom`, `baseline`, `sub`, `super`, numeric offsets).
- **`overflow: hidden`** — after line boxes are created, if a box has an explicit height and `overflow: hidden`, its `ActualBottom` is clamped to `Location.Y + ActualHeight`.

### Float layout

Floated boxes are removed from normal flow. `CssLayoutEngine.FloatBox` positions them at the left or right edge of their containing block and records their extent in `CssFloatCoordinates` on the container. Subsequent inline content queries these coordinates to wrap around the float. The `clear` property is handled by `ClearBox`, which advances the current Y position past all floats of the specified side.

### Fit-content / min-content / max-content

`GetFitContentWidth`, `GetMinContentWidth`, and `GetMaxContentWidth` implement the intrinsic sizing keywords used by table column widths and `width: fit-content`. They work by measuring all words and recursively summing child widths without line-breaking (max-content) or by finding the longest single word (min-content).

### Flex layout

**Key type:** `CssLayoutEngineFlex` ([Html/Core/Dom/CssLayoutEngineFlex.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/Html/Core/Dom/CssLayoutEngineFlex.cs))

Boxes with `display: flex` or `inline-flex` are laid out by a dedicated engine implementing CSS Flexbox Level 1, entered from `CssBox.PerformLayoutImp` in place of the normal block/inline formatting context. It runs as a sequence of phases per the spec: collect and order items (respecting `order`), measure each item's hypothetical main size from `flex-basis`/`width`/`height` or its content size, wrap items into lines (`flex-wrap`), resolve flexible lengths via `flex-grow`/`flex-shrink` clamped to `min`/`max-width`/`height`, size and align lines on the cross axis (`align-content`), position items on the main axis (`justify-content`, with `auto` margins absorbing free space first), and align items on the cross axis (`align-items`/`align-self`). Flex items are blockified before measurement per spec §9.2. `align-items`/`align-self: baseline` aligns items by their first font baseline — each item's baseline is found by descending into its first in-flow child (in document order) for the first line box, using `RFont.Ascent` for the offset from that box's top — and only applies for row-direction flex; column-direction flex has no vertical baseline concept and falls back to `flex-start`, as does an item with no discoverable line-box content.

### Table layout

`CssLayoutEngineTable` implements the CSS 2.1 fixed and auto table layout algorithms:

- **Column widths** — explicit `width` on `<col>` or `<th>`/`<td>` cells is honoured first; remaining width is distributed proportionally. Min-content widths are respected.
- **Row heights** — rows grow to fit their tallest cell. Cells with `rowspan > 1` accumulate height across rows.
- **Cell vertical alignment** — `ApplyCellVerticalAlignment` shifts cell content for `vertical-align: top`, `middle`, `bottom`, and `baseline` after row heights are known.
- **Page breaking** — when a row would cross a page boundary, the row is moved to the next page. The `<thead>` group is re-emitted on each new page. `PageBreakBottoms` on the table box records the bottom Y of the last row on each page for use during border painting.

### Pagination

Because the output is a paginated PDF, layout must determine page breaks for all block content, not just tables. When a block box's computed bottom exceeds the current page height, its position is moved to the start of the next page. Elements with `page-break-inside: avoid` (or `break-inside: avoid`) are kept together as a unit when possible.

After layout every `CssBox` has a final bounding rectangle and each `CssLineBox` has absolute document coordinates ready for the painting phase.

---

## 6. Painting

**Key types:** `RGraphics` ([Html/Adapters/RGraphics.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/Html/Adapters/RGraphics.cs)), `BordersDrawHandler` ([Html/Core/Handlers/BordersDrawHandler.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/Html/Core/Handlers/BordersDrawHandler.cs)), `CssImagePainter` ([Html/Core/Handlers/CssImagePainter.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/Html/Core/Handlers/CssImagePainter.cs)), `CssImage` ([Html/Core/Entities/CssImage.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/Html/Core/Entities/CssImage.cs)), `BackgroundImageDrawHandler` ([Html/Core/Handlers/BackgroundImageDrawHandler.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/Html/Core/Handlers/BackgroundImageDrawHandler.cs))

### Graphics abstraction

The rendering engine uses an abstract `RGraphics` base class so that all painting logic is independent of the underlying output backend. The PDF-specific implementation forwards every call to PdfSharpCore's `XGraphics`. The abstract surface exposes:

| Method | Purpose |
|---|---|
| `MeasureString` | Query text dimensions (used during layout too) |
| `DrawString` | Render a text run with a given font, colour, and RTL flag |
| `DrawLine` | Draw a straight line segment (used for borders) |
| `DrawRectangle(RPen,…)` | Stroke a rectangle outline |
| `DrawRectangle(RBrush,…)` | Fill a rectangle (backgrounds, solid borders) |
| `DrawImage` | Blit a decoded image at a destination rectangle |
| `DrawPath` | Stroke or fill an arbitrary `RGraphicsPath` (rounded corners, dashed borders) |
| `DrawPolygon` | Fill a polygon (used for border mitre joints) |
| `PushClip` / `PopClip` | Manage a clip-rectangle stack for `overflow: hidden` and page margins |
| `SuspendClipping` / `ResumeClipping` | Temporarily remove all clips for `position: fixed` elements |
| `SetAntiAliasSmoothingMode` | Enable anti-aliasing around borders and paths |

Brush and pen objects are created through the adapter (`GetSolidBrush`, `GetPen`, `GetLinearGradientBrush`, `GetRadialGradientBrush`, `GetConicGradientBrush`, `GetTextureBrush`) so the platform-specific representation stays encapsulated.

### Paint order

`CssBox.Paint` applies the CSS painters algorithm in the correct order, skipping boxes with `display: none` or `visibility: hidden`. Fixed-position boxes suspend the clip stack so they paint relative to the page rather than within any page margin clip. Off-screen boxes are culled by intersecting their containing block's client rectangle with the current clip before calling `PaintImp`.

Each box is painted as follows:

1. **Background colour** — fills the box's border area with `background-color`.
2. **Background images, gradients, and list markers** — All CSS image values (whether used as a `background-image` layer or a `list-style-image` marker) are represented as a `CssImage` discriminated union and painted through the single entry point `CssImagePainter.Paint`. The host supplies the destination rectangle and position/repeat settings; `CssImagePainter` dispatches by image type:
   - **URL images** — delegated to `BackgroundImageDrawHandler.DrawBackgroundImage`, which handles all four `background-repeat` modes (`no-repeat`, `repeat-x`, `repeat-y`, `repeat`) and `background-position` placement. The decoded `RImage` is owned by `CssImage.Url` via its embedded `ImageLoadHandler`.
   - **Linear gradients** — `GetLinearGradientBrush` with arbitrary colour stops and angles, including `repeating-linear-gradient`.
   - **Radial gradients** — `GetRadialGradientBrush` with elliptical shape, size keywords (`closest-side`, `farthest-corner`, etc.), and repeating variants.
   - **Conic gradients** — `GetConicGradientBrush` with per-stop angle positions.

   List marker images are loaded during word measurement (the same `EnsureLoadedAsync` call used by background layers) and painted by `CssBox.PaintListStyleImageMarker` immediately after the child-box paint, using a font-height-sized square positioned to the left of the list item.
3. **Borders** — `BordersDrawHandler.DrawBoxBorders` draws each side independently, respecting `border-style` (solid, dashed, dotted, double, groove, ridge, inset, outset), `border-width`, and `border-color`. Rounded corners are rendered as `RGraphicsPath` arcs. For inline elements that span multiple line boxes, left and right borders are only drawn on the first and last fragment respectively.
4. **Inline content** — for each `CssLineBox` the box participates in, its `CssRect` words are drawn in order. `CssRectWord` instances emit a `DrawString` call; `CssRectImage` instances emit a `DrawImage` call. Text decoration (underline, overline, line-through) is drawn as lines immediately after the text.

### Image loading and decoding

Images are loaded on demand by `ImageLoadHandler`. Supported sources include file paths, HTTP URLs (via `INetworkLoader`), `data:` URIs, and MHTML-embedded resources. Decoding is handled by **StbImageSharp**, which supports JPEG, PNG, BMP, GIF, TGA, and HDR. Decoded images are cached for the lifetime of a single render so that the same image referenced multiple times in a document is only decoded once.

`ImageLoadHandler` is an implementation detail of `CssImage.Url`: each URL image owns its handler and exposes `EnsureLoadedAsync(HtmlContainerInt)` for lazy loading and `Dispose()` for cleanup. Callers (background layer loops, list marker painting) interact only with `CssImage` and never touch `ImageLoadHandler` directly.

---

## 7. PDF Rendering

**Library:** PdfSharpCore (forked and merged into PeachPDF)

**Source location:** [src/PeachPDF/PdfSharpCore/](https://github.com/jhaygood86/PeachPDF/tree/main/src/PeachPDF/PdfSharpCore/)

The final phase writes the PDF file. PeachPDF embeds a custom fork of [PdfSharpCore](https://github.com/ststeiger/PdfSharpCore) directly in the source tree. The fork has been optimised specifically for PeachPDF's usage patterns and trimming requirements.

### Adapter bridge

The adapters in [src/PeachPDF/Adapters/](https://github.com/jhaygood86/PeachPDF/tree/main/src/PeachPDF/Adapters/) implement the abstract types that the rendering engine uses (`RGraphics`, `RBrush`, `RPen`, `RFont`, `RFontFamily`, `RImage`, `RGraphicsPath`, `XTextureBrush`). They translate every `RGraphics` drawing call into the corresponding `XGraphics` call in PdfSharpCore, keeping the core rendering logic completely decoupled from the PDF format.

### Font pipeline

PdfSharpCore's font subsystem is built around OpenType:

- `XFont` / `XFontFamily` — the public API for specifying a font by family name and style.
- `XGlyphTypeface` — holds the resolved typeface metrics and maps to an `OpenTypeFontface`.
- `OpenTypeFontface` — reads raw OpenType/TrueType tables (`cmap`, `glyf`, `hmtx`, `loca`, `OS/2`, etc.) from font binary data.
- `GlyphTypefaceCache` — caches resolved typefaces keyed by family+style to avoid repeated file reads.
- Font subsetting — only the glyphs actually used in the document are embedded in the PDF, significantly reducing output file size for documents that use only a subset of a large font.

The font resolver honours the family mappings registered via `PdfGenerator.AddFontFamilyMapping` and discovers system fonts from the operating-system font directories at startup.

### Image pipeline

PdfSharpCore includes importers for JPEG (`ImageImporterJpeg`) and BMP (`ImageImporterBmp`) formats. Other formats (PNG, GIF, etc.) arrive as pre-decoded RGBA bitmaps from StbImageSharp and are written as PDF image XObjects using `XBitmapImage`.

### Graphics context

`XGraphicsPdfRenderer` implements `IXGraphicsRenderer` and translates every drawing operation into PDF content stream operators. The page coordinate system is flipped from the CSS top-left origin to the PDF bottom-left origin at this layer. Each page in the document gets its own `XGraphics` context; the rendering engine drives page breaks by advancing to a new page when `HtmlContainerInt` signals a page boundary.

### Output

`PdfGenerator` is the public entry point. It:

1. Creates a `PdfDocument` and configures page size and orientation from `PdfGenerateConfig`.
2. Resolves `@page` margin overrides from the stylesheet.
3. Runs the full HTML→DOM→CSS→Layout→Paint pipeline against the document's pages.
4. Returns the completed `PdfDocument`, which the caller saves to any `Stream`.

Unnecessary PdfSharpCore features (WPF/GDI rendering targets, interactive form fields, XPS output) have been removed, keeping the dependency surface small and compatible with .NET 8 trimming and AOT compilation.

---

## SVG Rendering

**Key types:** `SvgTreeBuilder` ([Svg/SvgTreeBuilder.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/Svg/SvgTreeBuilder.cs)), `SvgRenderer` ([Svg/SvgRenderer.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/Svg/SvgRenderer.cs)), `SvgDocument` ([Svg/SvgDocument.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/Svg/SvgDocument.cs)), `ISvgSourceNode` ([Svg/ISvgSourceNode.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/Svg/ISvgSourceNode.cs)), `CssBoxSvg` ([Html/Core/Dom/CssBoxSvg.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/Html/Core/Dom/CssBoxSvg.cs))

PeachPDF renders SVG — both inline `<svg>` elements in HTML and standalone SVG (`<img src="x.svg">` / `data:image/svg+xml`) — as real vector PDF content: shapes become native PDF path-construction operators, gradients become native PDF shadings, and clips/masks/patterns become native PDF constructs. SVG is never rasterized to a bitmap. This is a cross-cutting subsystem, not a pipeline phase of its own — it plugs into DOM construction (§2), layout (§5), and painting (§6) through two specialised `CssBox` subtypes. For the full element/property compatibility matrix, see [Supported SVG Features](supported-svg-features.md).

### Two entry points, one pipeline

SVG content reaches the renderer through one of two `CssBox` subtypes, both converging on the same `SvgTreeBuilder`/`SvgRenderer` pipeline:

| Entry point | Source | How the source is read |
|---|---|---|
| `CssBoxSvg` | An inline `<svg>` element in the HTML document | Its already-parsed descendant `CssBox` tree (built for free by the ordinary HTML parser — see §1) is read as a plain tag/attribute data source, never laid out or painted through the generic box pipeline |
| `CssBoxImage` | `<img src="x.svg">` or `<img src="data:image/svg+xml,...">` | `ImageLoadHandler` sniffs the `.svg` extension or `data:image/svg+xml`/`Content-Type: image/svg+xml` and, instead of decoding a raster bitmap, parses the fetched bytes as standalone XML (`XDocument.Load`) |

Both paths build an `SvgDocument` scene graph once (cached for the box's lifetime — `CssBoxSvg.EnsureDocument`, `ImageLoadHandler.LoadSvgFromStream`) and repaint it from that cached graph on every subsequent paint, including once per output page during pagination.

### Source abstraction — `ISvgSourceNode`

`SvgTreeBuilder` never touches `CssBox` or `XElement` directly. Both entry points instead wrap their underlying tree behind a minimal, source-agnostic interface — `Name`, `GetAttribute(name)`, `Children`, `GetTextContent()` — so the exact same tree-building code produces an identical `SvgDocument` regardless of which source it came from:

- `CssBoxSvgSourceNode` ([Svg/CssBoxSvgSourceNode.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/Svg/CssBoxSvgSourceNode.cs)) wraps a live `CssBox` tree (inline `<svg>`).
- `XElementSvgSourceNode` ([Svg/XElementSvgSourceNode.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/Svg/XElementSvgSourceNode.cs)) wraps a `System.Xml.Linq.XElement` (standalone SVG, parsed as real XML rather than through the HTML tokenizer).

`GetTextContent()` returns only a node's own direct text-node children, not descendant elements' text — this matters for `<text>Hello <tspan>World</tspan></text>`, where "Hello" (the `<text>`'s own run) must stay separate from "World" (the `<tspan>`'s own run) for both source kinds identically.

### Build phase — `SvgTreeBuilder`

`SvgTreeBuilder.Build(ISvgSourceNode root, RAdapter adapter, RColor? contextColor)` runs synchronously in two passes:

1. **`CollectDefinitions`** — a single walk of the whole tree that registers every id-bearing node (`Dictionary<string, ISvgSourceNode>`) and fully resolves self-contained definitions (gradients, markers, patterns, masks, `<style>` text) up front. This exists because SVG allows forward references — a `<use>` or `fill="url(#id)"` can reference an id defined later in document order.
2. **Recursive tree build** — `BuildElement`/`BuildGroup`/`BuildPath`/etc. walk the tree again, this time constructing the immutable `SvgElement` scene graph, resolving `url(#id)` references against the now-complete registry from pass 1.

Presentation properties (fill, stroke, opacity, font, etc.) are threaded down the recursion as small immutable record structs — `InheritedPaint` for paint/stroke properties, `FontContext` for `<text>`'s font-family/size/weight/style — so a property left unspecified on a child correctly resolves to its nearest ancestor's value, matching CSS-style inheritance without needing `CssBox`'s own cascade machinery.

The result is an `SvgDocument`: a `ViewBox`/`Width`/`Height`/`PreserveAspectRatio` plus a `List<SvgElement>` scene graph (`SvgPathElement`, `SvgCircleElement`, `SvgRectElement`, `SvgGroupElement`, `SvgUseElement`, `SvgImageElement`, `SvgTextElement`, and others — see [Svg/SvgElement.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/Svg/SvgElement.cs)) plus dictionaries of gradient/clip-path/marker/pattern/mask definitions keyed by id.

### Paint phase — `SvgRenderer`

`SvgRenderer.RenderInto(RGraphics g, SvgDocument document, RRect viewportRect)` is the single paint entry point shared by `CssBoxSvg.PaintImp` and `CssBoxImage.PaintImp`. It clips to the target rectangle, computes the viewBox→viewport transform (`ComputeViewportTransform`, supporting all 9 `preserveAspectRatio` alignment keywords plus `meet`/`slice`/`none`), pushes that transform, and recursively paints every scene-graph element.

Critically, `SvgRenderer` issues nothing but ordinary `RGraphics` calls (`GetGraphicsPath`, `DrawPath`, `GetSolidBrush`, `GetLinearGradientBrush`, `PushClip`, `PushTransform`, `DrawString`, …) — the same abstraction §6's HTML/CSS painting uses. There is no SVG-specific graphics API; an SVG `<path>` becomes an `RGraphicsPath` built from bezier/arc/line segments exactly the way a CSS `border-radius` corner does, and an SVG gradient becomes an `RBrush` from the same `GetLinearGradientBrush`/`GetRadialGradientBrush` calls background-image gradients use. This is what keeps SVG output genuinely vector: every drawing call flows through the same `XGraphics`-backed adapter as the rest of the document (§7).

PDF's native shading types have no tiling/repeat concept of their own, so `spreadMethod="repeat"`/`"reflect"` (`SvgRenderer.ExpandLinearSpread`/`ExpandRadialSpread`) pre-tile the gradient's own stop list — projecting the filled shape's bounding box onto the gradient axis (or radius) to find how many cycles are needed to cover it, then replicating the stops per cycle, mirroring alternate cycles for `reflect` — before the brush is ever built. This mirrors, but doesn't share code with, how CSS's `repeating-linear-gradient()`/`repeating-radial-gradient()` (`CssImagePainter.ExpandRepeatingStops`) solve the identical underlying problem: CSS's gradient axis is already sized to the background box before tiling starts, while SVG's `x1`/`y1`/`x2`/`y2` (or `r`) define only one author-chosen cycle, and SVG additionally needs `reflect`, which CSS repeating-gradients don't have.

The viewport-transform helper is reused, not reimplemented, for every SVG construct that establishes its own coordinate system: a nested `<svg>`, a `<symbol>` reached through `<use>`, a `<marker>` instance, and a `<pattern>` tile all call the same `RenderViewport` helper `RenderInto` itself is built on.

### PDF primitive reuse for pattern/mask

`<pattern>` and `<mask>` needed genuinely new PDF-writing capability, supplied by extending the `RGraphics`/`RAdapter` abstraction rather than adding SVG-specific PDF code:

- `RGraphics.CreateTile(width, height)` creates an `XForm`/`PdfFormXObject` pair — a real PDF Form XObject — and returns a fresh `RGraphics` that paints into it. A pattern's content, a mask's content, and (for a masked element) the element's own content are each rendered once into a tile this way.
- **Pattern fill** clips to the filled shape's geometry, then draws that one tile `RImage` repeatedly (`DrawImage`) across the shape's bounding box. Every repeat references the same underlying vector Form XObject content — never a rasterized bitmap — but this is not a native PDF `/PatternType 1` tiling pattern object; it's a simpler, lower-risk approximation that stays fully vector.
- **Mask** (`SvgRenderer.RenderMaskedElementContent`) renders the masked element's own content into one tile and the `<mask>`'s content into a second, identically-sized tile, then composites them via `RGraphics.DrawImageMasked(content, mask, destRect)` — a single atomic placement (`XGraphicsPdfRenderer.DrawImageMasked`, [PdfSharpCore/Drawing.Pdf/XGraphicsPdfRenderer.cs](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/PdfSharpCore/Drawing.Pdf/XGraphicsPdfRenderer.cs)) that emits the PDF `/SMask` `gs` operator inside the *same* `q ... cm ... Do ... Q` block used to place the content, reusing `DrawImage`'s already-correct destRect placement math rather than relying on whatever CTM happens to be ambient at some earlier point in the content stream. This atomicity is load-bearing, not stylistic: a `CreateTile` form's content is Y-flipped relative to *its own* small height, not the page's, so a mask activated via a bare, separately-timed `gs` (an earlier implementation) silently lands in the wrong part of the page the moment the page has any margin, scroll offset, or layout position — evaluating as fully transparent everywhere. Both the mask's `/G` form and the masked content's own form must additionally carry their own `/Group << /S /Transparency ... >>` transparency-group dictionary for the soft mask to take effect in spec-conformant readers (Chrome/Edge/Acrobat, all PDFium- or Acrobat-derived) — MuPDF is unusually lenient and applies an `/SMask` even to non-group content, which made this gap easy to miss under a single-renderer check. See [Supported SVG Features](supported-svg-features.md) and the `PdfSoftMask`/`PdfExtGState` PDF object types for the rest of the construct.

### Link annotations

An `<a>` element becomes a real PDF link annotation, reusing the same annotation-registration pipeline plain HTML `<a>` elements already use rather than a parallel SVG-specific one. Because painting runs once per *output page* during pagination, link discovery is a deliberately separate, paint-independent tree walk — `SvgRenderer.CollectLinks` composes transforms and bounding boxes only, issuing no `RGraphics` calls — so a link is registered exactly once regardless of how many pages the containing box is painted on. `DomUtils.GetAllSvgLinks` finds every `CssBoxSvg`/`CssBoxImage` in the box tree and calls `CollectLinks` on each; `HtmlContainerInt.GetLinks()` merges the results into the same list ordinary HTML `<a>` links populate.

### Coverage

See [Supported SVG Features](supported-svg-features.md) for the complete element/attribute compatibility matrix, including the reasoning behind each deliberately-excluded SVG feature (SMIL animation, scripting, `filter`, `foreignObject`, legacy SVG fonts, `textPath`, and others).
