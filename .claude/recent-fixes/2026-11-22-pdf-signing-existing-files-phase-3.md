# Signing and timestamping an existing PDF (PdfFileSigner)

**What landed:** `PdfFileSigner.Sign`/`SignAsync`/`AddDocumentTimestampAsync` append a signature or a document timestamp to a PDF as an
incremental update. Builds on [phase 1](2026-11-22-pdf-signatures-phase-1.md) and [phase 2](2026-11-22-pdf-signature-timestamps-phase-2.md).

**Load-bearing ideas:** `PdfIncrementalUpdate` copies the original bytes verbatim and appends only the objects that change, written back out
as text from the reader's object model (catalog, form, first page, the arrays that list fields and annotations, each as a replacement
object with the same number and generation), plus the new value and widget objects. Whichever of those the file keeps in its own object
(an indirect `/AcroForm`, `/Fields` or `/Annots`) is rewritten there; whichever is direct is rewritten inside its parent. The new
cross-reference section is a classic table, or a cross-reference stream when the file's newest section is one (checked by peeking at the
bytes `startxref` names): a classic table pointing `/Prev` at a stream is the case readers handle least reliably. The value dictionary holds
the same fixed-width `/ByteRange` and `/Contents` placeholders as [phase 1](2026-11-22-pdf-signatures-phase-1.md), so `PdfDocumentSigner`'s
seal fills them unchanged (see the invariant on fixed-width placeholders). A document timestamp reuses the seal with the token itself as
`/Contents` (RFC 3161 over the digest of the covered bytes, `ETSI.RFC3161`).

**Found by running it:** the first draft named the class `PdfSigner`, which is iText's; the naming register check renamed it. The field
name must be free in the form (`FreeName`), so a file signed by the composer (`Signature1`) gets `Signature2` from the second signer. The
reader reconstructs a damaged file lazily (a later object load can set `WasRebuilt`), so the rebuilt check runs after the objects were
read, not before.

**Evidence:** `PdfFileSignerTests` (original bytes preserved, earlier signature valid, three signatures numbered apart, linearized source,
five form/annotation shapes, a cross-reference-stream source signed twice, encrypted and garbage refused, timestamped signature, document
timestamp). pyHanko, on a composer-signed + appended + document-timestamped file: 3 revisions, `Signature1` still intact/valid/trusted
(`ENTIRE_REVISION`, form-filling level change only), `Signature2` valid, `Timestamp1` valid with `ENTIRE_FILE` coverage; the xref-stream
source validates too.

**Not done:** encrypted sources (the objects would need encrypting), a visible appearance or a chosen page/position, a callback signer,
`/DSS` and the B-LT/B-LTA levels, a stream-based object in the rewritten set (a catalog or page that is a stream is refused), repairing a
damaged file first.
