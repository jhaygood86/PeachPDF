# The PeachDrawing.PDF public API must not mirror a competitor's

Same standing rule as [[peachdrawing-public-api-must-not-mirror-a-competitor]], [[core-public-api-must-not-mirror-a-competitor]] and
[[text-public-api-must-not-mirror-a-competitor]], scoped to the surface `PeachDrawing.PDF` publishes. Its collision risk is the highest of the
siblings: PDF-writing libraries (PDFsharp - whose engine this package embeds internally - iText, QuestPDF, PdfPig, MigraDoc) share a vocabulary
of `PdfDocument`, `PdfPage`, `PdfWriter`, `PdfSharp...`, and a "too similar" objection would be raised against exactly those names. The PDFsharp
types stay `internal` for this reason as well as for API stability: nothing public here may be one of them, or a thin renaming of one.

## Register

| Public name | Role | Origin of the name and shape |
|---|---|---|
| `PdfRenderContext` (extends `RenderContext`) | The context a PDF canvas draws with: fonts, images, the layout-unit scale | `<Backend><Base>` naming of the already-registered `RasterRenderContext`/`RenderContext` pair |
| `PdfCanvas` (sealed, extends `Canvas`) | The PDF drawing surface for one page | Same `<Backend><Base>` pattern as `RasterCanvas`; the type existed under this name inside PeachPDF before the package did |
| `PdfComposer` | Builds one PDF file from pages | Ours; "composer" names the act of putting pages together and deliberately is not `PdfDocument`/`PdfWriter`, which every competitor uses for this role |
| `PdfComposerOptions` | What a composer writes besides the pages | `<Type>Options`, the ordinary .NET options-object naming |
| `PdfVersion`, `PdfPermissions`, `PdfEncryptionOptions` | The version, the permission flags and the password settings | The PDF specification's own terms (ISO 32000, "version", the permissions word, the standard security handler's user/owner passwords); they moved here unchanged from PeachPDF's public API, where they predate the package |
| `PdfPageLabelRange`, `PdfPageLabelStyle`, `PdfComposer.AddPageLabels` | The ranges a viewer labels pages by ("iv", "A-3"), their numbering styles and adding them | ISO 32000-1 12.4.2's own terms ("page label", the five numbering styles, prefix, start); `AddPageLabels` follows `AddBookmarks`. No competitor in the list has a writer for these (QuestPDF has none; the others name it differently or not at all), and permissively licensed libraries' naming would have been acceptable anyway |
| `PdfSigningOptions` (including `PrivateKey`, `KeyPin`, `AllowKeyPrompt`), `PdfSignatureFormat`, `PdfComposerOptions.Signing` | Signing the saved file with a certificate | `<Type>Options` like `PdfComposerOptions`; the format names are the specifications' own subfilters ("PKCS#7 detached", "CAdES detached"). Checked against iText (`PdfSigner`, `PdfSignatureAppearance`), PDFsharp (no signing) and others: none of those names is used, and `Signing` is the plain noun for the act |
| `PdfTimestampOptions`, `PdfSigningOptions.Timestamp`, `PdfComposer.SaveAsync` | Having an RFC 3161 authority vouch for the signing time; the awaitable save it needs | RFC 3161's own term ("time stamping authority", "timestamp"); `Exchange` names the one request/response round trip the host supplies, not any competitor's `ITSAClient`/`TSAClientBouncyCastle` shape. `SaveAsync` is the ordinary async counterpart of `Save` |
| `PdfSignedSourcePolicy` (`Warn`, `Reject`), `PdfOperation.WithSignedSourcePolicy` | What an operation does with a signed source file | Follows `PdfCopyMode`/`WithCopyMode`, the neighbouring option and its setter on the same type; the values are the plain verbs for the two outcomes. No competitor in the list has a merge operation with this knob |
| `PdfFileSigner` (`Sign`, `SignAsync`, `AddDocumentTimestampAsync`) | Signing or timestamping a PDF that already exists, by incremental update | Named for the thing it works on (a file) and what it does. Deliberately **not** `PdfSigner`, which is iText's class for this role: that name was the plan's first draft and was changed on the register check. "Incremental update" and "document timestamp" are ISO 32000's own terms |
| `PdfSourceSignature`, `PdfPageSource.GetSignatures` | What a PDF's signatures and document timestamps state and whether they verify | Follows the read-side `PdfSource*` family already registered (`PdfSourceLink`, `PdfSourceConformance`...) and `PdfPageSource.GetLinks`/`GetOutline`. Not iText's `SignatureUtil`/`PdfPKCS7`/`SignatureValidator`, PDFsharp's or PDFBox's `PDSignature` shapes; "intact" (digest and signature agree with the covered bytes) is used instead of "valid", which would claim a trust decision this does not make |
| `PdfComposer.AddPage`, `.Save`, `.PageCount` | Appending a page, writing the file, counting pages | Ordinary verbs and nouns for those operations |

## Rules

1. A new public type or member is named for what it does in this package's own terms, never copied from a PDF library's API. Check it against the
   competitors above before it lands and record it in the table.
2. The embedded engine (`Engine/`) is never reachable through the public surface: no public member's signature, base type or documentation names a
   type from it. `PeachDrawing.PDF.Tests`' `PublicSurfaceTests` snapshot makes a leak visible in review.
3. No assembly but `PeachDrawing.PDF.Tests` has `InternalsVisibleTo` access (a test pins it). PeachPDF reaches the writer only through the public API,
   so a capability PeachPDF needs and the API lacks is added to the API - designed in the package's own terms and named by rule 1 - and never reached
   for through a new internals grant.
