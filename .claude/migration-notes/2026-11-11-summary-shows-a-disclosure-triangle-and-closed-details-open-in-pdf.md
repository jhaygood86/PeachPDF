# `<summary>` shows a disclosure triangle, and a closed `<details>` can be opened in a PDF reader

Before (checked against the last tag's `docs/html-css-support.md`): the first `<summary>` of a `<details>` was plain block text with no marker, and a closed `<details>` in a PDF was only its summary.

Now:

- The first `<summary>` is a `display: list-item` with a triangle (right when closed, down when open). A stylesheet that wants the old look adds `summary { list-style: none }` (or `display: block`). `list-style-type: disclosure-open|closed` on any list item is now a vector triangle rather than the glyph, so it no longer needs a font that has it.
- `PdfGenerateConfig.EnableInteractivePdfDetails` is on by default: the PDF of a document with a `<details>` gains hidden form widgets, a transparent button over the summary and an AcroForm (an open `details` collapses, a closed one opens). Set it to `false` to keep the old output. PDF/A, PDF/X and PDF/UA documents never get them.
