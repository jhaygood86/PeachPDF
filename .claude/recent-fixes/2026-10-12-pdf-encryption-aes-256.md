# PDF encryption: AES-256 (V5/R6) with owner and user passwords and permissions

`PdfGenerateConfig.Encryption` (`PdfEncryptionOptions`, `PdfPermissions`) encrypts the output. Why AES-256 only is in
[the accepted gap](../accepted-gaps/pdf-encryption-offers-aes-256-only-no-rc4-or-aes-128.md).

**Shape.** `PDF/Security/PdfAes256SecurityHandler.cs` derives `/U /UE /O /OE /Perms` (Algorithm 2.B hash) from BCL
`Aes`/`SHA256/384/512`/`RandomNumberGenerator`, and encrypts strings and streams (random IV prepended, AES-256-CBC,
PKCS#7, the file key used directly: AESV3 has no per-object key). `DoSave` builds a **new handler per save**, so two
saves never share a key.

**Traps found by running it, not by reading:**

- `/Length` is written *before* the stream bytes and AES changes the count, so `PdfDictionary.WriteObject` encrypts
  first, rewrites `/Length` for the write, and **puts the plaintext `/Length` back in a `finally`**. Mutating
  `Stream.Value` instead would encrypt twice on the second `Save` (a test saves twice).
- Only strings **inside an indirect object** are encrypted (`PdfWriter._inIndirectObject`): the trailer, `/ID` and the
  direct `/Encrypt` dictionary must stay in the clear or no reader can open the file.
- `PdfDate` writes through `WriteDocString`, which bypasses `Write(PdfString)`; both go through the encryptor.
- A Unicode string's `FEFF` byte-order mark is part of the plaintext, so it is added before encryption.
- Algorithm 2.B's loop test uses the number of rounds *executed* (`i < 64 || i < E[last] + 32`, as pdfium has it);
  testing against the zero-based index of the last round is off by one and fails only for some passwords.
- An explicit `PdfVersion.Pdf17` writes a **1.4 header** unless something raises it, so encryption raises the version
  to 1.7 in `EstablishDocumentOptions` and `DoSave` adds the `/Extensions /ADBE` level-8 entry below 2.0.
- `EncryptMetadata = false` needs the Identity `/Crypt` filter on the XMP stream (set and removed around the write).
  pypdf cannot decode a `/Crypt` filter with a `/Name` (its limitation, not ours): MuPDF and pdfium read it.

**Evidence.** `PdfEncryptionTests` carries a spec-based reader (recovers the file key from `/UE` and `/OE`, decrypts
and inflates the content) so a file only the writer could open does not pass. Independently, pypdf, PyMuPDF and
pdfium opened the output with the user password, the owner password (MuPDF reports owner authentication), the empty
password, a non-ASCII password, a PDF 1.7 file and plain/encrypted XMP, and rejected a wrong password; the Native AOT
`peachpdf` binary produced a file they all open. Both renderers draw the encrypted showcase page identically.

**Not done.** No CI job runs the external-reader interop above (it was run by hand). Browser WebAssembly was not
tried: encryption needs the platform's AES and SHA-2, which that host may not have, and the rule against our own
cryptography rules out a managed fallback.
