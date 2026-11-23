# `PdfPageSource.GetSignatures` cannot bind the signed hole to `/Contents` in an encrypted file

`PdfSourceSignatures` reads the CMS from the bytes between `ByteRange[1]` and `ByteRange[2]` of the file (it must: `/Contents` is never
decrypted, so the raw route is what makes encrypted files work) and checks it against the bytes outside that hole. For an unencrypted file
it also requires that hole to be exactly the value of the signature dictionary's own `/Contents`, so a reader that takes `/Contents` from the
dictionary checks what was verified here.

An encrypted file's strings are decrypted when the reader loads them, and a signature's `/Contents` is never encrypted, so the dictionary's
string is not the file's bytes there and the comparison cannot be made; the hole is still checked against the covered bytes. It is a
hardening point, not a forgery route found: the hole must hold a CMS whose digest matches every other byte. Closing it means recording the
raw token offsets of `/Contents` in the parse (`PdfLexer` tokens carry `Start`; `ParseItem` discards them for strings) and comparing those.
