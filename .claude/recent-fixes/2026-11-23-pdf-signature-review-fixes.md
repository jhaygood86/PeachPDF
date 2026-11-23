# Fixes from the review pass over the PDF signing work

A review of the five signing commits found one reproducible bug and several hardening points; each was checked against the code before
being changed. Builds on the [phase 4 entry](2026-11-22-pdf-reading-signatures-phase-4.md).

**Fixed:**
- **A hostile `/ByteRange` crashed `GetSignatures`.** `(long)doc.NumberOf(...)` saturates for a real such as `9.3e18`, so
  `range[2] + range[3]` wrapped negative and `data[range[2] - 1]` threw `IndexOutOfRangeException`. Entries are now required to be whole
  numbers inside the file before any arithmetic, and the sums are done as differences.
- **Quadratic cost.** Each signature is checked against its own copy of the covered bytes, so a file listing thousands of `/Sig` values
  cost time and memory quadratic in its size; at most 256 are read (`PdfSourceSignatures.MaxSignatures`).
- **BER.** The contents length was read as one DER length, so a BER indefinite-length structure (written by some producers and TSAs) was
  reported unreadable. `ElementEnd` now walks the structure (depth limited, lengths bounded). White space between hex digits is allowed.
- **`PdfFileSigner` on a file that needs a password** threw `PdfPasswordException`, not the documented `NotSupportedException`.
- **Object numbers near `int.MaxValue`** (hostile `/Size`) wrapped when new objects were numbered; now `InvalidDataException`.
- **The composer's signature field was always `Signature1`**, so a form field the document drew with that name collided; it takes the
  first free `SignatureN` as `PdfFileSigner` already did.

**Checked and fine:** `CmsSigner` does not add a signing time on its own (a PKCS#7 signature has exactly one, a CAdES one none: tested);
an object-copied page does not carry the source's signature value (the earlier warning tests assert `HasSignatures` is false); an explicit
`null` form entry cannot occur because the parser drops null values.

**Left, with reasons:** [the hole is not bound to the declared `/Contents`](../accepted-gaps/pdf-signature-reading-does-not-bind-the-hole-to-the-declared-contents.md),
and [signing is not silent](../accepted-gaps/pdf-signing-does-not-suppress-key-access-prompts.md).
