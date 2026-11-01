# `PdfOperation.Encrypt` writes AES-256 only; AES-128 and 40-bit RC4 output is not offered

QuestPDF's document operations offer 40, 128 and 256 bit encryption. `PeachDrawing.PDF` writes AES-256
(revision 6) only, and reads AES-128 and AES-256 (see `PdfDecryptor`); RC4 is refused both ways.

**Why AES-128 output is not there either.** An AES-128 file is version 4, revision 4, and that revision
computes its `/O` and `/U` password-check entries (ISO 32000-1 algorithms 3 and 5) with RC4, whatever cipher
encrypts the content. Writing a file a reader will accept a password for therefore needs RC4, which .NET does
not provide. The repository rule is to use .NET-native cryptography only and never hand-roll RC4, so the option
is left out rather than writing a file whose passwords no reader can verify. Reading is the other side of the same
fact: an AES-128 file's user password is used to derive the key without being checked against `/U`, so a wrong
password is detected by the content decrypting to invalid padding rather than by a failed check, and an AES-128
file's **owner** password cannot be turned into the user password (algorithm 7 is RC4), so only the user password
opens it. AES-256 revisions 5 and 6 check both passwords with SHA-2, so both open the file.

**What would close it.** Allowing a minimal, reviewed RC4 used only for `/O` and `/U` of revision 4, which is a
decision about shipping cryptography this repository has so far declined; ask before adding any.
