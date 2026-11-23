# Long-term validation: the document security store (PdfValidationData)

`PdfValidationData` (certificates, OCSP responses, CRLs), `PdfValidationData.CollectAsync`, `PdfFileSigner.AddValidationData` and
`PdfPageSource.GetValidationData` put validation data in a file's `/DSS`; with a document timestamp after it that is PAdES B-LT and B-LTA.
The last of the gaps listed after the [encrypted-file work](2026-11-23-pdf-signing-encrypted-files.md) and the
[change classification](2026-11-23-pdf-signature-changes-after.md).

**Load-bearing ideas:** the data is *supplied or collected, never fetched implicitly*: `AddValidationData` does no I/O (a sync method), and
`CollectAsync` takes the caller's `HttpClient` the way `PdfTimestampOptions.FromHttpAuthority` does. The store is written as an incremental
update by `PdfIncrementalUpdate.AppendValidationData`: each blob is a stream object (`<< /Length n >>`), the `/DSS` dictionary lists the
existing entries plus the new ones (so two calls accumulate; a blob whose SHA-256 is already in the store is skipped, and a call with nothing
new returns the file unchanged), and the catalog gets `/DSS` and, for a file older than PDF 2.0, `/Extensions << /ESIC << /BaseVersion /1.7
/ExtensionLevel 1 >> >>` merged into any `/Extensions` it already has. In an encrypted file each blob is encrypted under its own object's key,
as an appearance stream is. The `ChangesAfter` comparer already treats the objects the `/DSS` reaches as signature-level changes, so a file
with a store reads `SignaturesAndTimestamps` and the earlier signature stays intact (a test pins it). The OCSP request is built by hand (the
BCL has none): `CertID` names the issuer by SHA-1 of its name and of its key bytes (RFC 6960's default), the certificate by serial number.

**Found by running it:** a chain built with nothing trusted finds only what it is given, so a private CA's certificate must be passed as an
extra or the chain stops at the leaf and *no revocation data can be collected for it* (there is no issuer to ask about it): the first draft
skipped that silently; it now reports an incomplete chain in `Problems`. A handler that never looks at the cancellation token let the whole
collection run after a cancel, so each fetch checks the token itself; and a cancelled call must throw, not turn into a "problem" (the
`catch` filters on `!cancellationToken.IsCancellationRequested`). `CertificateRevocationListBuilder.Build` and `CertificateRequest.Create`
both need an explicit `RSASignaturePadding` for an RSA issuer (`X509SignatureGenerator.CreateForRSA` for the latter).

**Evidence:** `PdfValidationDataTests` (15): collection over a fake HTTP responder from a real test CA (chain, CRL, OCSP request shape,
refusals, non-web addresses, network failure, cancellation), and storing (round trip, the earlier signature intact, the version rule,
existing `/Extensions`, de-duplication and accumulation, an encrypted file, B-LTA end to end). qpdf and pyHanko read the store back (2
certificates, 1 CRL) for a signed file with a store and a document timestamp; pyHanko validates the signature and the timestamp (the signer
needed to be trusted explicitly: its default policy wants a non-repudiation key usage the demo leaf lacks).

**Not done:** a per-signature `/VRI` entry, fetching missing intermediates from the `caIssuers` address, checking that an OCSP response is
genuine, signed by the right issuer or current (it is stored as given), OCSP nonces, DSS for a signature field filled by a tool that already
wrote its own, and a `Crls`-less file for an issuer with only OCSP (works, but not tested against a reader that insists on both).
