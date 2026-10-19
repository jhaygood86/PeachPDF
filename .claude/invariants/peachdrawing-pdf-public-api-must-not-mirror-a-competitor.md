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
| `PdfComposer.AddPage`, `.Save`, `.PageCount` | Appending a page, writing the file, counting pages | Ordinary verbs and nouns for those operations |

## Rules

1. A new public type or member is named for what it does in this package's own terms, never copied from a PDF library's API. Check it against the
   competitors above before it lands and record it in the table.
2. The embedded engine (`Engine/`) is never reachable through the public surface: no public member's signature, base type or documentation names a
   type from it. `PeachDrawing.PDF.Tests`' `PublicSurfaceTests` snapshot makes a leak visible in review.
3. Until the `InternalsVisibleTo` bridge to PeachPDF is gone (see `.claude/recent-fixes/2026-10-19-pdf-writer-is-its-own-project.md`), the public API is
   the intended end state and the internals are a means to it; do not grow the bridge to avoid designing a public member.
