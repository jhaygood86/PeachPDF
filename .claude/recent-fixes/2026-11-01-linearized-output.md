# Linearized output (`PdfComposerOptions.Linearize`, `PdfGenerateConfig.Linearize`, `--linearize`, `PdfOperation.Linearize()`)

`PdfLinearizer` (`PeachDrawing.PDF/Engine/IO`) writes the ISO 32000-1 Annex F layout from `PdfDocument.DoSave`. It renumbers the
irefs for the write (first-page section highest) and puts the numbers back in a `finally`, so a composer can be saved again.

Found by checking the output with qpdf (`pikepdf.open(f).check_linearization()` returns a bool, and the *reason* is only in
`pdf.get_warnings()` - print that, the call itself prints nothing), and by having qpdf linearize the same file and decoding both
hint streams side by side:

- **Hint offsets past the hint stream are given as if it were not there** (qpdf: `offset - hint length`). The lin dictionary's `/H`,
  `/E`, `/T` and the xref entries are real offsets.
- **qpdf lists every object of the first-page section as a shared-object entry** (first-page entries, then the part-8 ones); the first
  page names no shared objects of its own, and other pages name entries by index. A page's content is described as the whole page
  (offset 0, length = page length).
- **A page's shared ids are in the order of the referenced objects' numbers**, not of entry index - the checker walks the page's
  objects by number. Symptom of getting it wrong: `page 1: shared object N: in hint table but not computed list`.
- **`/PageMode /UseOutlines` pulls the outline hierarchy into the first-page section**, contiguous at its end, with a third (outline)
  hint table whose offset is `/O` in the hint stream dictionary. Symptom without it: `incorrect object count in outline hint table`.
- **The interactive form, viewer preferences, threads and open action (and everything they reach) go in part 4, after the catalog,
  even when a page uses them too** - so the fonts a form's appearance shares with the pages end up there, and the pages do not
  list them. Symptom without it: `object count mismatch for page 0` and a pile of `shared object` complaints, only with
  `EnableInteractivePdfForms`.
- The linearization dictionary and trailer `/Prev` are written with fixed-width (10 digit, zero padded) numbers so the layout is
  settled in one pass; the hint stream's size depends only on lengths, never offsets. The last `startxref` is the first-page table's
  offset, and the first-page trailer ends with `startxref 0`.
- Encryption: strings are encrypted per object number, so objects are serialized after renumbering; the hint stream is encrypted
  like any stream.

Evidence: `PdfLinearizeTests` (offsets promised by the dictionary, every xref entry points at its object, round trips, encrypted,
saved twice), `LinearizeIntegrationTests` (HTML with tagging/forms, declarative), and qpdf's check over one page, merged,
object-copied, encrypted and HTML-rendered (plain, tagged, tagged+forms, encrypted) outputs - all `True` - with PDFium pixels
equal to qpdf's own non-linearized rewrite.
