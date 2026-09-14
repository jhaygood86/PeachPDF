# Declarative Document-Building API

`PdfGenerator.CreateDocument` builds a PDF directly from C# — no HTML, no CSS strings — for callers who want a code-first, fluent API instead of authoring markup. It's built entirely on PeachPDF's own HTML/CSS layout engine (flexbox for rows and columns, the real table layout algorithm for tables, the same list-marker and running-header/footer machinery HTML documents use), so a value like `PdfLength.Points(12)` or `PdfColor.FromRgb(200, 0, 0)` resolves through exactly the same length/color parsing PeachPDF already uses for HTML — this is a different way to *construct* a document, not a second rendering engine. For the underlying HTML/CSS feature set this API is built on, see [HTML & CSS Support](html-css-support.md); for everyday HTML-string rendering, see [Usage Examples](usage-examples.md).

All examples assume:

```csharp
using PeachPDF;
using PeachPDF.Layout;
```

## Contents

- [Quick start](#quick-start)
- [Pages: size, margin, background](#pages-size-margin-background)
- [Containers: padding, border, background, corner radius, shadow](#containers-padding-border-background-corner-radius-shadow)
- [Text and rich text](#text-and-rich-text)
- [Images](#images)
- [Hyperlinks and bookmarks](#hyperlinks-and-bookmarks)
- [Rows and columns](#rows-and-columns)
- [Tables](#tables)
- [Lists](#lists)
- [Headers, footers, and page numbers](#headers-footers-and-page-numbers)
- [Known v1 limitations](#known-v1-limitations)

## Quick start

```csharp
var generator = new PdfGenerator();

var document = await generator.CreateDocument(doc =>
{
    doc.Page(page =>
    {
        page.Size(PageSize.A4);
        page.Margin(20);
        page.Content(container =>
        {
            container
                .Padding(20)
                .Border(1, PdfColor.FromHex("#CCCCCC"))
                .CornerRadius(8)
                .Column(column =>
                {
                    column.Spacing(10);
                    column.Item().Text("Hello, declarative PeachPDF!");
                    column.Item().Text(t =>
                    {
                        t.Span("Bold red text").FontColor(PdfColor.Red).Bold();
                    });
                });
        });
    });
});

var stream = new MemoryStream();
document.Save(stream);
```

`doc.Page(...)` can be called more than once to append further pages, and `PdfGenerator.AddPages` adds more pages to a `PeachPdfDocument` you already have (mirroring `AddPdfPages` on the HTML side). A document built with no `PdfGenerateConfig` at all defaults to A4 with 20pt margins; pass one to `CreateDocument`/`AddPages` for PDF metadata, PDF/A conformance, tagged PDF, compression, or a custom network loader — every `PdfGenerateConfig` option that isn't specific to HTML parsing applies here too.

## Pages: size, margin, background

```csharp
page.Size(PageSize.Letter);                       // a named page size...
page.Size(PdfLength.Millimeters(210), PdfLength.Millimeters(297)); // ...or an explicit one
page.Orientation(PageOrientation.Landscape);
page.Margin(20);                                   // all four sides
page.MarginTop(30);                                // one side at a time; also MarginBottom/Left/Right/Horizontal/Vertical
page.Background(PdfColor.White);
page.DefaultTextStyle(style => style.FontFamily("Georgia", "serif").FontSize(11));
```

Any geometry a page doesn't set falls back to the document's own `PdfGenerateConfig`, the same way an HTML document's `@page` rule falls back to it.

## Containers: padding, border, background, corner radius, shadow

`IContainer` is the one composable box used everywhere content is placed — a page's content area, a `Row`/`Column` item, a table cell. Every decorator wraps the current box and returns the new, innermost container, so they chain:

```csharp
container
    .Padding(16)
    .Border(2, PdfColor.FromRgb(30, 30, 30))
    .CornerRadius(12)
    .Background(PdfColor.FromRgb(245, 245, 245))
    .BackgroundLinearGradient(45, PdfColor.FromHex("#FFDEE9"), PdfColor.FromHex("#B5FFFC"))
    .Shadow(new PdfBoxShadow(PdfColor.FromArgb(60, 0, 0, 0), offsetX: 0, offsetY: 4, blur: 12))
    .Width(300)
    .Text("A card");
```

`BorderLinearGradient(width, angleDegrees, stops)` paints a gradient border instead of a solid one. Lengths accept a bare number (points) or an explicit unit — `PdfLength.Pixels(16)`, `PdfLength.Inches(1)`, `PdfLength.Percent(50)`, `PdfLength.Em(1.5)` — resolved through PeachPDF's own CSS length parser.

## Text and rich text

```csharp
container.Text("Plain paragraph text.");

container.Text(t =>
{
    t.Alignment(TextAlignment.Justify);
    t.Span("Regular, ");
    t.Span("bold").Bold();
    t.Span(", and ");
    t.Span("italic red").Italic().FontColor(PdfColor.Red);
    t.Span(" text, plus ");
    t.Span("underlined").Underline().DecorationColor(PdfColor.Blue).DecorationStyle(PdfTextDecorationStyle.Wavy);
    t.Span(" and a ");
    t.Span("superscript").Superscript();
    t.Span(" footnote marker.");
});
```

`ITextStyle` (shared by `DefaultTextStyle` and every span) also covers `FontWeight(int)` (a raw 1–1000 weight, not just `Bold()`), `LetterSpacing`/`WordSpacing`, `LineHeight`, `Subscript()`, `FontFeature(tag, enabled)` for OpenType features, `Direction(PdfTextDirection.Rtl)` for right-to-left paragraphs, and `BreakAnywhere()`. A container itself has `ParagraphFirstLineIndentation`, `ParagraphSpacing`, and `ClampLines(n)` to truncate a block after a fixed number of lines with an ellipsis. `ITextSpanContainer.Element(content => ...)` inserts arbitrary container content — an image, a styled box — inline between spans.

## Images

```csharp
container.Image(imageBytes);                 // byte[]
container.Image(stream);                     // Stream, read fully
container.Image(new Uri("https://example.com/logo.png"));
container.Image(@"C:\assets\logo.png");      // local file path

// Reuse the same resolved image in more than one place:
var logo = PdfImage.FromFile(@"C:\assets\logo.png");
container.Image(logo);
otherContainer.Image(logo);
```

Byte/stream sources are wrapped as an inline `data:` URI, so no network access is needed to place them; sizing/fit uses the ordinary `Width`/`Height` decorators on the container wrapping the image.

## Hyperlinks and bookmarks

```csharp
container.Hyperlink("https://peachpdf.net/").Text("Visit our site");

container.Bookmark("Chapter One", level: 1).Text("Chapter One");
```

`Hyperlink` produces a real PDF link annotation; `Bookmark` adds a real PDF outline (sidebar) entry, at the given nesting level (1 = top level) — both reuse the same generic link/bookmark machinery an HTML `<a href>`/heading already gets.

## Rows and columns

Backed by PeachPDF's flexbox layout engine:

```csharp
container.Row(row =>
{
    row.Spacing(8);
    row.Item().Width(80).Height(40).Background(PdfColor.Red);
    row.Item().Grow().Height(40).Background(PdfColor.Green); // fills remaining space
    row.Item().Width(80).Height(40).Background(PdfColor.Blue);
});

container.Column(column =>
{
    column.Spacing(6);
    column.Item().Text("First line");
    column.Item().Text("Second line");
});
```

`Grow(ratio)` maps to `flex-grow`; combine it with an explicit `Width`/`Height` on sibling items for a "fixed items plus one flexible item" layout.

## Tables

Backed by the real CSS table layout algorithm, including colspan/rowspan:

```csharp
container.Table(table =>
{
    table.Columns(columns =>
    {
        columns.FixedColumn(80);   // an absolute width
        columns.RelativeColumn(2); // shares the remaining width, weighted against other relative columns
        columns.RelativeColumn(1);
    });
    table.Header(header =>
    {
        header.Cell().Text("ID");
        header.Cell().Text("Name");
        header.Cell().Text("Amount");
    });
    table.Row(row =>
    {
        row.Cell().Text("1");
        row.Cell(columnSpan: 2).Text("A row spanning two columns");
    });
});
```

`Header`/`Footer` repeat on every physical page the table spans, exactly like a real `<thead>`/`<tfoot>`.

## Lists

Backed by PeachPDF's own `list-style-type`/`list-style-position`/`list-style-image` support, which already covers dozens of numbering systems:

```csharp
container.OrderedList(list =>
{
    list.Item().Text("First");
    list.Item().Text("Second");
}, PdfListMarkerType.UpperRoman); // → I., II., ...

container.UnorderedList(list =>
{
    list.Position(PdfListMarkerPosition.Inside);
    list.Item().Text("Bulleted");
}, PdfListMarkerType.Square);
```

`IListDescriptor.MarkerImage(...)` uses an image as every item's marker instead of a bullet/number; `MarkerText("→ ")` uses a literal custom marker string.

## Headers, footers, and page numbers

```csharp
page.Header(header =>
{
    header.Text("My Document");
});
page.Footer(footer =>
{
    footer.Text(t =>
    {
        t.Alignment(TextAlignment.Center);
        t.Span("Page ");
        t.CurrentPageNumber();
        t.Span(" of ");
        t.TotalPages();
    });
});
```

`Header`/`Footer` content repeats at the top/bottom of every physical page the page's own content paginates across (the same `position: running()` + `@page` margin-box mechanism an HTML document uses for repeating headers). `CurrentPageNumber()`/`TotalPages()` only resolve inside `Header`/`Footer` content — a page number has no meaning in ordinary flowing content.

## Known v1 limitations

- **No standalone SVG or `Placeholder` element yet.** Both are planned; `Placeholder` needs new paint code (it has no CSS mapping at all), while standalone SVG just needs wiring to PeachPDF's existing SVG support.
- **No sectioned page numbers** (a page count scoped to/counted from a named section) — only document-wide `CurrentPageNumber()`/`TotalPages()`.
- **No dashed-line pattern** for `LineHorizontal`/`LineVertical` — only a solid fill or a gradient.
- **`PdfTextDirection` has no `Auto`** — only explicit `Ltr`/`Rtl`.
- **`ClampLines` always uses the default ellipsis** — there's no way to supply a custom truncation string.
- **No callback-driven raster image generation, per-image compression/DPI override, or `ShrinkToFit`/`ScaleToPageSize`** for a declarative document — each of these needs a caller-provided image at a pixel size or a re-run of the whole builder callback that a hand-built tree has no equivalent for; document-wide settings on `PdfGenerateConfig` (`DownscaleImages`, `PixelsPerInch`, ...) still apply.
