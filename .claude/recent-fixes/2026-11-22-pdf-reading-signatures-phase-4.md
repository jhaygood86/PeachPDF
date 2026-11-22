# Reading and checking a PDF's signatures (PdfPageSource.GetSignatures)

**What landed:** `PdfPageSource.GetSignatures()` returns `PdfSourceSignature` values: the field name, what the signature states, its
embedded certificate, whether it is intact, whether it covers the whole file, and an embedded timestamp's time, certificate and validity.
Document timestamps are listed too, with `IsDocumentTimestamp`. Builds on the earlier phases' [signing entry](2026-11-22-pdf-signatures-phase-1.md).

**Load-bearing ideas:** a signature is checked against the *file's own bytes* - the `/ByteRange` is read from the value dictionary but the
covered bytes and the hex in `/Contents` come straight from `PdfSourceDocument.Data` (the `/Contents` between `<` and `>` at the offsets the
range names), not through the object model. The reader decrypts strings it loads and a signature's `/Contents` must never be decrypted, so
the raw route is what makes encrypted files work. The DER length at the start of the zero-padded contents says where it ends; trimming
trailing zeros would eat real bytes. "Intact" and "trusted" are kept apart on purpose: no chain building, revocation or validity-period check
happens here, and the property names do not say "valid".

**Found by running it:** a damaged signature value does not just fail `CheckSignature`; reading `SignerInfo.Certificate` or the unsigned
attributes can throw `CryptographicException` ("Cannot find the requested object") first. Both are inside a try so a corrupt signature is a
finding in the result and never an exception from the call. The same hazard exists for a document timestamp's signer certificate.

**Evidence:** `PdfSignatureReadingTests` (23 tests): intact, tampered covered byte, damaged and unreadable `/Contents`, bytes appended, an
appended second signature, embedded and document timestamps (including damaged ones), an encrypted file, malformed `/ByteRange`s, PDF dates,
nested field names, shared values. A file signed by pyHanko is read correctly (field name, sub-filter, signer, intact).

**Not done:** trust (chains, revocation, signing time against validity), checking the timestamp authority's EKU, the legacy
`adbe.x509.rsa_sha1` sub-filters (reported not intact), classifying what changed after a signature (the DocMDP/modification-level analysis a
validator like pyHanko does), and long-term validation data.
