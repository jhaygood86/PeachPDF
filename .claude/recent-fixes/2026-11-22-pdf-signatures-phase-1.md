# Signing a generated PDF (PdfComposerOptions.Signing)

**What landed:** `PdfComposerOptions.Signing` (`PdfSigningOptions`, `PdfSignatureFormat`) signs the file `PdfComposer.Save` writes with a
CMS detached signature, `adbe.pkcs7.detached` or `ETSI.CAdES.detached`. Phase 1 of a plan whose later phases (RFC 3161 timestamps,
signing an existing file by incremental update, verification, LTV) are not built. Phase 0 (`PdfPageSource.HasSignatures`, the
`PdfOperation.Warnings` entry for a signed source) landed first.

**Load-bearing idea:** the document gets an invisible `/Sig` widget on page 1 whose `/V` dictionary holds two *fixed-width* placeholders,
`/ByteRange [0 0000000000 0000000000 0000000000]` and `/Contents <000...>` (2 x `ReservedSignatureBytes` hex zeros), written through
`PdfLiteral`. `PdfComposer.Save` writes to a `MemoryStream`, `PdfDocumentSigner.Seal` finds each placeholder (exactly once, or it throws),
patches `/ByteRange`, hashes the bytes outside `/Contents`, and hex-writes the CMS over the zeros. Nothing moves, so the xref offsets and the
linearized hint tables stay valid, which is how it works with `Linearize`. Encryption works because `PdfWriter.Write(PdfLiteral)` is raw: a
`PdfString` here would have been encrypted, and a signature's `/Contents` must not be.

**Found by running it:** `System.Security.Cryptography.Pkcs` is not in the shared framework. A throwaway NativeAOT app that signed, verified and built an RFC 3161 request published with no warnings and ran, so the package was added (central version per TFM: 10.0.14 / 11.0.0). Two traps in the first test draft: trimming trailing `0` hex digits off the placeholder eats real signature bytes (read the DER length instead), and the same certificate added twice is embedded once (use distinct ones to test an overflow).

**Evidence:** `PdfSigningTests` verify each signature with `SignedCms.CheckSignature` over the bytes the file's own `/ByteRange` names (RSA,
ECDSA, SHA-256/384/512, both formats, encrypted, linearized, saved twice). pyHanko (an independent implementation) reports the PKCS#7, CAdES
and linearized CAdES samples intact, valid, trusted, `ENTIRE_FILE` coverage.

**Not done:** PDF/A interplay is untested (PDF/A-1 limits signatures); a visible appearance; a callback signer for keys held in an HSM; the
`/Contents` placeholder is not excluded from `PdfOutputBaselineTests` because unsigned output is unchanged.
