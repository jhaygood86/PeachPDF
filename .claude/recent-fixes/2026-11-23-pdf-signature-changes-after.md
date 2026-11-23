# What changed after a signature (PdfSourceSignature.ChangesAfter)

`PdfSourceSignature.ChangesAfter` (`PdfSignatureChanges`: `None`, `SignaturesAndTimestamps`, `FormFilling`, `Annotations`, `Other`,
`Unknown`) says what happened to the document after each signature, so a later signature or timestamp is no longer indistinguishable from an
altered page. Closes the "classifying what follows a signature" gap listed after the
[encrypted-file signing](2026-11-23-pdf-signing-encrypted-files.md).

**Load-bearing ideas:** `PdfRevisionComparer` reads the file twice: the bytes up to the end of the signed range (a whole file ending at its own
`%%EOF`, which `PdfSourceDocument.Open` can read on its own) and the file as it is, and compares objects. The comparison is by *canonical
fingerprint* (dictionary keys sorted, strings as hex, a stream by its SHA-256), so equal objects compare equal whatever order or syntax they
were written in. The accounting is conservative and inverted: it does not try to recognize every harmless change, it recognizes the *few*
that are (the catalog's `/AcroForm` `/DSS` `/Extensions`, the form's `/Fields` growing at the end and `/SigFlags`, a field's `/V` `/AP` `/AS`,
a page's `/Annots` growing at the end, new annotation dictionaries, the objects the security store reaches) and puts every one of those
objects in a "permitted" set; any other object that existed before and now differs makes the answer `Other` (the catch-all loop at the end).
New objects need no accounting: they only matter through an existing object that now refers to them, and existing objects are all checked.
A signature that is not intact, or whose prefix cannot be re-read (or makes the reader rebuild a damaged file), is `Unknown`.

**Found by running it:** the first unit tests all started from a hand-built file with no form, where adding the first signature field changes
the catalog and so raised the level by accident; a composer-signed file already has a form with an indirect `/Fields` array, so appending a
signature only grew that list and raised nothing, reporting `None` for a signature with two later ones. Caught by running the classifier over
the earlier interop files (`inc.pdf`) and fixed by raising `SignaturesAndTimestamps` for a new signature field; a test now starts from a
composer-signed file. pyHanko calls the same states `FORM_FILLING` (a first signature followed by a new signature field) and `LTA_UPDATES`
(a timestamp): its levels are broader, and ours deliberately narrower.

**Evidence:** `PdfSignatureChangesTests` (15) cover every outcome: nothing after, a later signature, a document timestamp, a form that
already exists, trailing bytes, an unreferenced new object, replaced page content, a changed page, filled and renamed/moved fields, an added
and a removed annotation, a tampered signature and an encrypted file.

**Not done:** a per-field distinction inside form filling (a changed `/Ff` read-only flag reads as `Other`), the signer's own permissions
(`/Reference` DocMDP transform parameters and a certification signature's allowed level are not read, so `Other` does not mean "disallowed"),
rewritten object streams and cross-reference streams as such (compared as the objects they hold), and a comparison cost bounded for hostile
files beyond the 256-signature cap (each signature re-opens its own prefix).
