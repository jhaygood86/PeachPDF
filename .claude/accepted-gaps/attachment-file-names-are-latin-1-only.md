# Attachment file names are limited to Latin-1

`PdfAttachment.FileName` may contain only characters up to U+00FF (and no path separators or control characters).
A name outside Latin-1 - CJK, Cyrillic, Greek, an emoji - is rejected with an `InvalidOperationException`. A
Latin-1 name that is not plain ASCII (`café.csv`) works: `/F` carries an ASCII fallback (`caf_.csv`) and `/UF` the
real name as UTF-16BE.

## Why

The name is also the key of the `/Names /EmbeddedFiles` name tree, and `PdfNameTreeNode` stores keys as raw 8-bit
`PdfString`s (`PdfString(string)` throws for a character above U+00FF). The PDF specification lets name-tree keys
be text strings, so UTF-16BE keys are possible - but they have to be sorted by their *encoded bytes* (ISO 32000-1
§7.9.6), which the tree's ordinal insertion does not do, and the tree never writes `/Limits`. Getting that wrong
yields a tree some readers cannot binary-search, so the attachment silently vanishes from their panel - a worse
outcome than an up-front error. Every real use so far (invoice XML, CSV/XLSX/PDF supporting files) has an ASCII
name; Factur-X mandates `factur-x.xml`/`xrechnung.xml`.

Closing it means teaching `PdfNameTreeNode` to key on text strings (byte-order sorting, both encodings) and
adding the tests that prove a strict reader still finds the file; then delete this file and the "Limited to
Latin-1" wording in `docs/usage-examples.md`'s attachment table.
