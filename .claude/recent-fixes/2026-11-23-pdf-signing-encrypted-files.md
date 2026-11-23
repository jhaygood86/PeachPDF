# Signing an encrypted file in place (PdfFileSigner password)

`PdfFileSigner.Sign`, `SignAsync` and `AddDocumentTimestampAsync` take a `password` and sign an AES-128 or AES-256 file by writing the new
objects encrypted with the file's own key. Closes the "encrypted sources" gap listed after the
[visible signatures](2026-11-23-pdf-visible-signatures.md) work; the old "encrypted files are refused" test and message are gone.

**Load-bearing ideas:** in an encrypted PDF every string and stream of an indirect object is stored under *that object's* key
(`ObjectKey`: the file key itself for AES-256, `MD5(key + object + generation + "sAlT")` for AES-128), so each object the update writes is
encrypted by its own number and generation. A dictionary or array that is *direct* belongs to the indirect object that holds it: a form
dictionary inside the catalog is encrypted with the catalog's key, and `Serialize` takes an `encrypt` closure chosen by the container
(`formOwner`, the page for a direct `/Annots`). The reader hands over decrypted strings, so a rewritten object is serialized from the
decrypted model and re-encrypted; an object that came out of an object stream (whose own strings were never encrypted) is written back as
an ordinary encrypted indirect object. `PdfDecryptor` gained `EncryptString`/`EncryptStream` (AES-CBC, a fresh `PdfEntropy` IV in front,
PKCS#7) over the same key derivation it decrypts with, and `PdfSourceDocument.Decryptor`. Never encrypted: `/Contents` and `/ByteRange` of the
signature (they are a literal placeholder), the trailer's `/ID`, and the cross-reference stream. **Every trailer of an encrypted file
must carry `/Encrypt`**, so the update's trailer (or xref stream dictionary) repeats it. The appearance stream of a visible signature is
encrypted (its `/Length` is the ciphertext length), which is binary, so `PdfIncrementalUpdate` now writes Latin-1, not ASCII.

**Found by running it:** the unit tests write and read with the same key code, so they cannot show a symmetric mistake; the independent proof
is qpdf (through pikepdf), MuPDF, PDFium and pyHanko opening an AES-128 (R4) and an AES-256 (R6) file qpdf encrypted and PeachPDF then
signed: qpdf finds the field name and `/Reason` decrypted and `check()` reports nothing, MuPDF authenticates and renders the box text, PDFium
renders it, and pyHanko decrypts and reports the signature intact, valid and covering the entire file. **Separate, pre-existing:** pyHanko
refuses a file *the composer* encrypted ("Encryption settings must be an indirect reference"), signed or not, because the composer writes
`/Encrypt` as a direct dictionary in the trailer; qpdf, MuPDF and PDFium open it. Valid PDF, a stricter reader; not changed here.

**Not done:** RC4 files (no .NET RC4), AES-128 *composition* (the composer only writes AES-256, so the R4 path is covered by the
qpdf-made file in the interop check and not by a unit test), and a per-call choice of which password a file needs beyond the one given.
