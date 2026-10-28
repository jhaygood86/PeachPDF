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

`Pages` lists the laid-out pages: each has its `Index` and the size of its sheet in points. A page's size can differ from its neighbours'
when `@page` rules say so, which is why you read it per page rather than assuming one size.

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
| `Media` | The media type `@media` queries see. `print` by default. |
| `ColorScheme` | What `prefers-color-scheme` reports: `Light` (the default) or `Dark`. |
| `UserStyleSheet` | CSS applied after the built-in defaults and before the document's own style sheets. |
| `IgnoreAuthorStyleSheets` | Drop the document's own `<style>` and `<link rel=stylesheet>` rules, leaving the defaults and `UserStyleSheet`. |
| `DefaultLanguage` | The language to assume when the document declares none; it drives hyphenation and language-dependent line breaking. |
| `AnimationProgress` | Paint the document at a point in its animations, from 0 to 1, instead of its static look. |
| `SnapBoxDecorationsToCssPixels`, `SnapBorderWidthsToCssPixels` | Snap backgrounds, borders and border widths to whole CSS pixels, as a browser does. |
| `CollectFormFields` | Collect form controls into `Annotations.FormFields`. |
| `RecordTextRuns` | Let the document report where its text is (`TextRuns`), for selection, copy and find. |
| `EnableIncrementalUpdates` | Allow `SetStyle` to restyle elements, and `ReplaceTextAsync` and its relatives to edit the text, of the laid-out document in place. |
| `EnableSelectionStyling`, `SelectionBackground`, `SelectionForeground` | Paint a selection (`SetSelectedRanges`) in the document's `::selection` style, or in these colours where no rule applies. Needs `RecordTextRuns`. |

The options are read when the document is loaded.

## What the pages hold besides pixels

`document.Annotations` describes the parts of the document a reader of the pages can act on, positioned on their pages in points from the
top-left corner of the sheet. A consumer that writes a format with these concepts carries them over; one that only draws can ignore them.

- **`PaintLinks(pageIndex, canvas)`** puts a page's links on a canvas as `Canvas.AddLink`/`AddDestinationLink`/`AddDestination` calls (see
  [Links and destinations](peachdrawing-core.md#links-and-destinations)), so a canvas for a format with hyperlinks - the PDF and SVG ones - writes
  them without the consumer reading `Links` itself. Call it for every page *after all the pages have been painted*: where a link sits under a
  transform is only known once its page has been painted, and a link can lead to a later page. It also hands each link's handle to the
  `StructureTagSink`, if there is one, so a tagged format can tie the link to the content it covers.
- **`Links`** - each `PageLink` has its `PageIndex` and `Bounds`, an optional four-corner `Quad` when the link sits under a transform, and
  either a `Uri` or an `Anchor` with the `Destination` (`PagePosition`: page index and distance from the top) it leads to.
- **`Bookmarks`** - the outline: each `Bookmark` has a `Level`, a `Title`, whether it starts open, and a `Uri` or `Destination`. Headings
  produce them by default; `bookmark-level`, `bookmark-label` and `bookmark-target` change that.
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
- Only `color` and `background-color` are painted so far, and the highlight is as tall as the word's own line height. A selection never changes
  layout.
- Where a selection changes the text colour, the selected word is drawn twice (once clipped to the selection), so a PDF written with a selection
  holds that word's text more than once. Selection is meant for interactive output, not for PDF.

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

Splitting and joining work on a plain paragraph only - a block in a block, with only inline content, no generated content, float or positioning -
and, for a join, two adjacent paragraphs of the same element; a list item, table cell, flex or grid item, or a block that holds blocks is refused.
The new paragraph takes the old one's computed style as it is rather than being cascaded again, so `:nth-child` and `:last-child` rules are not evaluated for it. A paragraph with an `id` leaves two paragraphs with that `id`.

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
