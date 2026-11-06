# PeachBrackets

`PeachBrackets` is the HTML, CSS, SVG and MathML engine inside PeachPDF, on its own. It parses a document, resolves the cascade, lays the
content out into pages, and paints each page onto any [PeachDrawing.Core](peachdrawing-core.md) `Canvas`: a PDF page from
[PeachDrawing.PDF](peachdrawing-pdf.md), a bitmap from [PeachDrawing](peachdrawing.md), or a canvas you implement. It has **no PDF
dependency** and does not know what the canvas writes. It is pure .NET, trimmable and Native AOT compatible, and is versioned in lockstep
with PeachPDF: the same version number for every release.

```bash
dotnet add package PeachBrackets
```

> **Status: pre-1.0.** `HtmlDocument`, `HtmlDocumentOptions`, `DocumentPage` and the annotation and tagging types on this page are the
> package's public surface today - see `PublicApi.txt` for the exact, machine-checked snapshot. PeachPDF is the package's other consumer:
> it lays HTML out with this same engine and paints it onto PDF pages, so this is not a copy built beside it. Until 1.0, the public API may
> change between releases, and what PeachPDF reaches through internals today moves onto it step by step.

For what the engine supports - elements, properties, selectors, at-rules - see [HTML and CSS support](html-css-support.md) and
[SVG support](supported-svg-features.md). That matrix describes the engine; it applies here exactly as it does to PeachPDF.

## Quick start

Render a document to a PNG per page:

```csharp
using PeachBrackets;
using PeachDrawing;
using PeachImage.Formats.Png;

var context = new RasterRenderContext();
await using (var fontFile = File.OpenRead("SourceSans3-Regular.ttf"))
{
    await context.AddFont(fontFile, "Source Sans");
}

var html = """
    <html><body style="font-family: 'Source Sans'">
      <h1>Hello, PeachBrackets</h1>
      <p style="background: #fde8c8; padding: 6pt">Laid out by the same engine that powers PeachPDF.</p>
    </body></html>
    """;

using var document = await HtmlDocument.LoadAsync(html, context, new HtmlDocumentOptions
{
    PageWidthPoints = 400,
    PageHeightPoints = 300,
});

using (var measuring = context.CreateCanvas(1, 1))
{
    await document.LayoutAsync(measuring);
}

foreach (var page in document.Pages)
{
    using var canvas = context.CreateCanvas((int)page.WidthPoints, (int)page.HeightPoints);
    await document.PaintPageAsync(page.Index, canvas);

    await using var file = File.Create($"page-{page.Index + 1}.png");
    await canvas.SaveAsync(file, "png", new PngEncoderOptions());
}
```

## The three steps

1. **`HtmlDocument.LoadAsync(html, renderContext, options)`** parses the HTML and cascades every style sheet that applies. The
   `RenderContext` supplies the fonts, images and resource loading the document uses; fonts the document names must be registered on it
   (or installed) before loading.
2. **`LayoutAsync(measuringCanvas)`** breaks the content into pages. The canvas is only used to measure text, so any canvas made by the same
   render context will do; nothing is drawn on it. A document is laid out once.
3. **`PaintPageAsync(pageIndex, canvas)`** draws one page onto a canvas you provide. The page is painted in the layer order css-page-3
   defines: the `@page` box's background, the document canvas background, the page border, the content, footnote areas, and the margin
   boxes. Pages can be painted in any order, any number of times.

`Pages` lists the laid-out pages: each has its `Index` and the size of its sheet in points. `GetContentHeight(pageIndex)` says how far down a
page's content reaches, in points from the top of its content area, which is what a viewer that sizes itself to the document needs (the sheet
itself is as tall as the page, whatever the content). A page's size can differ from its neighbours'
when `@page` rules say so, which is why you read it per page rather than assuming one size.

### Synchronous layout

A host that cannot wait - a control that has to know its size while the application is laying itself out - can say that a document needs
nothing asynchronous and use the synchronous forms of the three steps:

```csharp
var options = new HtmlDocumentOptions { SynchronousOnly = true, MediaType = MediaType.Screen };
using var document = HtmlDocument.Load(html, context, options);
document.Layout(measuringCanvas);
document.PaintPage(0, canvas);
```

Nothing is awaited and nothing blocks. `SynchronousOnly` is a promise about the document: its images, style sheets and fonts are inline (`data:`
URIs), already registered on the render context, or come from a render context that answers at once (`RenderContext.GetResourceStream` returns
a `ValueTask` that has already completed). If the document then tries to do something that would really suspend - fetch an image, a style sheet
(including an `@import`) or a web font whose `RenderContext` has not answered yet - the call throws `InvalidOperationException` naming the
resource, instead of waiting for it. Without `SynchronousOnly` the synchronous forms throw, and the `Async` ones are the only choice.

The asynchronous methods return `ValueTask`, so they too finish synchronously, without allocating, when there is nothing to wait for.

### Units

Every length the API reports or accepts is in **points** (1/72 inch). The canvases are the exception: a canvas is in the *layout units* of the
render context, `RenderContext.LayoutUnitsPerPoint` units to the point, with its origin at the top-left corner of the sheet and y growing
downward. With the default of one unit per point, a canvas for a 400 x 300 point page is 400 x 300 units. Set
`LayoutUnitsPerPoint` before loading a document: fonts are sized in it.

## `HtmlDocumentOptions`

| Option | Meaning |
|---|---|
| `PageWidthPoints`, `PageHeightPoints` | The sheet. A4 (595 x 842) by default. A `@page { size }` rule in the document overrides it. |
| `MarginTopPoints`, `MarginRightPoints`, `MarginBottomPoints`, `MarginLeftPoints`, `SetMargins(points)` | The page margins, 20 points each by default. `@page { margin }` overrides them. |
| `MediaType` | The medium: `Print` (the default) breaks the document into pages and applies `@media print`; `Screen` lays it out as one continuous surface and applies `@media screen`. See [Screen and print media](#screen-and-print-media). |
| `ViewportHeightPoints` | For `Screen`: the height `vh`, a percentage root height and the `height`/`orientation` media features resolve against, and the least height of the surface. Null (the default) when the host does not know it. |
| `ColorScheme` | What `prefers-color-scheme` reports: `Light` (the default) or `Dark`. |
| `DevicePixelRatio` | Device pixels the host paints for one CSS pixel (1 by default). The `resolution` and `device-pixel-ratio` media features, a `<source media>` condition and the choice among `srcset` images see it. |
| `ImageSelection` | How a `srcset` image is chosen: `ImageSelectionPolicy.HighestDensity` (the default for `Print`) or `BestForTarget` (the default for `Screen`), with a `Tolerance`, a `MaxDecodePixels` budget and `UpgradeOnZoom`. See [Responsive images on screen](#responsive-images-on-screen). |
| `Preferences` | The reader's settings that `prefers-reduced-motion`, `prefers-contrast`, `forced-colors`, `inverted-colors`, `prefers-reduced-transparency` and `prefers-reduced-data` report (`UserPreferences`). The default is a static document's: reduced motion on, everything else off, so a document reports what it always did. A window or phone reads them from the system (Windows high contrast, Android's text-contrast and remove-animations settings). |
| `FontScale` | A multiplier for the root font size (1, the default, leaves it alone), for a reader who sets a text size on their system. Text sized in `em`, `rem`, a percentage or a keyword follows; text in `px` or `pt` does not. Added to `UserStyleSheet`, so ignored when `StyleSheet` is set. |
| `PaintFormControls` | Draw form controls with their look for a host that shows the page: a check box's check mark and a radio button's dot (in `accent-color`), a text field's value or placeholder, a button's label (`Submit` and `Reset` by default), a drop-down's chosen option and arrow, and a range's track and thumb. Everything is drawn from the control's own CSS, so borders, backgrounds, padding, fonts and colours apply. Off by default, which keeps a control the plain box it is in a PDF; it takes precedence over `CollectFormFields` for how a control is drawn, so a host can collect the fields and still show them. |
| `TextSpacing` | Spacing applied over the document's own for a reader who needs more room around text: line height, paragraph spacing, letter spacing and word spacing as multiples of the font size. `TextSpacingOverrides.Wcag` is what WCAG 2.2 success criterion 1.4.12 asks a page to survive. Added to `UserStyleSheet` as `!important` rules, so it is ignored when `StyleSheet` is set. |
| `UserStyleSheet` | CSS applied after the built-in defaults and before the document's own style sheets. |
| `SynchronousOnly` | The document needs nothing asynchronous; see [Synchronous layout](#synchronous-layout). |
| `IgnoreAuthorStyleSheets` | Drop the document's own `<style>` and `<link rel=stylesheet>` rules, leaving the defaults and `UserStyleSheet`. |
| `DefaultLanguage` | The language to assume when the document declares none; it drives hyphenation and language-dependent line breaking. |
| `AnimationProgress` | Paint the document at a point in its animations, from 0 to 1, instead of its static look. |
| `AnimationsEnabled` | Play the document's CSS animations, transitions and animated images over time; see [Animations and transitions](#animations-and-transitions). |
| `SnapBoxDecorationsToCssPixels`, `SnapBorderWidthsToCssPixels` | Snap backgrounds, borders and border widths to whole CSS pixels, as a browser does. |
| `CollectFormFields` | Collect form controls into `Annotations.FormFields`. |
| `RecordTextRuns` | Let the document report where its text is (`TextRuns`), for selection, copy and find. |
| `EnableIncrementalUpdates` | Allow `SetStyle` to restyle elements, and `ReplaceTextAsync` and its relatives to edit the text, of the laid-out document in place. |
| `EnableSelectionStyling`, `SelectionBackground`, `SelectionForeground` | Paint a selection (`SetSelectedRanges`) in the document's `::selection` style, or in these colours where no rule applies. Needs `RecordTextRuns`. |

The options are read when the document is loaded.

## Responsive images on screen

For paged output a `srcset` or `<picture>` picks the candidate with the highest pixel density, the best print quality. A `Screen` document picks the smallest candidate that is sharp enough for the display, so a phone on a 2x screen does not decode the 4x image of a photo it shows 360 pixels wide:

- the target density is `HtmlDocumentOptions.DevicePixelRatio` times the render scale (below); a candidate counts as sharp enough when its density, against the width the image is shown at, reaches the target divided by `ImageSelectionOptions.Tolerance` (1.1 by default);
- when none is, the largest candidate within `MaxDecodePixels` is used (the pixel count is only known for width descriptors, estimated from the `width` and `height` attributes of the `<img>`);
- a `<source type>` the host cannot decode is skipped, which is what `RenderContext.SupportsImageType` answers (a context that decodes with PeachImage reads PNG, JPEG, GIF, WebP, AVIF, JPEG XL, BMP, TIFF and ICO; one that is set to use only a platform codec says what that codec reads);
- the box never changes size with the candidate: the natural size is divided by the density measured against the `sizes` slot, so choosing a smaller or larger bitmap changes pixels, not layout;
- after layout the images are chosen again against the width they were really given (which is how `sizes="auto"` and a stylesheet width that differs from `sizes` are served), only towards a denser candidate, at most once per layout;
- a candidate that cannot be loaded is replaced by the next best (up to three) and then by `src`; each failure is listed in `HtmlDocument.ImageLoadFailures`.

A host that lets the reader zoom calls `HtmlDocument.SetRenderScale(zoom)` and then `UpgradeImagesAsync()`. The second chooses again without parsing or laying the document out again, so every `DocumentElement` stays valid, loads the denser images and returns the regions to repaint (`PaintPageRegionAsync`). The scale only grows, and zooming back out keeps the images already loaded. `SetRenderScale(zoom, devicePixelRatio)` also reports a display with another scale. The `resolution` media features are evaluated when the document is loaded; a new device pixel ratio reaches them on the next load. An image that nothing shows any more is dropped from the document's image cache.

PDF output is not affected: `PdfGenerator` asks for `HighestDensity`.

## Screen and print media

`MediaType` says which kind of medium the document is for, and it decides two things at once: which `@media` rules match, and whether the document has pages.

- **`Print`** is paged. The page size, `@page` rules, margin boxes, page breaks, footnotes and page floats all apply, and `Pages` has as many entries as the content needs.
- **`Screen`** has no pages. The document is one surface as wide as `PageWidthPoints` less the margins and as tall as its content (at least `ViewportHeightPoints`), `Pages` has exactly one entry, and nothing cuts a very tall document.

What does not apply on a screen, as in a browser: `@page` (size, margins, borders, named pages, `:first`/`:left`/`:right`) and page margin boxes, page breaks (`break-*: page|left|right|recto|verso`, `page-break-*`), running elements (`position: running()`), `string-set`/`bookmark-*` output, and the css-gcpm footnote and css-page-floats values (`float: footnote|top|bottom|top-bottom|snap`), whose declarations are dropped so the box stays in the flow. Multi-column containers still break into columns, including `break-before: column` and `break-inside: avoid` inside them. `counter(pages)` is 1.

The media features describe the widget: `width` and `height` are the viewport, `orientation` is `portrait` when the height is at least the width, `scan` is `progressive` and `display-mode` is `browser`.

### Scroll containers

On a screen, a box whose `overflow` is `auto`, `scroll` or `hidden` is a scroll container ([CSS Overflow 3 §2](https://www.w3.org/TR/css-overflow-3/#scroll-container)): its content is clipped to its padding box and moves as the container is scrolled. A paged document has none (print shows the whole of the content), and the root and the body, whose `overflow` belongs to the viewport, are not scroll containers: the host scrolls the surface itself. A `<textarea>`, `<select>` and `<input>` are never one; they scroll their own text.

```csharp
var list = document.GetElement("list")!;
var metrics = document.GetScrollMetrics(list);            // null when it is not a scroll container: X, Y, MaxX, MaxY, Viewport, ContentWidth, ContentHeight (points)
foreach (var region in document.SetScrollPosition(list, 0, 120))   // scrollTo; NaN leaves an axis, a position past the end is clamped
    Invalidate(region);                                     // the scrollport, and only that: nothing is laid out again
document.ScrollBy(list, 0, 40);                             // scrollBy
document.ScrollIntoView(someItem);                          // every container that holds it, outermost first, by the least that brings it into view
document.ScrollPositionChanged += (_, e) => Log(e.Element.Id, e.X, e.Y);
```

Scrolling moves what is drawn, not the layout, so it costs a repaint of the scrollport. `GetElementBounds`, hit testing (`GetCursorAt`, `GetInteractionAt`), text runs, links and form controls follow the scrolled position, and a point outside a container's scrollport is not on what it clips. A host that draws no bars of its own routes input with `GetScrollbarAt(page, x, y)` (the container, the axis, the thumb or the track before or after it, and the track and thumb in points), `ScrollThumbTo` (a thumb being dragged: where its leading edge is to be), `ScrollByPage` (a press on the track), `GetScrollContainerFor(element, dx, dy)` (the container a key goes to when the focus is in `element`) and `GetElementOf(node)` (the element of an accessibility node). A host that routes a wheel turn or a touch drag asks which container is under the pointer with `GetScrollContainerAt(page, x, y, dx, dy)`: the innermost one that can scroll the way asked, so a container at the end of its content passes the turn on to the one around it (scroll chaining), and `null` means the host scrolls the surface. The scrollable overflow area is what the container holds, plus the padding at its end; a nested scroll container counts as its own box. A container drawn with `overflow: auto` or `scroll` shows overlay scrollbars (a thumb on the right and bottom edges, over the content); `hidden` scrolls by these calls only and shows none. The scroll offsets are kept while the document is laid out again and are dropped when a container goes away. Scroll and view timelines (`animation-timeline: scroll(self)`, `scroll(nearest)`, `scroll-timeline`, `view()`) follow the container that scrolls them, see [Scroll-driven animations](#scroll-driven-animations).

### The viewport, fixed and sticky boxes

`vh`, `vi`/`vb` and a percentage `height` on the root resolve against `ViewportHeightPoints`, which the host pins: a value that followed the content would never settle for `100vh` content. With none set, viewport-height units behave as `auto`. The root is not stretched to the viewport, but the canvas background fills it.

`position: fixed`, `position: sticky` and `background-attachment: fixed` depend on which part of the surface is showing. Tell the document with `SetViewport`:

```csharp
// The host scrolled: this part of the surface (points from its top-left) is showing now.
foreach (var region in document.SetViewport(new Rect(0, scrollY, widthPoints, heightPoints)))
    Invalidate(region);   // repaint what moved: where fixed and sticky boxes were and are
```

A fixed box stays where its offsets put it in the viewport, a sticky box slides with the top (or bottom, left, right) of it until its containing block ends, and a fixed background image is positioned against it. Text runs, links, form controls and accessibility bounds follow, so a click lands on what is drawn. `Viewport` is the last rectangle given, and a document that is laid out again keeps it.

A host that scrolls a surface natively (a scroller moved by the compositor) cannot keep a fixed or stuck box still by repainting it after the scroll: it would move with the content first and snap back a frame later. Paint such a document in two layers instead, the content on the scrolled surface and the boxes the viewport holds in place on a layer over it that does not scroll:

```csharp
await document.PaintPageAsync(0, scrolledCanvas, DocumentPaintLayer.Content);          // everything except fixed and stuck boxes
// overlay canvas: the viewport, with the surface translated so the viewport's top-left is at (0, 0)
await document.PaintPageAsync(0, overlayCanvas, DocumentPaintLayer.ViewportAttached);  // only fixed and stuck boxes
```

`Content` and `ViewportAttached` together draw exactly what `All` (the default) does. A fixed box is always in the attached layer; a sticky box is in it while the viewport holds it away from its place, and in the content layer (scrolling with it) before and after. A sticky table cell or row is held inside its table. A `Print` document has no viewport that moves: `SetViewport` returns nothing.

A `background-attachment: fixed` image is different: it sits under its box's content and over the backgrounds behind it, so it is part of the content layer and cannot be kept still in the attached one. `HtmlDocument.HasViewportBackgrounds` reports whether the document has one; a host that scrolls a painted surface natively should then paint the visible part of the page itself on every scroll step (`PaintPageRegionAsync`) instead, or the image moves with the content until the next paint puts it back.

## What the pages hold besides pixels

`document.Annotations` describes the parts of the document a reader of the pages can act on, positioned on their pages in points from the
top-left corner of the sheet. A consumer that writes a format with these concepts carries them over; one that only draws can ignore them.

- **`GetInteractionAt(pageIndex, x, y)`** tells a pointer or touch host what the document allows at a point: whether it is inert, which gestures a
  touch there may begin (`touch-action`, as `TouchActions`) and whether its text may be selected (`user-select`).
- **`GetAccessibilityTree()`** is what an assistive technology reads: a tree of the parts of the page that mean something to a reader, each with its
  role (the one HTML gives the element, or its `role` attribute's), the name it is called by, its state, the language of its content and where it is
  on its pages. A host that has an accessibility tree of its own builds it from this one; **`GetAccessibilityNodeAt(pageIndex, x, y)`** gives the
  innermost part at a point. See [Accessibility](accessibility.md).
- **Interaction state** (`SetPointerStateAsync(page, x, y, pressed, canvas)`, `ClearPointerStateAsync(canvas)`, `SetFocusAsync(element, focusVisible, canvas)`)
  make the document's `:hover`, `:active`, `:focus`, `:focus-visible` and `:focus-within` rules apply. A state change that only changes how
  something is painted - a colour, `opacity`, `transform`, `filter`, a shadow, an outline, `text-decoration`, `cursor` - is applied to the
  styles in place with no layout: the `UpdateResult` has `Incremental = true`, `DirtyRegions` cover where the elements were painted and where
  they are now (a shadow, outline, blur or transform widens them), and `DocumentElement` handles, text runs and the selection stay valid. A
  change to anything else (a size, a font, `display`, generated content, a pseudo-element appearing) applies by cascading and laying the document
  out again with it, so a rule can change anything, including the size of an element; `DirtyRegions` then name every page from the first one
  that moved, `Incremental` is false, and `DocumentElement` handles taken before it are stale and must be taken again. A state change that no
  rule reads costs nothing, and one that is already in force returns `Applied = false`. Selection and highlights are kept when the text does not
  change. `HtmlDocumentOptions.IncrementalRestyle = false` always loads the document again. Combine it with `HtmlDocumentOptions.Input`, so
  `@media (hover: hover)` offers hover styling only on a host with something to hover with. Limits: only a document loaded from HTML can be
  restyled this way (not a declarative one), and not after its text was edited in place. A state change starts the transitions the box
  names when `AnimationsEnabled` is on (see [Animations and transitions](#animations-and-transitions)).
- **`NeedsRestyle`** and **`RestyleFormAsync(canvas)`**: after a change to a control, `NeedsRestyle` is true when a style sheet rule reads the state that changed (`:checked`,
  `:disabled`, `:enabled`, `:required`, `:optional`, `:read-only`, `:read-write`), and `RestyleFormAsync` styles the document again with the controls as they are now and
  reports the pages to repaint. Typing into a field is not such a change. A restyle for another reason (a `:focus` rule) also settles it.
- **`SetOpenAsync(element, open, canvas)`** opens or closes a `<details>` element, as a click on its summary does. A closed one shows only its summary, so
  the document is styled and laid out again with the new state and the result lists the pages to repaint from the first one that moved. The state is kept if
  the document is loaded again for a `:focus` or pointer rule.
- **`GetFormState(element)`**, **`SetChecked(element, isChecked)`**, **`SetValue(element, value)`** and **`Activate(element)`** read and change what an
  `<input>` or `<select>` holds in the document itself: whether a check box or radio button is checked (checking a radio button unchecks the rest of its
  name in the same form), a text field's text, a range's number (kept between `min` and `max`), a drop-down's chosen option. `Activate` does what a click
  does (toggles a check box, checks a radio button). Each returns an `UpdateResult` with the regions to repaint; no layout pass runs, and the accessibility
  tree follows. A disabled control, and a read-only one for `SetValue`, is left alone. What a control holds is kept when a change of focus loads the
  source again for a `:focus` rule. `SetRangeAtPoint` sets a range control from a point on its page, mapped onto the track the way the control draws it and rounded to its `step`, for a press or drag; `GetSelectOptions` lists a drop-down's options for a host that offers a choice. A `<textarea>` is edited with the same calls as a text field (`GetEditState`, `SetEditSelection`, `InsertText`...) and keeps its line breaks; `GetEditIndexAt` also takes a `y` to pick the line, `GetEditIndexByLine` moves the caret up or down by lines and `GetEditLineRange` gives the ends of a line for Home and End. A text area scrolls its lines to keep the caret in view after the caret moves or the text changes; `ScrollTextArea` scrolls it by a wheel's distance instead (it reports not applied at the first or last line, so a host can let the page scroll). `GetFileInputInfo` says whether a file input takes several files and which kinds, and `SetFileNames` records the names a host's picker returned so the control shows them (the document does not read or keep the files; `SetFiles` takes their bytes as well). `CreateFormSubmission` takes a submit button and returns the form as it would be sent: the method, the address (the button's `formaction`, `formmethod` and `formenctype` replace the form's own), the encoding, and an entry for every control that takes part - named, not disabled (or in a disabled `fieldset`), a check box or radio button only when checked, a drop-down's chosen options, a file input's files (an empty one when none was chosen) and the button that was used, in document order, with line breaks as CRLF. An image button (`<input type=image>`) sends the point it was pressed at, relative to its top left in CSS pixels, as `name.x` and `name.y` (or `x` and `y`) through the overload of `CreateFormSubmission` that takes the page and point. `CreateImplicitSubmission` is what Enter in a one-line field submits: the form's first submit button as if it was clicked, or - with none - the form when it has no more than one field Enter would submit from. `GetValidationProblems` checks a form as a browser does before it submits (`required`, `minlength`, `maxlength`, `pattern`, the syntax of an email address, URL or number, and `min`, `max` and `step`; disabled and read-only controls, hidden inputs and buttons are left out, and `novalidate` / `formnovalidate` turn it off) and returns a `ValidationProblem` - the control, a `ValidationKind` and an English sentence - for each control that fails. `ResetFormAsync` puts a form's controls back as the source has them (what a reset button does); `IsResetButton` says whether an element is one. `FormSubmission.Serialize()` writes the body as `application/x-www-form-urlencoded`, `multipart/form-data` or `text/plain`, and `GetUri()` the address a `get` goes to; the document sends nothing. An `AccessibilityNode` also describes the text it holds: `FontFamily`, `FontSize` (points), `FontWeight`, `IsItalic`, `ForegroundColor` and `BackgroundColor` (0xAARRGGBB), `Underline` and `Strikethrough` (its own `text-decoration` or that of an element around it).
- **`GetEditState()`**, **`SetEditSelection(start, end)`**, **`InsertText(text)`**, **`ReplaceEditText(start, end, text, caret)`**,
  **`DeleteEditText(backward)`**, **`SetComposition(start, end)`**, **`SetCaretVisible(visible)`** and **`GetEditPlace(canvas, index)`** edit the text
  field that has the focus (text, search, URL, telephone, email, password and number inputs): its text and selection, the text an input method is still
  composing, and where the caret is on the page for the system's candidate window. The field draws its selection, an underline under the composition and
  a caret, which a host blinks with `SetCaretVisible`. Typing is cut to `maxlength`, line breaks are dropped, a read-only or disabled field is left alone,
  and deleting takes a whole grapheme cluster. A host maps its text-input service (Windows TSF, Android `InputConnection`) onto these calls.
- **`PaintAccessibility(pageIndex, canvas)`** puts the roles, names and states of the [accessibility tree](accessibility.md) on a canvas as
  `Canvas.AddSemanticRegion` calls in reading order, for a canvas that writes them (SVG), so a drawing made from the document can be read by
  assistive technology. Containers and runs of text are left out; the regions are flat.
- **`GetCursorAt(pageIndex, x, y)`** gives the pointer the document asks for at a point (the CSS `cursor`, with `auto` resolved), and
  **`PaintCursors(pageIndex, canvas)`** puts the areas that ask for one of their own on a canvas as `Canvas.AddCursor` calls, for a canvas that
  records them for a host (`SkiaLinkMap.CursorAt`, `Win2dLinkMap.CursorAt`) or writes them (SVG). `pointer-events: none` lets a point fall
  through, in these and in `GetElementAt`. See [Pointer Interaction](html-css-support.md#pointer-interaction).
- **`PaintLinks(pageIndex, canvas)`** puts a page's links on a canvas as `Canvas.AddLink`/`AddDestinationLink`/`AddDestination` calls (see
  [Links and destinations](peachdrawing-core.md#links-and-destinations)), so a canvas for a format with hyperlinks - the PDF and SVG ones - writes
  them without the consumer reading `Links` itself. Call it for every page *after all the pages have been painted*: where a link sits under a
  transform is only known once its page has been painted, and a link can lead to a later page. It also hands each link's handle to the
  `StructureTagSink`, if there is one, so a tagged format can tie the link to the content it covers.
- **`Links`** - each `PageLink` has its `PageIndex` and `Bounds`, an optional four-corner `Quad` when the link sits under a transform, and
  either a `Uri` or an `Anchor` with the `Destination` (`PagePosition`: page index and distance from the top) it leads to.
- **`Bookmarks`** - the outline: each `Bookmark` has a `Level`, a `Title`, whether it starts open, and a `Uri` or `Destination`. Headings
  produce them by default; `bookmark-level`, `bookmark-label` and `bookmark-target` change that.
- **`Annotations`** - the annotations authors asked for with `-peachpdf-pdf-annotation-type`: each `PageAnnotation` has its `PageIndex`, `Kind` (`PageAnnotationKind`: a note, a text mark, an attached-file icon, a shape, free text, a stamp, ink), `Bounds`, the `Quads` of a text mark, the `Strokes` of ink, an optional `PopupBounds`, and its icon name, colour, title, contents, author, attachment name and dates. Always collected.
- **`FormFields`** - with `CollectFormFields` on, each `PageFormField` has its `PageIndex`, `Bounds`, a `Classification` (`FormFieldKind`,
  name, value, checked state, options, and the `readonly`/`required`/`maxlength`/`placeholder` attributes), and a `FormFieldAppearance` that
  paints each state of the control onto a canvas of your choosing, so an interactive field can look like the rest of the page.

`document.Language` and `document.Metadata` (title, author, subject, keywords, date, generator) report what the document says about itself.
`GetElementBounds(id)` gives the rectangle of an element by its `id`.

## Where the text is: selection, copy and find

A consumer that paints pages into a bitmap has no text to select unless the engine says where the text landed. With `RecordTextRuns` set,
`document.TextRuns` (all pages) and `GetTextRuns(pageIndex)` give one `TextRun` per line of a block, in reading order, in points from the
top-left corner of the sheet - the same space as `Annotations.Links[].Bounds`. Nothing is computed until you read it, and layout and painting
are identical either way.

A `TextRun` has:

- `Text` - what was drawn, in logical order (bidirectional text is not reordered or mirrored), whitespace as the layout collapsed it, and a
  space where two words on a line were separated.
- `Bounds`, `Baseline`, `Ascent`, `Descent` - the line's rectangle and font metrics, enough to fill a highlight as tall as the line.
- `CharacterOffsets` - the x of the edge before each UTF-16 index and after the last (`Text.Length + 1` values), in the logical order of the
  text, so a right-to-left run's offsets decrease. `CharacterBoundaries` lists the indices a caret can sit at: the grapheme cluster
  boundaries, so never inside a surrogate pair, an emoji sequence or a letter with its accents.
- `IsRtl`, `BlockId` (the same for every run of a paragraph, list item, table cell or `pre`) and `ElementId` of that block, and `LinkUri`
  when the run is inside a link. A line is split into several runs where a link starts or ends.

On `HtmlDocument`:

| Member | Result |
| --- | --- |
| `HitTest(page, x, y)` | A `TextPosition` (`RunIndex`, `CharIndex`): the start of the character under the point. A point in a gap or margin goes to the nearest line, one past a line's end to its nearest character. |
| `GetCaretPosition(page, x, y)` | The boundary between characters nearest the point - where a cursor, or the end of a drag selection, goes. |
| `GetSelectionRects(from, to)` | One rectangle per line covering the text between two positions (the end is not included). |
| `GetText(from, to)` | The plain text as it would be copied: a line break between blocks and at a forced break (`<br>`, a `pre` line), a space where a paragraph wrapped, and no space that was not in the text. |
| `FindText(query, ignoreCase)` | Every occurrence as a `TextRange`; a match can span lines and inline elements. |
| `GetWordAt(position)`, `GetBlockAt(position)` | The range a double click and a triple click select. |

### Painting a selection and `::selection`

With `EnableSelectionStyling` (and `RecordTextRuns`) set, `document.SetSelectedRanges(ranges)` selects the text between pairs of positions and
`PaintPageAsync` paints it: the `background-color` of the document's `::selection` rules fills the selected characters and the spaces between
selected words, and the rule's `color` replaces the text colour inside the selection. `SetSelectedRanges(null)` clears it; repaint the pages
to show either change. Where no `::selection` rule applies, `HtmlDocumentOptions.SelectionBackground` (light blue) and
`SelectionForeground` (the text's own colour) are used. With the option off, `::selection` rules are ignored, as they are in a PDF.

- A rule applies to the text of the element it matches (`p::selection`, `.note::selection`, or a bare `::selection` for every element); an
  element with no rule of its own takes its nearest ancestor's.
- The `background-color` fills the whole line box behind the selected characters; `color`, `text-shadow` and `-webkit-text-stroke*` restyle the selected
  text, and a `text-decoration` the rule adds is drawn under it. `color` and `text-shadow` an ancestor's `::selection` declares are inherited by a child
  with rules of its own. A selection never changes layout, follows a CSS transform of its element, and is not painted in vertical writing modes.
- Where a selection changes the text colour, the selected word is drawn twice (once clipped to the selection), so a PDF written with a selection
  holds that word's text more than once. Selection is meant for interactive output, not for PDF.

### Named highlights: `::highlight()`, `::spelling-error`, `::grammar-error`, `::target-text`

With the same options, `document.SetHighlight(name, ranges, priority)` paints text in the rules of `::highlight(name)`, the way the CSS Custom
Highlight API does for ranges a page registers. The names `spelling-error`, `grammar-error` and `target-text` are the built-in highlights
(`::spelling-error` and so on), for what an embedder's spell checker or find-in-page marks. `SetHighlight(name, null)`, `RemoveHighlight(name)` and
`ClearHighlights()` remove them; `HighlightNames` lists them in painting order.

- Highlights are painted lowest first: the built-in ones, then the custom ones by `priority` and then by the order they were first set, then the
  selection over all of them. Where several change the text colour the one painted last wins, per stretch of the word it covers.
- A highlight no rule styles paints nothing (the selection, by contrast, has default colours). Rules apply as for `::selection`: to the text of the
  element they match, falling back to the nearest ancestor's; `::highlight( name )` is the same selector as `::highlight(name)`, and the name is
  case-sensitive.
- The same properties are painted as for the selection. A text edit removes every highlight, since the positions they hold no longer
  mean the same text.

`document.ApplyTextFragmentDirective(directive)` is what a browser does for a link with a text fragment: it finds the text a `text=[prefix-,]start[,end][,-suffix]`
directive names (case-insensitive, percent-decoded, the first match with its prefix and suffix around it) and paints it as `::target-text`. It returns the
range, or null and clears the highlight when nothing matches.

### The selection model

`document.Selection` (a `DocumentSelection`, needs `RecordTextRuns`) is the state a pointer and keyboard drive, in the manner of the DOM's
`Selection`: an `Anchor` and a `Focus` (the caret end) as `TextPosition`s, `IsCollapsed`, `IsBackward`, `GetRange()`, `GetText()`, and
`GetRegions()`/`GetCaretBounds()` for what to draw. It draws nothing itself; with `EnableSelectionStyling` it hands every change to
`SetSelectedRanges`, so the next paint shows it in the `::selection` style.

| To | Call |
| --- | --- |
| click, shift-click or drag | `SelectAt(page, x, y)`, `ExtendTo(page, x, y)` |
| double and triple click | `SelectWordAt(page, x, y)`, `SelectBlockAt(page, x, y)` |
| select a range, or everything | `Select(range)`, `SetBaseAndExtent(anchor, focus)`, `SelectAll()` |
| collapse, extend, clear | `Collapse`, `CollapseToStart/End`, `Extend`, `RemoveAllRanges` |
| arrow keys and their modifiers | `Modify(extend, direction, granularity)` |

`Modify` moves by `Character`, `Word`, `Line` (up and down, keeping the caret's horizontal position across lines), `LineBoundary` (Home and
End), `Block` (paragraph edges) or `Document`. Direction is logical - in right-to-left text the key that moves towards the left is a backward
move. A move that does not extend, over a selection, first collapses it to the near edge. It returns false at the edge of the document.

`SelectionChanged` fires after a change that moved the anchor or the focus, with the `DirtyRegions` - where the selection or caret was and
where it is - so only those regions need repainting (`PaintPageRegionAsync`). `document.GetElementAt(position)` gives the element a position's
text was written in, for a consumer that wants the DOM node behind a selection end.

Limits: text in vertical writing modes, in page margin boxes and in footnotes is not reported, and text moved by a `transform` is reported where
layout put it, not where it was drawn. The per-character positions come from shaping the word with its font and scaling to the width layout gave
it, so they follow ligatures and justification but not a custom `letter-spacing` applied unevenly.

## Restyling in place

A consumer that shows a document and changes it - a selection, a hover colour, an editor's highlight - should not load and lay out the
document again for every change. With `EnableIncrementalUpdates`, `document.SetStyle(element, property, value)` changes one CSS property of
an element of the laid-out document and returns an `UpdateResult`: whether it was applied, why not if it was not, and the `DirtyRegions`
(page index and a rectangle in points) that now look different. No layout pass runs, so nothing moves.

- Get the element with `GetElement(id)`, or `GetElementAt(page, x, y)` for the innermost element under a point (what a click hit).
- Applied in place: `color` (the element's descendants that inherited it follow; one with a colour of its own keeps it), `background-color`,
  `text-decoration-line`, `text-decoration-style`, `text-decoration-color` and `outline-color`. A border drawn in `currentcolor` follows a `color`
  change.
- Not applied, with the reason in `UpdateResult.Reason` and the document untouched: any other property (anything that can move or resize
  content needs a layout pass), a value that does not parse, and the `background-color` of `html` and `body`, which is the page canvas's.
- A background or outline set to `currentcolor` earlier keeps the colour it had; only a later write picks up the new one.
- To show the change, clear each region on the page's canvas and call `PaintPageRegionAsync(page, canvas, region)`, which paints only what
  lies inside it; or paint the page again.

## Animations and transitions

A PDF is one moment, so by default a document does not move. Set `HtmlDocumentOptions.AnimationsEnabled` and the document plays its CSS animations
and transitions and its animated images (GIF, WebP) over time, for a host that shows it on a screen. Nothing moves by itself: the host calls
`AdvanceAnimations(TimeSpan now)` once per frame with a clock that only increases (a display's frame timestamp), and repaints the regions it returns.

```csharp
var document = await HtmlDocument.LoadAsync(html, context, new HtmlDocumentOptions { MediaType = MediaType.Screen, AnimationsEnabled = true });
await document.LayoutAsync(measuring);

// Once per frame, while something is playing:
while (document.HasRunningAnimations)
{
    AnimationFrame frame = document.AdvanceAnimations(frameTimestamp);
    foreach (DirtyRegion region in frame.DirtyRegions)
        await document.PaintPageRegionAsync(region.PageIndex, canvas, region.Bounds);   // clear the region first
    if (!frame.IsRunning) break;
}
```

- **What plays.** `@keyframes` animations with their delay, iteration count, direction, fill mode, timing function and play state; transitions started by a
  change of `:hover`, `:active` or `:focus` state (see [Restyling in place](#restyling-in-place)); animated images at the frame durations and loop count the file
  records (an `<img>`; a `background-image` shows its first frame). `HasRunningAnimations` says whether another step is due, and an `AnimationFrame`'s `IsRunning` whether
  anything is still playing after this one: a paused animation, one that has ended and a document with none make no more frames, so a host stops its frame loop and starts
  it again when a state change or a newly loaded image makes `HasRunningAnimations` true.
- **What it costs.** `AdvanceAnimations(now)` plays the properties that cannot change layout (`transform`, `opacity`, `filter`, colours, shadows, outlines): a frame runs
  no layout and no cascade, just the sampling of what is playing and a repaint of the regions that changed.
- **Layout properties.** `AdvanceAnimations(now, measuringCanvas)` (or `AdvanceAnimationsAsync`) also plays the properties that move or resize boxes (`width`, `height`,
  `margin`, `padding`, `top`/`left`, `border-width`, `font-size`, flex and grid sizing, ...). A step that changes one lays the whole document out again on the canvas, so
  the geometry of every frame is what a document written with those values would have, a `font-size` reaches the boxes that inherit it, and the boxes after an animated one
  move with it. The `AnimationFrame` then has `LayoutChanged` set (`HtmlDocument.Pages` may have a new size; text runs, links and the outline are made again) and its
  `DirtyRegions` are the places where a box, line or word is not where it was, or the pages whole when they cannot be compared. The cost is the cost of laying out the whole
  document, so it follows the document's size, not the animated box's. Only a `MediaType.Screen` document without container queries plays them; any other plays what
  does not change layout. Properties that change what boxes there are (`display`, `position`, `float`, `content`) and custom properties do not play.
- **Retained layers.** A box that animates `transform` or `opacity`, and has no box inside it that does, is painted into a [`RetainedLayer`](peachdrawing-core.md#retained-layers)
  once and drawn again each frame with the new matrix and opacity, so a canvas backed by a GPU (see [`PeachDrawing.Win2D`](peachdrawing-win2d.md)) composites a texture and does
  not paint what is inside the box again. What a layer holds is recorded again when anything else about the page changes (a restyle, a reload). On a canvas with no better way,
  a layer is a recording replayed each frame.
- **Reduced motion.** The default `Preferences` report `prefers-reduced-motion: reduce`, so a style sheet that switches animation off for it does; pass
  `new UserPreferences { ReducedMotion = false }` along with `AnimationsEnabled` to let them play, and leave `AnimationsEnabled` off when the system asks for no motion.
- **A PDF.** `AnimationProgress` (a still frame, see [Animations](html-css-support.md#animations)) takes precedence and turns the clock off.

### Events and controlling the animations

- **Events.** Each `AnimationFrame` has `Events`, the events a browser would dispatch for what happened since the last step, in order, as `AnimationEventInfo` (`Kind`, `Element`, `Name`,
  `ElapsedTime`): `AnimationStart`, `AnimationIteration` (at the start of each iteration after the first), `AnimationEnd` and `AnimationCancel` (an animation that began and stopped before it
  ended because the element no longer names it, or it was cancelled), and for a transition `TransitionRun` (when it is created, even with a delay to wait), `TransitionStart`,
  `TransitionEnd` and `TransitionCancel` (replaced by a newer transition of the property, its element gone, or cancelled). `Name` is the `animation-name` or the property. An event
  caused by a call (a transition that a class change created, an animation `Cancel()`ed) is reported by the next step, and `HasRunningAnimations` is true until it has been. A
  WinUI `HtmlView` raises them as `AnimationEvents`.
- **Control.** `HtmlDocument.GetAnimations()` (or `GetAnimations(element)`) lists the animations and transitions that are running as `DocumentAnimation` objects, which follow
  Web Animations 1 §4.4: `Name`, `IsTransition`, `Element`, `PlayState` (`Idle`, `Running`, `Paused`, `Finished`), `CurrentTime` (seek by setting it, paused or not), `PlaybackRate` and
  `UpdatePlaybackRate` (negative plays backwards), `Play()`, `Pause()`, `Finish()` (an animation that repeats for ever has no end and throws), `Reverse()`, `Cancel()` (the element has the value
  it would have without the animation; a transition ends on the value the style has) and `Finished`, a task that completes when it finishes and is cancelled if it is cancelled or removed first.
  A change takes effect on the next step. The object stays the animation it was when the document is loaded again for a restyle, and is `Idle` once the element stops naming it. Seeking does
  not report the events of the time it skips over: the flags that say what was reported follow the new position.

### Scroll-driven animations

An animation with `animation-timeline: scroll()` or `view()` (or a name from `scroll-timeline-name`/`view-timeline-name`, with `timeline-scope` to reach it from elsewhere) takes its progress from how
the document is scrolled instead of from time, over the part of the scroll `animation-range` names (see [Animations](html-css-support.md#animations) for the grammar). The host says how it is scrolled
with `SetViewport` (as it already does for fixed and sticky boxes, see [the viewport](#the-viewport-fixed-and-sticky-boxes)); that wakes the clock for one step, so `HasRunningAnimations` is true
after a scroll and `AdvanceAnimations` (or `AdvanceAnimationsAsync`) then writes the new values and reports the regions to repaint. `AnimationFrame.IsRunning` is false afterwards: nothing runs on time,
so a host's frame loop idles between scrolls. A WinUI `HtmlView` does this itself on every scroll.

An animation on a scroll timeline plays over the distance the document can scroll; on a view timeline, over the part of the scroll where the element is in view (`cover`, or the named range). Animations on
these timelines send no events and ignore `CurrentTime`, `PlaybackRate`, `Finish()` and `Reverse()` of the control API (`Pause()` holds the last value, `Cancel()` removes the effect). Only the document scrolls:
a box with `overflow: auto` or `scroll` has no scroll position, so a timeline on it, or `scroll(nearest)` from inside one, does not exist and the animation has no effect.

### Animated SVG

An inline `<svg>` plays too (and so does a standalone SVG that an `<img>` or `<object>` shows, like an animated GIF, see [Standalone SVG images](supported-svg-features.md#standalone-svg-images)): the CSS animations of its elements and its SMIL animation elements (`animate`, `set`, `animateTransform`, `animateMotion`; see [SMIL animation](supported-svg-features.md#smil-animation)).
The clock starts them when the document loads, builds the graphic again at its time on every step and reports the `<svg>`'s rectangle as the region to repaint, so a page of which only one icon
spins repaints that icon. `AnimationFrame.IsRunning` stays true while a graphic still has something to play (an animation that repeats for ever, or one yet to end) and is false once every
animation of it has ended and is frozen or removed. A document loaded for a still frame (`AnimationProgress`) samples SMIL at that fraction of the span the animations cover instead.

`HtmlDocument.AnimationsWoke` is raised when the control API (`Play`, `CurrentTime`, `Finish`, `Cancel`, `Reverse`...) makes a clock that had stopped run again, so a host that stopped its frame loop when `HasRunningAnimations` went false starts it again; it is not raised while the clock is running or from a step, and it may arrive on any thread. A WinUI `HtmlView` handles it itself.

## Changing classes, attributes and preferences

A host that has no script engine changes what a loaded document looks like by changing the elements, as a page's script would, and the document is styled again:

| Call | Does |
| --- | --- |
| `AddClassAsync` / `RemoveClassAsync` / `ToggleClassAsync(element, className, canvas)` | Changes one class in the element's `class` attribute. |
| `SetAttributeAsync(element, name, value, canvas)` / `RemoveAttributeAsync(element, name, canvas)` | Sets or removes any attribute (`data-*`, `id`, `style`, `hidden`...). |
| `GetAttribute(element, name)` | Reads an attribute as the document has it now. |
| `SetPreferencesAsync(preferences, colorScheme, canvas)` | Changes what `prefers-color-scheme`, `prefers-reduced-motion`, `forced-colors` and the other user preferences report. |

Each returns an `UpdateResult` (`Applied`, and the `DirtyRegions` to repaint). Every rule that reads the attribute or media feature matches again: `.open`, `[data-state=open]`, `#id`, sibling
and descendant combinators, `@media (prefers-color-scheme: dark)`. The document is loaded from its source again with the change applied, so the element handles taken before are stale (a
handle still names its element, but take new ones from `GetElement` to be safe), what was selected stays only if the text did not change, and the change is kept: loading the document
again for a pointer or focus rule applies it too. A document built with the declarative API, or one whose text was edited in place, has no source to load again, and the call is not applied.

With `AnimationsEnabled`, every restyle that loads the document again (these calls, a `:hover` or `:focus` rule that moves something, a form state, `SetOpenAsync`) compares what each
element looked like before with what it looks like after, and a property whose value differs and that the element's `transition-property` names (`all`, the property, or a shorthand that sets it
such as `margin`) runs to its new value over `transition-duration` instead of snapping. A property that moves or resizes boxes (`width`, `margin`, `top`, ...) runs when the host steps the
document with a canvas (`AdvanceAnimationsAsync`); an inherited one (`font-size`) and a property a running animation animates snap. A transition of a pseudo-element (`::before`) does not run.

## Editing the text

With `EnableIncrementalUpdates` and `RecordTextRuns`, the text of the loaded document can be changed in place - the primitive a text editor
or a chat composer is built from - and the document is laid out again from the changed tree (from the changed page on, where it can be), with no re-parse and no new `HtmlDocument`:

| Call | Does |
| --- | --- |
| `ReplaceTextAsync(from, to, text, canvas)` | Replaces the text between two `TextPosition`s (either order) with `text`, which may be empty. |
| `InsertTextAsync(position, text, canvas)`, `DeleteTextAsync(from, to, canvas)` | The two halves of it. |
| `InsertTextAsync(element, text, canvas)` | Appends text to an element's own text, or gives an element with no text some - an empty paragraph has no position to insert at. |
| `SplitParagraphAsync(position, canvas)` | Enter: the text after `position`, and the inline elements it is inside, move to a new paragraph of the same element and style after this one. When the split is at the end the new paragraph is empty: `TextEditResult.Block` is it, for `InsertTextAsync(element, ...)`. |
| `JoinWithNextParagraphAsync(position, canvas)` | Delete at the end of a paragraph: the next paragraph's content moves to the end of this one and it is removed. The caret is where the two met. |
| `WrapRangeAsync(from, to, tag, attributes, canvas)` | Wraps the text between two positions in a new inline element (`strong`, `em`, `u`, `a`, `span`, `code`, `mark` and the like), as bold, italic or a link is applied. `TextEditResult.Block` is the element; the caret is at the end of the wrapped text. |
| `UnwrapElementAsync(element, canvas)` | Takes such an element out, leaving its text where it was and joining the text around it: turns bold or a link off. |

A range to wrap must lie in one run of text with the same parent: not across another inline element or across paragraphs (wrap each part, or unwrap the element
first). An element that is not one of those phrasing elements, an attribute that is not a plain name or starts with `on`, an empty range, and an element that holds
other elements or generated content (for unwrap) are refused. The text boxes involved are replaced by new ones, so the new element and what is around it lay out
exactly like the markup with it. A split paragraph's copy does not keep the `id` of the paragraph it came from.

Splitting and joining work on a plain paragraph only - a block in a block, with only inline content, no generated content, float or positioning -
and, for a join, two adjacent paragraphs of the same element; a table cell, flex or grid item, or a block that holds blocks is refused. A list item splits and joins like a paragraph, and the items after it are renumbered.
The paragraph a split or join leaves, the one it creates and the one after them are cascaded again, so `:first-child`, `:last-child` and adjacent-sibling rules match their new positions; `:nth-child` rules of paragraphs further on are not evaluated again.

`canvas` is a measuring canvas from the render context, as `LayoutAsync` takes. Each call returns a `TextEditResult`:

- `Applied` and `Reason`: when an edit cannot be made in place nothing is changed, and `Reason` says why.
- `PagesBefore`, `PagesAfter` and `FirstDirtyPage`: where the layout now differs. Every page from `FirstDirtyPage` on may look different (a
  paragraph that grows pushes everything after it), and `DirtyRegions` lists those pages whole. An edit on the last page leaves the earlier
  ones alone, apart from the page before it, which widows and orphans can reach.
- `Caret`: a `TextPosition` just after the inserted text in the new `TextRuns`. The document's `Selection` is collapsed to it, so typing
  is: insert at the caret, use the returned caret for the next key.

What an edit does to the document:

- A range inside one text, or across inline elements and paragraphs, is replaced between its ends. Text left empty has its text box removed (an
  emptied `<strong>` stays as an empty element). `ReplaceTextAsync` does not join blocks: deleting from the middle of one
  paragraph to the middle of the next leaves two paragraphs (use the split and join calls for that).
- Everything loading does to text is redone for the paragraphs the edit touches: bidirectional levels (which depend on the whole paragraph),
  collapsing of a space that continues across an inline boundary, soft hyphens, and the split into words. A test lays out an edited document and
  a fresh load of the same markup and requires the same pages, text runs and character positions.

Refused, with the reason in the result and the document untouched: a document whose layout depends on every page (footnotes, page floats,
`target-counter(page)`, named pages, running elements, multiple columns, container queries, pages of different widths); a document with a
`StructureTagSink` attached; a position in generated content (`::before`, markers, counters), in `svg` or `math`, or on a `<br>`; and a position
where layout cut a word in a way that cannot be undone (a small-caps split where a soft hyphen was removed). `InsertTextAsync(element, ...)` refuses an
element that holds blocks, an image or a form control.

Layout is picked up from the page before the one the edited paragraph starts on, and the pages before it are kept as they were, so an edit late in a
long document costs a fraction of a layout (1,500 paragraphs over 84 pages: about 50-80 ms on the last pages, against about 700 ms for one on the
first); `TextEditResult.ResumedFromPage` says where it picked up, or is null when the whole document was laid out again (an edit on the first pages, flex or grid content, or
another document the shortcut does not cover). An edit also discards `ClipReport` and any selection painted through `SetSelectedRanges`.

## Structure for tagged output

Set `document.StructureTagSink` to an `IStructureTagSink` before painting, and the engine reports the logical structure of what it paints:
a grouping element for a list or a section, a content element around the text and images of a paragraph, an artifact for decoration that
is not part of the structure. Each element is identified by an opaque `StructureNode`, equal for the same source element on every page, so a
paragraph that continues across a page break is one element. This is the seam a tagged PDF is built from: PeachPDF implements the sink with
`PdfTagRecorder` from [PeachDrawing.PDF](peachdrawing-pdf.md#tagged-pdf). Which tag a box maps to is governed by the
`-peachpdf-pdf-tag-type` property described in [HTML and CSS support](html-css-support.md).

## Text that was cut off

`document.ClippedText` lists the words that were painted but cut by an ancestor's `overflow` clip, with how much of each showed. Read it after
painting to learn whether content was silently lost, rather than discovering it in the output.

## Building a document in code

`HtmlDocument.LoadAsync` parses HTML. When your tree is not HTML - it comes from a template, a model or a fluent builder - build it directly
instead, with the types in `PeachBrackets.Building`:

```csharp
var context = new DocumentBuildContext(renderContext);
var page = context.CreatePage();

var heading = page.Root.AppendBlock();
heading.SetStyle("font-size", "24pt");
heading.AppendText("Quarterly report");

var sheet = await StyleSheet.ParseAsync(renderContext, ".note { color: #555 }");
using var document = await HtmlDocument.CreateAsync(page, new HtmlDocumentOptions(), sheet);
```

A `DocumentNode` is one box of the tree. `AppendBlock`, `AppendText`, `AppendElement`, `AppendListItem`, `AppendImage` and
`AppendHtmlFragment` add children; `SetStyle` writes a CSS property, `SetAttribute`/`AddClass` give a node the `id` and classes a style sheet
selects on, and `SetImageContent`/`SetSvgContent` supply a decoded image, an `SvgImage`, or a function that produces either at the size the
layout decides. `DeclarativePage.AddMarginBox` fills one of the page's margin boxes, and `Overrides` records which of the page size and margins
your code fixed explicitly, so a style sheet's `@page` rule does not replace them. The result lays out and paints exactly like a loaded
document.

## Fetching resources side by side

A document asks for its images and web fonts one at a time, as layout reaches them. A render context that implements `IResourcePrefetcher` is told
their addresses first and can fetch them all at once; `HostResourceLoader.PrefetchAsync` does that (a few requests at a time) into the loader's response
cache, so the requests that follow are answered from memory. `HtmlView` does this for you; a host with its own render context implements the interface
the same way, or ignores it, and nothing changes.

## Timing a load

The load pipeline reports where its time goes as [`Activity`](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.activity) spans of
an [`ActivitySource`](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.activitysource) named `PeachBrackets`. Nothing is recorded,
and starting a span costs a null check, until something listens (an `ActivityListener`, OpenTelemetry, `dotnet-trace`):

```csharp
ActivitySource.AddActivityListener(new ActivityListener
{
    ShouldListenTo = source => source.Name == "PeachBrackets",
    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    ActivityStopped = activity => Console.WriteLine($"{activity.OperationName}: {activity.Duration.TotalMilliseconds:0} ms"),
});
```

| Span | Covers |
|---|---|
| `html.parse` | Parsing the HTML into the box tree. |
| `css.collect` | Fetching and parsing the document's style sheets. |
| `css.scan` | Looking through the selectors for the interaction states they use. |
| `fonts` | Fetching and registering the web fonts. |
| `css.cascade` | Matching the rules to every element and computing styles. |
| `layout` | Laying the document out (images, which load while it is measured, are inside it). |
| `resource.fetch` | One web request made by `HostResourceLoader`, tagged `url`, `cache` (`hit` or `miss`), `status` and `bytes`. |
| `images.prefetch` | Fetching the document's images (and web fonts) side by side before layout, when the render context implements `IResourcePrefetcher`. |
| `image.load` | One image, tagged `url`. |

The spans nest (an image loads while the page is laid out, and a request is inside what asked for it), so their durations are not meant to add up to
the load time; they say which phase the time is in.

## Font and URI helpers for a host

A `RenderContext` that wants the HTML engine's font behaviour (the CSS generic families, `system-ui`, `font-variation-settings`) needs three
small things, which `FontDefaults` provides: `DefaultFamily` and `DefaultSizePoints`, `GenericFamilies` (each CSS keyword with the generic
family it stands for) and `ToAxisSettings` (the encoded `font-variation-settings` as font axes). `DataUri.TryDecode` decodes a `data:` URI,
percent-decoding before base64-decoding as the URL standard does, and `PeachBrackets.Svg.SvgGlyphPainter` is the `ISvgGlyphPainter` that paints
SVG-in-OpenType glyphs onto the canvas a `RenderContext` hands out.

## Using it from PeachPDF

You do not need this package to render HTML to PDF - [PeachPDF](getting-started.md) depends on it and drives it for you, writing the pages
through [PeachDrawing.PDF](peachdrawing-pdf.md). Reach for `PeachBrackets` directly to render HTML to something else (a preview bitmap, a
different document format, your own canvas), or to inspect how a document paginates and what its links and outline are.

## Licences

`PeachBrackets` is BSD 3-Clause. The CSS parser descends from [ExCSS](https://github.com/TylerBrinks/ExCSS) and the layout engine from
[HTML Renderer](https://github.com/ArthurHub/HTML-Renderer); their notices ship in the package. See [License](license.md) for the whole list.
