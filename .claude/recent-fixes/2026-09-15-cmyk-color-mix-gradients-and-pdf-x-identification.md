# Same-space CMYK color-mix()/gradients, real PDF/X version identification, and two declarative-path bugs

Closes the two accepted gaps left behind by the earlier `device-cmyk()`/PDF/X change
(`.claude/recent-fixes/2026-09-15-device-cmyk-colors-and-pdf-x-conformance.md`):
`device-cmyk-color-mix-and-gradient-stops-unsupported.md` and `pdf-x-version-header-left-at-1-7.md`, both
deleted. Also fixes two real bugs a post-change review agent found in that same uncommitted diff: the
declarative document-builder path never enforced PDF/A/PDF/X conformance it claimed, and two doc spots
still described Phase A's `ColorOptions.ConversionMode` as unimplemented after Phase B implemented it.

## Same-space CMYK `color-mix()`/gradients

**Load-bearing idea**: neither gap needed a real RGB↔CMYK conversion to close - both gap files' own "what
would close this" sections already scoped the fix to the *same-space* case (two CMYK operands, or a
gradient whose stops are all CMYK), leaving the genuinely undefined mixed-space case still rejected.

- `Color.MixCmyk` (new, `CSS/Values/Color.cs`) mixes two CMYK-tagged operands with the same premultiplied
  -alpha per-channel interpolation shape as the existing RGB `Color.Mix`, just over C/M/Y/K instead of a
  `ColorSpaceMath` rectangular space - deliberately a project-specific extension, not literal CSS Color 5
  behavior (the spec has no "cmyk" interpolation space, and its own fallback for a profile-less
  `device-cmyk()` operand is exactly the naive formula this project has rejected everywhere else).
  `ColorFunctionExtensions.ParseColorMix` branches on both operands' `IsDeviceCmyk`: both true → `MixCmyk`
  (declared `in <space>`/hue ignored - no CMYK equivalent exists); exactly one true → still invalid, same
  as before.
- Gradients: the real bug was `PdfShading` reading `_document.Options.ColorMode` (always `Undefined` →
  always `/DeviceRGB`) for a shading's `/ColorSpace`, while `PdfEncoders.ToString` already dispatched
  CMYK/RGB *per color* correctly even under `Undefined` - so an unguarded CMYK stop would have produced a
  `/DeviceRGB`-declared shading with 4-component `/C0`/`/C1` arrays, real PDF corruption. Fixed by a new
  `PdfShading.ResolveShadingColorMode(colors)` (mirrors `PdfGraphicsState.ResolveEffectiveColorMode`'s
  per-color pattern, but over a shading's own stop array) used in place of the document-wide read at all 4
  shading-construction call sites (legacy 2-color linear/radial, multi-stop linear, multi-stop radial) -
  each needed its stop-color array hoisted above the `/ColorSpace` assignment it now depends on.
- `PdfSharpAdapter.RejectCmykGradientStops` → `RejectMixedColorSpaceGradientStops`: now throws only when
  stops are a genuine mix of CMYK and non-CMYK, not simply "any CMYK".
- **Conic gradient (Type 4 Gouraud mesh) needed real changes, not just the color-mode plumbing above** - it
  hand-packs raw bytes per vertex rather than going through `PdfEncoders`. `AppendConicVertex`/`BuildConicMeshData`
  now take a `cmyk` flag and write 4 component bytes (C/M/Y/K, quantized 0-255) instead of 3 (R/G/B) in CMYK
  mode, `/Decode` grows from 3 to 4 trailing `0 1` component-range pairs, and a new `LerpXColorCmyk`
  interpolates C/M/Y/K directly instead of R/G/B (meaningless for a CMYK-tagged `XColor`). Component count
  in `/Decode`, bytes-per-vertex, and `/ColorSpace` all have to agree exactly or a reader misinterprets the
  mesh geometry entirely - exactly the corruption risk the original gap warned about, so this got explicit
  structural test coverage (`/Decode` pair count) alongside the usual content-stream assertions.

## Real PDF/X version header + `GTS_PDFXVersion`/`GTS_PDFXConformance` identification

Per explicit user decision, PeachPDF's `PdfXConformance` levels target the **2003-era ISO revisions** for
X1a/X3 (ISO 15930-4/ISO 15930-6, PDF 1.4 base) rather than the original 2001/2002 revisions (PDF 1.3 base);
X4 (ISO 15930-7) has only the one revision, PDF 1.6 base. `PdfGenerator.EstablishDocumentOptions` (see
below) sets `document.PdfDocument.Version` accordingly - X1a/X3 need no explicit assignment at all, since
14 (PDF 1.4) is already PeachPDF's own historical default (`PdfVersionTests.Default_Pdf17_KeepsHistoricalVersion14`);
only X4's bump to 16 needed a real line.

Identification is written in both places real PDF/X tooling checks: the Info dictionary (`/GTS_PDFXVersion`,
and for X1a only, `/GTS_PDFXConformance`) and the XMP packet (mirrors the Info dict under the original
Adobe `pdfx` namespace for every level, plus `pdfxid` - the newer ISO-registered schema - additionally for
X4, which ISO 15930-7 names as that level's primary identification mechanism). Both mechanisms are driven
from one shared source of truth, `PdfMetadataStream.PdfXIdentifiers`, so they can't drift apart.

**The real ISO 15930 text is paywalled** - verified instead by cross-referencing multiple independent
secondary sources (ISO's own standard abstracts, IDEAlliance/Adobe PDF/X application notes, the `iText`
library's `PdfXConformanceImp` reference implementation) and, most precisely, the widely-deployed LaTeX
`pdfx` package's real `pdfx.xmp` XMP template - its actual conditional logic (`\ifnum\xmp@Part<3`/`>3`)
gave an exact, internally-consistent picture no prose summary matched: `GTS_PDFXConformance` is written
only for part &lt; 3 (X1a only, among PeachPDF's three levels), and `pdfxid:GTS_PDFXVersion` only for part
&gt; 3 (X4 only) - a detail an earlier WebFetch-summarized source got wrong (claimed `GTS_PDFXConformance`
was required for every level with a literal value of `"F"`, which doesn't match any other source and was
discarded as a probable summarization artifact). Called out here, same as this project's PDF/A work's own
veraPDF-gap precedent, as best-available verification rather than a primary-source citation.

## A third bug found only by rasterizing - not in PeachPDF, but in a showcase's own test fixture

Rasterizing the `pdf_x1a_conformance` showcase to verify the version/identification work (per this repo's
own "rasterize with two renderers" convention) turned up a real MuPDF/PDFium disagreement: PDFium rendered
the showcase's one genuinely chromatic `device-cmyk()` paragraph in its authored orange; MuPDF rendered the
exact same paragraph as solid black. Traced (via a minimal isolated repro - the same color with no
`PdfXConformance` at all rendered identically in both renderers, isolating the cause to something specific
to the PDF/X path) to `ShowcaseCmykIccProfile`'s `OutputIntent` ICC profile: **its `A2B0` CLUT was entirely
zeroed** (deliberately, since colorimetric accuracy previously didn't matter - the profile only needed to
parse as a valid 4-channel CMYK profile). Once this change gave the document real `GTS_PDFXVersion`
identification (previously it had none at all), MuPDF recognized it as genuine, verifiable PDF/X content and
started applying the `OutputIntent` profile for real ICC-managed CMYK rendering - correct, spec-permitted
reader behavior - which mapped every CMYK input through the zeroed CLUT to the same (black) output. PDFium
doesn't apply OutputIntent-based color management for on-screen preview, so it never exercised this.

Not a PeachPDF defect - the actual PDF content stream bytes were independently confirmed correct (the exact
`0 0.81 0.94 0 k` operator, verified by decompressing and reading the content stream directly) - but a
misleading-looking showcase is still worth fixing, since a real press ICC profile in production wouldn't
have this problem. Fixed `ShowcaseCmykIccProfile`'s CLUT to populate its 16 corner grid points with a real
sRGB(naive-CMYK)→XYZ(D50) conversion, correctly encoded per ICC.1:2010 §6.3.4.2's `lut8Type` PCSXYZ byte
range - the first attempt (reusing the naive RGB bytes directly as if they were already XYZ) produced a
non-degenerate but *wrong-hued* (magenta instead of orange) result once actually applying the real
sRGB→XYZ→sRGB round-trip a colorimetric engine performs, which is what caught that the encoding, not just
"is it zeroed," mattered. Verified by rasterizing again after each attempt: zeroed → solid black in MuPDF;
naive-bytes-as-XYZ → wrong hue (still disagreed with PDFium); real XYZ conversion → pixel-identical to
PDFium.

## Two bugs found by the post-change review agent, unrelated to either gap

1. **The declarative document-builder path (`PdfGenerator.AddPages`/`CreateDocument`) never wired
   `PdfDocumentOptions.ColorMode`/`.PdfAConformance`/`.PdfXConformance`/`.ColorOptions`/`.PdfVersion`** -
   only the HTML path (`AddPdfPages`) did, in an inline block. `RenderPagesCore` (shared by both paths)
   still validated `config` and wrote a fully-formed `/OutputIntents`/XMP conformance claim regardless of
   which path built the document - but the actual paint-time *enforcement* of that claim
   (`PdfGraphicsState.ResolveEffectiveColorMode`, `PdfXColorSpaceGuard`, `PdfColorConversionGuard`,
   `PdfATransparencyGuard`) all read `document.PdfDocument.Options.*`, which stayed at bare defaults
   (`ColorMode = Rgb`, `PdfXConformance = None`, ...) for a declaratively-built document. Net effect: a
   `CreateDocument` call requesting `PdfXConformance.X1a` produced a PDF whose catalog genuinely claimed
   PDF/X-1a conformance while none of X1a's actual restrictions (CMYK-only content, no live transparency)
   were enforced - a document that lied about its own conformance. No test exercised this combination,
   which is why it wasn't caught. Fixed by extracting the whole block into `PdfGenerator.EstablishDocumentOptions`,
   called from both `AddPdfPages` and `AddPages` (once per top-level call, matching `AddPdfPages`'s own
   "first call establishes it, a later mismatched call throws" granularity - `AddPages` is documented as
   equally repeatable). `DeclarativeApiConformanceIntegrationTests.CreateDocument_X1aConformance_ChromaticRgbBackground_Throws`
   is the regression test that would have caught this - confirmed it failed with the bug present (wrong
   exception type entirely - the document generated successfully) before the fix, and passes after.
2. **`docs/usage-examples.md`'s PDF/X section and `PdfGenerateConfig.ColorOptions`'s XML doc comment both
   still said `ConversionMode` "isn't implemented yet" and throws `NotSupportedException`** - stale Phase A
   language never updated when Phase B (same diff, same session) actually implemented real ICC conversion.
   Both rewritten to document the real, tested feature (`PdfColorConversionGuard`, `PdfColorConversionTests.cs`),
   with a working usage example added to the docs page.

## What was deliberately not done

- **A mixed RGB/`device-cmyk()` `color-mix()` or gradient still has no defined result** and keeps failing
  to parse / throwing at generation time - this remains a genuine design gap (no real ICC conversion is
  reachable at CSS-parse time, and even if it were, the project's "no naive approximation" stance would
  still block a meaningful default), but per the closed gap file's own scope, this was never promised to be
  fixed by this change - only the same-space case was.
- **PDF/X-1a:2001/PDF/X-3:2002 (the older ISO revisions) are not offered** - `PdfXConformance` has no enum
  value for them. A caller with a specific requirement for the older revision has no way to request it
  today; adding one would need its own enum member and version-mapping branch, out of scope here.

## Evidence

- New/updated tests: `Color.MixCmyk`/`color-mix()` CMYK coverage (`DeviceCmykColorParsingTests.cs`), gradient
  same-space CMYK structural coverage across linear/radial/conic including the mixed-stop rejection
  (`DeviceCmykGradientIntegrationTests.cs`, `PdfSharpAdapterCmykGradientTests.cs`), PDF/X version header +
  Info-dict/XMP identification per level (`PdfXConformanceTests.cs`, parsed via `System.Xml.Linq`, not
  substring matching), and the declarative-path conformance-enforcement regression
  (`DeclarativeApiConformanceIntegrationTests.cs`).
- Full suite and targeted regression runs (PdfXConformanceTests, PdfAConformanceTests, PdfColorConversionTests,
  PdfVersionTests, DeclarativeApiIntegrationTests, ModernColorParsingTests, gradient integration tests) all
  passing with no regressions.
- `dotnet build PeachPDF/PeachPDF.csproj` and the test project both build with zero errors/warnings at each
  step of this change.
