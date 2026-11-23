# `PdfPageSource.GetSignatures` does not check that the signed hole is the dictionary's own `/Contents`

`PdfSourceSignatures` reads the CMS from the bytes between `ByteRange[1]` and `ByteRange[2]` of the file (it must: `/Contents` is never
decrypted, so the raw route is what makes encrypted files work) and checks it against the bytes outside that hole. It does **not** compare
the hole with where the signature dictionary's own `/Contents` string sits in the file. A file could therefore name a hole that holds a valid
CMS over the remaining bytes while the dictionary's `/Contents` points elsewhere.

This is a hardening point, not a forgery route found: the hole must still hold a CMS whose digest matches every other byte, and
`IsIntact` is documented as "matches the bytes its byte range names", which is exactly what is checked. Closing it means recording the
token offsets of `/Contents` in the reader's parse (`PdfLexer` tokens carry `Start`, `ParseItem` discards them for strings) and comparing.
Not done because no reader that reads `/ByteRange` first (the ISO 32000 procedure) is affected, and the cost is a parser change.
