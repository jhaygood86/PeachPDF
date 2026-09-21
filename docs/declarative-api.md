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
- [Class, Id, Tag, and a document-level stylesheet](#class-id-tag-and-a-document-level-stylesheet)
- [Text and rich text](#text-and-rich-text)
- [Images](#images)
- [HTML fragments and slots](#html-fragments-and-slots)
- [Hyperlinks and bookmarks](#hyperlinks-and-bookmarks)
- [Rows and columns](#rows-and-columns)
- [Tables](#tables)
- [Lists](#lists)
- [Headers, footers, and page numbers](#headers-footers-and-page-numbers)
  - [Sectioned page numbers](#sectioned-page-numbers)
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

`doc.Page(...)` can be called more than once to append further pages, and `PdfGenerator.AddPages` adds more pages to a `PeachPdfDocument` you already have (mirroring `AddPdfPages` on the HTML side). A document built with no `PdfGenerateConfig` at all defaults to A4 with 20pt margins; pass one to `CreateDocument`/`AddPages` for PDF metadata, PDF/A conformance, tagged PDF, compression, or a custom network loader — every `PdfGenerateConfig` option that isn't specific to HTML parsing applies here too (PDF/A conformance, file attachments and [ZUGFeRD / Factur-X e-invoices](usage-examples.md#zugferd--factur-x-e-invoices) among them, set on the config exactly as for an HTML document).

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

`LineHorizontal(thickness, color)`/`LineVertical(thickness, color)` place a simple rule — a solid-filled line by default, or `dashed: true` for a dashed one:

```csharp
container.LineHorizontal(1, PdfColor.FromHex("#BBBBBB"), dashed: true);
```

## Class, Id, Tag, and a document-level stylesheet

`Class`/`Id`/`Tag` tag a container the same way an HTML element's `class`/`id`/tag name would, so a stylesheet
attached to the whole document (`IDocumentBuilder.Stylesheet`) can target it with a real CSS selector:

```csharp
var stylesheet = await generator.ParseStyleSheet("""
    .card { border: 1px solid #ccc; }
    .card .title { font-weight: 700; }
    #hero { background: #f5f5f5; }
    """);

var document = await generator.CreateDocument(doc =>
{
    doc.Stylesheet(stylesheet);
    doc.Page(page =>
    {
        page.Content(container =>
        {
            container.Id("hero").Class("card").Column(column =>
            {
                column.Item().Class("title").Text("Card title");
            });
        });
    });
});
```

Selector matching is real and general - class, id, compound, descendant, attribute, and `:not()`/`:is()`-style
selectors all work, the same selector engine HTML rendering uses. `Tag(name)` renames a container's own internal
tag (every anonymous container defaults to a synthetic `"div"`, an image to `"img"`, a hyperlink to `"a"`) so a
bare type selector like `li { ... }` can target it precisely - without a rename, a type/universal selector still
matches this API's own internal structure (`div`/`img`/`a`), so prefer class/id/`Tag` for predictable targeting.

An explicit builder call always outranks a plain (non-`!important`) stylesheet rule for the same property - the
same precedence an inline `style=""` attribute has over an author stylesheet - but a matching `!important` rule
still wins, matching ordinary CSS:

```csharp
// stylesheet: .card { padding: 8pt; }
container.Class("card").Padding(20);   // ends up 20pt - the explicit call wins
```

`IDocumentBuilder.Stylesheet` can be called anywhere in the document-building callback (`Page` only records its
own handler; every page's content tree, and this stylesheet's effect, are built afterward) - only the last call
is kept. `Class`/`Id`/`Tag` have no visual effect at all when no stylesheet is attached; they're a stable hook a
document can add before it needs styling.

A stylesheet's `@font-face`/`@property`/`@font-palette-values` rules take effect too - fonts register, custom
properties resolve through `var()` - with the same precedence rule extended to `@page`: a base `@page` rule's
`margin`/`size` only fills in whatever the page's own `Margin*`/`Size` call left unset, ranking between an
explicit call and the document's `PdfGenerateConfig` default. `PageName(name)` sets the CSS `page` property
(CSS 2.1 §13.2) so a document-level `@page name { ... }` rule can target this point in the flow onward:

```csharp
var stylesheet = await generator.ParseStyleSheet("@page chapter { size: landscape; }");

column.Item().PageName("chapter").Text("A landscape chapter starts here.");
```

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

`ITextStyle` (shared by `DefaultTextStyle` and every span) also covers `FontWeight(int)` (a raw 1–1000 weight, not just `Bold()`), `LetterSpacing`/`WordSpacing`, `LineHeight`, `Subscript()`, `FontFeature(tag, enabled)` for OpenType features, `Direction(PdfTextDirection.Rtl)` for right-to-left paragraphs (or `PdfTextDirection.Auto` to detect it from the span's own text — the same first-strong-character detection `dir="auto"` uses on the HTML side), and `BreakAnywhere()`. A container itself has `ParagraphFirstLineIndentation`, `ParagraphSpacing`, and `ClampLines(n, ellipsis)` to truncate a block after a fixed number of lines, marking the cut with the usual "…" by default, a custom string (e.g. `ClampLines(2, "… [continued]")`), or nothing at all (`ClampLines(2, "")`). `ITextSpanContainer.Element(content => ...)` inserts arbitrary container content — an image, a styled box — inline between spans.

## Images

```csharp
container.Image(imageBytes);                 // byte[] - raster or SVG, detected automatically
container.Image(stream);                     // Stream, read fully
container.Image(new Uri("https://example.com/logo.png"));
container.Image(@"C:\assets\logo.png");      // local file path

// Reuse the same resolved image in more than one place - decoded once, not once per placement:
var logo = PdfImage.FromFile(@"C:\assets\logo.png");
container.Image(logo);
otherContainer.Image(logo);
```

`Image(byte[])`/`Image(Stream)` decode directly into the image the container places (no `data:` URI round trip); `Image(Uri)`/`Image(string filePath)` still load lazily, over the network or from disk. `PdfImage.FromFile`/`FromBytes`/`FromStream` load once and cache the decoded result, so placing the same `PdfImage` instance in more than one container (even across pages) decodes its source only the first time.

An image placed with `Image` sizes itself from its own intrinsic dimensions (the raster's pixel size, or an SVG's `width`/`height`/`viewBox`) unless the image itself - not the container it's chained onto - is given an explicit size; `Width`/`Height` called before `Image`/`Svg` sizes the *container* the image sits in, which the image doesn't automatically stretch to fill (ordinary CSS replaced-element sizing - the same as an `<img>` with no `width`/`height` of its own inside a sized `<div>`). To make image content fill a container of a known size, generate or author it at that size directly, or use the dynamic overloads below, which do fill by default.

### Standalone SVG

```csharp
container.Svg(svgMarkup);   // string
container.Svg(stream);      // Stream, read fully
container.Svg(svgBytes);    // byte[]
```

Parses SVG markup directly into a real vector image, painted with PeachPDF's own SVG renderer - the same one an inline `<svg>` or `<img src="x.svg">` already uses on the HTML side, so anything on the [SVG feature matrix](supported-svg-features.md) works here too.

### Dynamic (size-aware) content

```csharp
container.Width(300).Height(160).Image(size =>
{
    // size.Width/size.Height are the container's own resolved point size - generate content
    // at exactly that size instead of a guessed fixed one.
    return RenderChartAsPng((int)size.Width, (int)size.Height);
});

container.Width(300).Height(160).Svg(size => RenderChartAsSvgMarkup(size.Width, size.Height));
```

`Image(Func<PdfSize, byte[]>)`/`Svg(Func<PdfSize, string>)` run their callback once layout knows the container's resolved size, and - unlike the other overloads above - fill that container by default (`width: 100%; height: 100%`), so the generated content is sized to fit rather than needing its own explicit dimensions. The container itself still needs a definite resolved size for this to mean anything: an explicit `Width`/`Height`, or an ancestor that already has one (the page content area, a table cell with a definite column width, a `Row`/`Column` item with an explicit size); an indefinite container throws `InvalidOperationException` rather than generating content at a garbled or zero size. Each callback runs exactly once, even across the layout engine's own internal convergence passes.

## HTML fragments and slots

`Html(markup)` parses an HTML fragment (through the same parser and cascade the HTML-string rendering path
uses) and splices it into a container's tree in place:

```csharp
container.Html("""
    <table>
      <tr><td>Row 1</td><td>Data</td></tr>
      <tr><td>Row 2</td><td>Data</td></tr>
    </table>
    """);
```

Unlike every other terminal method here, a fragment can contain more than one top-level element - real HTML
semantics apply throughout (UA default styles, an anonymous-table fixup for a bare `<tr>`, the fragment's own
`<style>` tag). Pass a stylesheet for the fragment's own class/id-driven styling to cascade against:

```csharp
var cardStyles = await generator.ParseStyleSheet(".highlight { background: yellow; }");
container.Html("""<p class="highlight">Important</p>""", cardStyles);
```

A `<link rel="stylesheet">` inside the fragment is never loaded (there is no document context to resolve it
through at this point in building the tree) - author a `<style>` tag, or pass CSS via the stylesheet parameter,
instead. `Stream`/`byte[]` overloads exist alongside the `string` one, matching `Svg`'s own shape.

### Slots

A fragment can declare replaceable `<slot>` points, Web-Components style:

```csharp
container.Html(
    """<div class="card"><slot name="body">Loading…</slot></div>""",
    onSlot: (slot, slotContainer) =>
    {
        if (slot.Name == "body")
        {
            slotContainer.Text("Real content, filled in from C#.");
        }
    });
```

`onSlot` fires once per `<slot>` element in the fragment (in document order, regardless of name), with a
`SlotContext` (its `Name` and `Attributes`) and an `IContainer` positioned to replace it - populate it the same
way any other container is populated. A slot the callback leaves untouched (including when `onSlot` is `null`
altogether) keeps its own fallback content - whatever markup it contained - exactly as authored.

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

`AlignLeft()`/`AlignCenter()`/`AlignRight()` position a container within its parent (with auto margins, so give it a `Width` narrower than the parent - or leave it auto-width, and it shrinks to its content). They work on both `Row` and `Column` items. Text inside a container is aligned separately, with `Alignment(...)` on the text (`t.Alignment(TextAlignment.Right)`).

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

To right-align a column of amounts, align the cells' text — `row.Cell().AlignRight().Text("3.20")` (or `Text(t => { t.Alignment(TextAlignment.Right); t.Span("3.20"); })`). Margins do not apply to a table cell, so on a cell `AlignLeft()`/`AlignCenter()`/`AlignRight()` align the cell's content instead of positioning it.

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

### Sectioned page numbers

A page count scoped to a named part of the document, rather than the whole thing - "Page 2 of 5" within a chapter, not the whole report:

```csharp
container.Column(column =>
{
    column.Item().Text("Cover / table of contents...");

    column.Item().BeginPageNumberOfSection("chapter1").Text("Chapter 1");
    column.Item().Text("Chapter 1 content, spanning several pages...");
    column.Item().EndPageNumberOfSection("chapter1").Text("End of Chapter 1.");

    column.Item().Text("Appendix...");
});

page.Footer(footer => footer.Text(t =>
{
    t.Span("Page ");
    t.PageNumberWithinSection("chapter1");
    t.Span(" of ");
    t.TotalPagesWithinSection("chapter1");
}));
```

`BeginPageNumberOfSection(id)`/`EndPageNumberOfSection(id)` are decorators, like `Bookmark` - they tag whatever container they're chained onto (rather than placing content of their own), so chain one before that container's own terminal content and it can go anywhere in a page's normal content flow (not just `Header`/`Footer`). `PageNumberWithinSection(id)`/`TotalPagesWithinSection(id)` then resolve against the physical pages that pair of tagged containers land on, the same way `CurrentPageNumber()`/`TotalPages()` do - only inside `Header`/`Footer` content, and continuously renumbered per document page. The current-page-within-section number is deliberately unclamped outside the section itself: it climbs by exactly one per document page throughout the whole document, so it reads non-positive before the section starts and past the total after it ends - a footer that only wants to show it inside the section's own pages should condition on that itself. An id with no matching `BeginPageNumberOfSection` call (a typo, or a forgotten call) resolves to `1` rather than throwing, the same fallback an unresolved `target-counter()` reference already uses.

## Known v1 limitations

- **No `Placeholder` prototyping element** — unlike every other terminal method here, a placeholder has no CSS mapping at all, so it needs genuinely new paint code rather than a wrapper over an existing property.
- **No per-image compression/DPI override, or `ShrinkToFit`/`ScaleToPageSize`** for a declarative document — each of these needs a re-run of the whole builder callback that a hand-built tree has no equivalent for; document-wide settings on `PdfGenerateConfig` (`DownscaleImages`, `PixelsPerInch`, ...) still apply.
- **A bare type or universal selector in a document-level stylesheet matches this API's own internal box structure** — every anonymous container defaults to a synthetic `"div"` tag (`img`/`a` for images/hyperlinks) unless renamed with `Tag(...)`, so a rule like `div { ... }` or `* { ... }` matches broadly; prefer class/id selectors, or `Tag(...)`, for predictable targeting.
- **No vertical writing modes for the declarative API** — a document-level stylesheet's `writing-mode` resolves onto individual boxes' computed style but the document root stays fixed at `horizontal-tb` regardless (a separate, much larger feature than this one, and orthogonal to it).
- **A `<link rel="stylesheet">` inside an `Html(...)` fragment is never loaded** — there is no document context yet at the point a fragment is spliced in to resolve a network/relative URL through; author a `<style>` tag, or pass CSS via `Html`'s own stylesheet parameter, instead.
- **A `float: footnote` element inside an `Html(...)` fragment renders as ordinary inline content** rather than being detached into a footnote area — footnote detachment needs a real document context the fragment doesn't have at splice time.
