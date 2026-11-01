# PDF merge and document operations (`PdfOperation`, `DocumentMerge`)

What landed: `PeachDrawing.PDF.PdfOperation` (Load/TakePages/Merge/Overlay/Underlay/AddAttachment/ExtendMetadata/Encrypt/
Decrypt, `PdfPageSelector`, `PdfCopyMode`), password-aware opening (`PdfPageSource.Open(bytes, password)`,
`PdfPasswordException`), and `PeachPDF.Layout.DocumentMerge` with `PdfGenerator.CreateMergedDocument/AddMergedPages`.

Load-bearing ideas and traps (all found by running, not reading):

- **Redraw is the default because it reuses the layer pipeline.** `PdfOperationWriter` is `FileLayerPlan` generalized:
  stamps keyed by output-page *identity* (`OutputPage`), not index, so a layer follows its page through a later
  `TakePages`, and `TakePages("1,1")` makes two independent copies (copy-on-take, `OutputPage.Copy`).
- **The engine allows one `PdfGraphics` per page at a time.** Overlays on an object-copied page therefore cannot use a
  second canvas next to the page's own: the page canvas draws underlays, is `Finish()`ed, the copied streams are
  inserted after content stream 0, and `PdfComposerPage.DrawAppended` opens a fresh graphics for the overlays.
- **Resource-name collision (symptom: `Alpha` extracted as `$lSKa`).** A copied page's `/Resources` names its fonts
  `/F0`, `/F1`; `PdfResources` handed out `/F0` again to the layer's font, silently replacing the page's. Fixed in
  `PdfResources.Unused` (names skip what the map already holds) and by giving every copied page a private resources
  dictionary (shared objects stay shared; the dictionary of categories does not), so a layer never grows a dictionary
  other pages share. Seen in MuPDF text extraction; both renderers draw the glyphs wrongly, only MuPDF got the text wrong.
- **Content must go in before layers draw**, so the layer's resource names are chosen after the page's exist.
- **A widget must be the same object in the page's `/Annots` and its field's `/Kids`.** `PdfObjectCopier` registers the
  widget's destination object before cloning its `/Parent`, otherwise the parent clones a second copy of the widget.
- **Annotations are cloned per page copy, never memoized** (a page taken twice has two sets of links), except widgets.
- **`/Length` of a copied stream is rewritten from the bytes** (`CreateStream`), never copied: it may be an indirect object.
- **Page numbers**: `HtmlDocumentOptions.PageNumberOffset/TotalPagesOverride` are display-only; see the accepted gap
  [merged-documents-number-pages-for-display-only](../accepted-gaps/merged-documents-number-pages-for-display-only.md).
- **AES-128 output is impossible without RC4** (revision 4's `/O`/`/U`): see
  [pdf-operations-cannot-write-rc4-or-aes-128-encryption](../accepted-gaps/pdf-operations-cannot-write-rc4-or-aes-128-encryption.md).
  `PdfDecryptor` accepts a password for R4 without checking it against `/U` (that needs RC4); a wrong password is caught
  by invalid AES padding on the first page's content, so `PdfPasswordException` may be raised late for R4 files.

Evidence: `PeachDrawing.PDF.Tests` (selector grammar table, operation chain, object copy: byte-for-byte content, shared
resource copied once, links/outline/forms/overlays, encryption round trip), `PeachPDF.Tests` `DocumentMergeIntegrationTests`,
an engine test of the display offset, and the merged/layered outputs rendered with both MuPDF and PDFium.
