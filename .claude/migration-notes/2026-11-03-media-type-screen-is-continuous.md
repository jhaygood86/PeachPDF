# `Media` became `MediaType`; screen media has no pages; PDF is always print

**`HtmlDocumentOptions.Media`** (a string, `"print"` by default) is replaced by **`MediaType`** (`Print`, the default, or `Screen`). `Screen` now means a continuous surface: one `DocumentPage` as wide as the page width and as tall as the content, no `@page` rules or margin boxes, no page breaks (column breaks still work), and `float: footnote|top|bottom|...` and `position: running()` ignored. A caller that set `Media = "screen"` and expected the document to still be paged must use `Print`. New: `ViewportHeightPoints`, `HtmlDocument.Viewport`/`SetViewport` (fixed, sticky and `background-attachment: fixed` follow it).

**PeachPDF and the CLI.** `PdfGenerateConfig.Media` and `peachpdf --media` are removed: a PDF is paged, so it is always laid out as print media. Documents rendered to PDF with `screen` media styles now get `print` styles.

**`HtmlView`** (WinUI and Android): a document taller than 14000pt used to be cut into stacked pages with a gap; it is now one surface. The 8 DIP gap and the 4 DIP inset above the first page are gone. The viewport height (`vh`) is the view's height.

**Fixed boxes** with `right`/`bottom` (and no `left`/`top`) used to sit in the top-left corner of the page area; they now sit against the far edges, in paged media too.
