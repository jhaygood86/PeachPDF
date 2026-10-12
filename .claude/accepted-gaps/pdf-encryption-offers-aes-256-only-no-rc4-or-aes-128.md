# PDF encryption offers AES-256 only: no RC4 and no AES-128

**Gap:** `PdfGenerateConfig.Encryption` writes the standard security handler V=5 / R=6 (AES-256) and nothing
else. A reader that can only open the older handlers (V1-V4: RC4 40/128-bit, AES-128) is not served.

**Why out of scope:** the project rule is no deprecated cryptography and no cryptography beyond .NET's own
`System.Security.Cryptography`, written by us or taken from a library. AES-128 (R4) cannot be built under that
rule: its `/O` and `/U` values and its file key are derived with **RC4 and MD5** (ISO 32000-2, Algorithms 2 to 5),
and .NET offers neither as a sanctioned primitive (MD5 is flagged weak, RC4 is not in the BCL at all, so it would
be our own implementation). R5 (the pre-release AES-256) is not offered either: its hash was replaced by R6's
iterated one for a known weakness, and PDF 2.0 deprecates it.

**What this costs:** a PDF 1.4 to 1.6 reader from before 2008 cannot open the file. PDF 1.7 readers from Acrobat X
(2010) onwards, and every current browser and library reader, can: the file announces `/Extensions` Adobe extension
level 8 when it is not PDF 2.0.

**Also not supported:** public-key security handlers (certificate-based encryption), and per-stream crypt filters
beyond the identity filter on a cleartext XMP stream.
