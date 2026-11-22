# RFC 3161 timestamps on a signed PDF (PdfSigningOptions.Timestamp)

**What landed:** `PdfSigningOptions.Timestamp` (`PdfTimestampOptions`: an `Exchange` delegate from request bytes to response bytes, an HTTP
convenience `FromHttpAuthority`, a digest) and `PdfComposer.SaveAsync`. The signature value is timestamped and the token is added as the
`signature-time-stamp-token` unsigned attribute (1.2.840.113549.1.9.16.2.14), which makes a PAdES B-T signature. Builds on
[the phase 1 entry](2026-11-22-pdf-signatures-phase-1.md).

**Load-bearing ideas:** the token covers the CMS *signature value*, so it is requested after the signature is computed and added to the same
`SignedCms` before it is encoded; the placeholder size is unchanged, so a token that pushes the signature past `ReservedSignatureBytes` fails
the save with a message naming that option instead of corrupting the file. `Rfc3161TimestampRequest.ProcessResponse` does the imprint and nonce
check; its `CryptographicException` is rewrapped as an `InvalidOperationException`. The timestamp's authority is asked asynchronously, so sync
`Save` throws for a timestamped document rather than blocking on a task.

**Found by running it:** the BCL reads RFC 3161 tokens but cannot make them, so the tests build a TSA by hand (TSTInfo with the request's
nonce, a SignedCms with the TSTInfo content type, the ESS signing-certificate-v2 attribute the decoder insists on, and a critical
`id-kp-timeStamping` EKU on the TSA certificate). `Rfc3161TimestampRequest` has no `Decode`, so the test TSA parses the request itself.

**Evidence:** `PdfTimestampTests` (token present, verifies against the signer info, SHA-384/512, both formats, refused garbage, a response to
another request, overflow, cancellation, HTTP post and failure). pyHanko reports the PKCS#7 and CAdES samples' timestamps valid and intact.

**Not done:** B-LT/B-LTA (`/DSS`, a document timestamp), checking the authority's own certificate against a trust list (the caller's job),
retries or authority fallbacks.
