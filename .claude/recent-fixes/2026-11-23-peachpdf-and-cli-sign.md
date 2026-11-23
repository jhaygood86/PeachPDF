# PeachPDF and the CLI sign what they generate

`PdfGenerateConfig.Signing` (`PdfSigningOptions`, `PdfSignatureBox`, `PdfSignatureFormat` in `src/PeachPDF`) and `peachpdf --sign-cert=FILE.pfx`
(with `--sign-password[-file|-env]`, `--sign-format`, `--timestamp-url`, `--sign-box=PAGE,X,Y,W,H`, `--sign-name/-reason/-location/-contact`) reach
the writer's `PdfComposerOptions.Signing`. The signing code itself is in `PeachDrawing.PDF` ([the writer notes](2026-11-23-pdf-signing-validator-and-vri.md));
this layer is only the mapping.

**Shape:** PeachPDF keeps its own public option types and maps them in `Rendering/EngineOptions.cs` (the rule for every option; see `PdfEncryptionOptions`),
so the writer's types stay the writer's. The box is four plain numbers (`X`, `Y`, `Width`, `Height`, points from the top-left) rather than the writer's
`Rect`, which PeachPDF does not otherwise expose. A `TimestampAuthority` `Uri` plus an optional `TimestampClient` becomes the writer's
`PdfTimestampOptions.FromHttpAuthority`; with no client given a default `HttpClient` is made.

**Saving:** `PeachPdfDocument.SaveAsync(Stream, CancellationToken)` is new and the CLI always uses it. `Save` still works for every document except one
with a timestamp authority, which it refuses (the composer's rule: the authority is asked asynchronously). A caller who signs without a timestamp can
keep using `Save`.

**CLI traps:** the certificate is loaded in `CliRunner.BuildSigning` with `X509CertificateLoader.LoadPkcs12FromFile`, and a bad path or password throws an
`InvalidOperationException` that the runner reports as exit code 1 with the message (tested: no output file is left behind). The password can come from a
file or an environment variable like the encryption ones, because a command-line password is visible in the process list. `--sign-box` with `--pdfa` is
refused at parse time (the writer's rule, so the user gets the message before any rendering). Any `--sign-*` option without `--sign-cert` is an error
rather than silently ignored.

**Evidence:** `PdfSigningTests` (PeachPDF.Tests, 8) read the signature back with `PdfPageSource.GetSignatures` (intact, covers the whole file, reason/location/
name, CAdES sub-filter, visible text, encrypted file, extra certificates, `Save` refusing a timestamped document); `CliSigningTests` (17) cover the parser,
the errors, the password sources and two real runs (a signed file whose signature is intact, a wrong password).

**Not done:** no `--timestamp-url` end-to-end against a real authority in the suites (the library's tests use a local test authority; the CLI path only
maps the address), no certificate-from-the-store option, no signing of an existing PDF from the CLI (`PdfFileSigner` is library-only).
