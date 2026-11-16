# A document with no DOCTYPE is laid out in quirks mode

**Before:** the HTML parser told quirks mode from the DOCTYPE and used it for one rule (a `<table>` start tag closing an open `<p>`); the style sheet and layout were the standards' for every document, DOCTYPE or not.

**Now:** a document with no DOCTYPE, or a legacy one, is in quirks mode, and one with a transitional XHTML 1.0 or HTML 4.01 DOCTYPE with a system identifier is in limited-quirks mode, as in a browser. HTML handed to `PdfGenerator` without a DOCTYPE (a fragment, most often) is laid out differently from before:

- the first paragraph, heading or list of a `body` or table cell has no top margin, and the last one of a cell has no bottom margin;
- a `form` has a bottom margin of `1em`;
- a table does not inherit the font, `line-height`, `white-space`, `text-align` or color of what it sits in (it takes the initial values, and the body's color);
- a line holding only an image is the image's height, and an empty inline element makes no line taller (limited-quirks mode too);
- `html` and `body` fill the first page, and a percentage `height` is resolved against the page when the ancestors have no height;
- class and ID selectors ignore case, and `color: ff0000` and `width: 100` are a color and a length.

Outside quirks mode class and ID selectors are now **case-sensitive** (`.Card` no longer matches `class="card"` in a document with a DOCTYPE), as the standards say. That includes HTML fragments given to the declarative builder, which are never in quirks mode.

**To keep the old output:** add `<!DOCTYPE html>` to the HTML, or set `PdfGenerateConfig.DocumentMode = PdfDocumentMode.NoQuirks` (`--document-mode=no-quirks` for the command-line tool; `HtmlDocumentOptions.DocumentMode` for `PeachBrackets`). `HtmlDocument.DocumentMode` reports the mode a document is in.

See [Document modes](../../docs/html-css-support.md#document-modes-quirks-mode).
