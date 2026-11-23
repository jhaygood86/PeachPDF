# Visible signatures (PdfSigningOptions.Box)

`PdfSigningOptions.Box` (`PdfSignatureBox`: `PageNumber`, `Bounds`, `Text`, `FontSize`) draws the signature as a box on a
page, on both paths: a document the composer signs and a file `PdfFileSigner` signs in place. Closes the "no visible signature, always
page 1" gap left by the [signing key work](2026-11-23-pdf-signing-key-access.md).

**Load-bearing ideas:** the appearance is *plain PDF content* - a border and `BT /Helv ... Tj ET` lines, clipped to the box - written as
ASCII (`PdfSignatureBoxContent`), so the composer (engine objects: a stream-bearing `PdfDictionary` for the form XObject, a font
dictionary) and `PdfIncrementalUpdate` (the same content as appended object text) share one implementation and the same escaping (Latin-1, `(`
`)` `\` escaped, anything outside printable ASCII as an octal escape, a character beyond Latin-1 as `?`). The font is the standard
Helvetica, never embedded, so **a PDF/A or PDF/X file cannot have a visible signature** (rejected when the options are applied and by
`PdfFileSigner` on such a source); an invisible one is still allowed. A rotated page is refused on the incremental path (the box would need a
matrix and rotated text); a composer page is never rotated. The incremental path turns the box (page-as-shown points) into the widget's
rectangle in the page's own space with the inverse of `PdfPagePainter.PageToCanvas`, so a MediaBox that does not start at the origin works
(a test pins `[5 5 95 95]`).

**Naming:** the first draft called the type `PdfSignatureAppearance` - iText's class for the same role - which the public-API invariant forbids; it became `PdfSignatureBox` before the first commit.

**Found by running it:** `PdfPageSource.Paint(page, canvas)` draws page content only; annotations need `PdfAnnotationPaint.All`, so the
first pixel tests saw nothing. PDFium shows a signature widget only after `init_forms()` (the form environment draws widgets, the plain
render does not): a blank PDFium render without it is not a bug in the file. A test that rewrites bytes of a file must keep its length or
the cross-reference offsets break and the signer refuses the file as damaged.

**Evidence:** `PdfVisibleSignatureTests` (16), including pixel checks that the border lands on the chosen page and only there. Two
renderers (MuPDF, and PDFium with its form environment) draw the same border and four lines for a composer-signed and an incrementally
signed page; pyHanko reports both intact and valid.

**Not done:** an embedded font (so PDF/A), a rotated page, an image, a background color or line styles, text wrapping (long lines are clipped
to the box), a per-box `Text` alignment, and a visible document timestamp.
