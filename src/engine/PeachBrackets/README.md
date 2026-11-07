# PeachBrackets

The HTML, CSS, SVG and MathML engine behind [PeachPDF](https://peachpdf.net/), as a library of its own. It parses a document,
resolves the cascade, lays it out into pages and paints each page onto any
[PeachDrawing.Core](https://www.nuget.org/packages/PeachDrawing.Core/) `Canvas`: a PDF page, a bitmap, an SVG, or a canvas you
implement. It runs in-process in pure .NET (net10.0, plus net11.0), with no browser and no external tool, and it does not
depend on the PDF writer. It is trimmable and Native AOT compatible, and versioned in lockstep with PeachPDF.

```bash
dotnet add package PeachBrackets
dotnet add package PeachDrawing       # a canvas to paint on; this README uses its bitmap one
```

## Quick start: HTML to a PNG per page

```csharp
using PeachBrackets;
using PeachDrawing;
using PeachImage.Formats.Png;

var context = new RasterRenderContext();
await using (var font = File.OpenRead("SourceSans3-Regular.ttf"))
{
    await context.AddFont(font, "Source Sans");   // fonts the document names must be registered before loading
}

var html = """
    <html><body style="font-family: 'Source Sans'">
      <h1>Hello, PeachBrackets</h1>
      <p style="background: #fde8c8; padding: 6pt">Laid out by the engine that powers PeachPDF.</p>
    </body></html>
    """;

// 1. Parse and cascade.
using var document = await HtmlDocument.LoadAsync(html, context, new HtmlDocumentOptions
{
    PageWidthPoints = 400,
    PageHeightPoints = 300,
});

// 2. Lay out into pages. The canvas only measures text; nothing is drawn on it.
using (var measuring = context.CreateCanvas(1, 1))
{
    await document.LayoutAsync(measuring);
}

// 3. Paint each page onto a canvas you provide.
foreach (var page in document.Pages)
{
    using var canvas = context.CreateCanvas((int)page.WidthPoints, (int)page.HeightPoints);
    await document.PaintPageAsync(page.Index, canvas);

    await using var file = File.Create($"page-{page.Index + 1}.png");
    await canvas.SaveAsync(file, "png", new PngEncoderOptions());
}
```

Every length the API reports is in points (1/72 inch). Pages can be painted in any order, any number of times, and each can
have its own size when `@page` rules say so.

## Main features

**Options.** `HtmlDocumentOptions` is read at load time: the sheet (`PageWidthPoints`, `PageHeightPoints`, margins; an `@page` rule
overrides them), `MediaType` (`Print` paginates; `Screen` lays out one continuous surface), `ColorScheme`, `DevicePixelRatio`,
`UserStyleSheet`, `DefaultLanguage`, `Preferences` (reduced motion, contrast, forced colors), `FontScale` and `TextSpacing`.

```csharp
var options = new HtmlDocumentOptions
{
    MediaType = MediaType.Screen,
    ColorScheme = ColorSchemePreference.Dark,
    UserStyleSheet = "body { margin: 0 }",
};
```

**Synchronous layout.** A host that cannot wait (a control that must know its size during layout) sets `SynchronousOnly = true` for
a document whose images, style sheets and fonts are inline or already registered, and uses `HtmlDocument.Load`, `Layout` and
`PaintPage`. A document that then needs something that would really suspend throws `InvalidOperationException`.

**Links, outline, forms and structure.** `document.Annotations` reports each page's links (`Links`), the outline (`Bookmarks`), requested
annotations and, with `CollectFormFields`, form controls, so a consumer that writes a format with those concepts can carry them over.
`PaintLinks(pageIndex, canvas)` hands the links to a canvas that writes them (PDF, SVG), after all pages are painted.
`StructureTagSink` receives the logical structure for tagged output, and `ClippedText` lists words that were cut off.

```csharp
foreach (var link in document.Annotations.Links)
    Console.WriteLine($"page {link.PageIndex + 1}: {link.Uri ?? link.Anchor}");
```

**Showing the document on a screen.** With `MediaType.Screen` you can scroll, hit-test and update the document in place:
`GetElementAt`, `GetCursorAt` and `GetInteractionAt` answer what is under a point; `SetPointerStateAsync` applies `:hover` and
`:active`; `SetStyle` (with `EnableIncrementalUpdates`) restyles an element and returns the regions to repaint; `AnimationsEnabled`
plus `AdvanceAnimations` plays CSS animations, transitions and animated images; `PaintPageRegionAsync` paints only an invalidated
rectangle. The ready-made views built on this are `PeachBrackets.WinUI`, `PeachBrackets.Android` and `PeachBrackets.Apple`.

**Text: selection, copy and find.** `RecordTextRuns` lets the document report where its text is. `HitTest`, `GetWordAt`, `FindText`,
`SetSelectedRanges` and `GetText` give selection, copy and find; with `EnableSelectionStyling` the selection paints in the document's
`::selection` style. `ReplaceTextAsync` and its relatives edit text in place.

**Form controls.** `PaintFormControls = true` draws check boxes, text fields, buttons, drop-downs and ranges from their CSS for a host
that shows the page; `Activate`, `InsertText`, `SetValue` and `CreateFormSubmission` drive them.

**Accessibility.** `GetAccessibilityTree()` describes the page as roles, names and states for a screen reader, and
`GetAccessibilityNodeAt` finds the node under a point.

**Building a document in code.** When the tree comes from a template or model rather than HTML, build it with
`PeachBrackets.Building` (`DocumentBuildContext`, `DocumentNode`, `StyleSheet`) and call `HtmlDocument.CreateAsync(page, options, sheet)`.

**Errors.** Engine failures throw `PeachPDF.HtmlRenderException`; its `RenderErrorType` names the pipeline phase.

## Limits

A document is laid out once; to change it afterwards use the in-place update calls or load it again. Fonts a document names must be
registered on the render context (or installed) before loading. Support for individual elements, properties, selectors and at-rules
is listed in the [HTML and CSS support](https://peachpdf.net/html-css-support.html), [SVG](https://peachpdf.net/supported-svg-features.html)
and [MathML](https://peachpdf.net/supported-mathml-features.html) matrices.

## More

- [PeachBrackets guide](https://peachpdf.net/peachbrackets.html): the pipeline, every option, selection, animations, extension seams.
- [API reference](https://peachpdf.net/api/PeachBrackets/).
- To render HTML to a PDF file, use [PeachPDF](https://www.nuget.org/packages/PeachPDF/), which drives this package for you.

BSD 3-Clause. The CSS parser descends from ExCSS and the layout engine from HTML Renderer; their notices are in
`THIRD-PARTY-LICENSES.md` in the package.
