# A signed PDF's /ByteRange and /Contents placeholders must stay fixed width and unencrypted

`PdfDocumentSigner` reserves `/ByteRange [0 0000000000 0000000000 0000000000]` and `/Contents <00...00>` as `PdfLiteral`s, saves, then patches
both in place in the byte array. This only works because the patch changes **no length**: every xref offset, the linearizer's hint tables and
`/L` were computed with the placeholders in place. A `/ByteRange` written with unpadded numbers, a `/Contents` that grows, or a signature
built as a `PdfString` (the writer encrypts strings in an encrypted file, which corrupts the signature) breaks the file or the signature.

Also: the placeholders are found by searching the written bytes, so they must occur exactly once (`Seal` throws otherwise); do not put the zero
run anywhere else. Reading a signature back, never trim trailing `0` hex digits - take the DER length.
