# The composer writes /Encrypt as an indirect object

An encrypted file the composer wrote had its encryption dictionary *direct* in the trailer (`/Encrypt << ... >>`). That is valid
(ISO 32000 allows either), and qpdf, MuPDF and PDFium opened it, but pyHanko refuses it ("Encryption settings must be an indirect
reference"), signed or not, so the files could not be validated there. They are now `/Encrypt N 0 R`, and pyHanko opens and validates them.
Found while checking [signing encrypted files](2026-11-23-pdf-signing-encrypted-files.md) against four readers.

**Load-bearing ideas:** `PdfDocument.DoSave` makes the handler's dictionary an indirect object (`Internals.AddObject`) before
`PrepareForSave` (so it is numbered and kept by the reachability pass: the trailer refers to it) and points the trailer at it. **It must be the one
object written with its strings in the clear**: its `/O` `/U` `/OE` `/UE` `/Perms` are the key material a reader derives the file key from, and
`PdfWriter` encrypts every string of an indirect object when the file is encrypted, which would have corrupted all of them, so
`PdfDictionary.WrittenUnencrypted` (set by `CreateEncryptDictionary`) makes `WriteBeginObject` leave the encrypting switch off for it. The flag is
on the object, not on the writer, because the linearizer serializes each object on its own through `PdfWriter.ToBytes`, a path that never sees
a writer-level setting. **A second save of the same composer** replaces the first save's dictionary instead of leaving it as a stray object in the
table (`EncryptionDictionary` remembers it and removes its reference first; each save draws a new key and new salts). **Linearized files** put it in
the open-document section with the catalog, so a viewer streaming the file has it before the first page: qpdf's linearization check passes.

**Evidence:** `PdfEncryptionDictionaryTests` (7) pin the reference form, the cleartext entry lengths (`/U` and `/O` 48 bytes, `/UE` `/OE` 32,
`/Perms` 16: encrypted they would be 16 + padding longer), the passwords, saving twice, and the linearized order; the only baselines that moved are
`encrypted-aes256` and `encrypted-linearized` (every unencrypted hash is unchanged), regenerated on purpose. pyHanko now opens plain,
linearized and signed variants (authenticating as the user, validating the signature), and qpdf, MuPDF and PDFium still do.

**Not done:** a file *written by another tool* with a direct `/Encrypt` is still read and signed as before (`PdfFileSigner` copies whichever form
the source's trailer has).
