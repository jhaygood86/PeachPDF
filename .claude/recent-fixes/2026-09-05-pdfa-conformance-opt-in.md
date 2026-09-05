# Add opt-in PDF/A (ISO 19005) conformance: parts 1/2/3, levels B/U/A

New `PdfGenerateConfig.PdfAConformance` (default `PdfAConformance.None`, byte-for-byte unchanged
output), plus an independent `EnableXmpMetadata` opt-in and `PdfDocumentMetadata.CreationDate`/
`CustomXmpProperties`. Covers all eight non-`None` levels: `PdfA1B`/`PdfA1A`, `PdfA2B`/`PdfA2U`/
`PdfA2A`, `PdfA3B`/`PdfA3U`/`PdfA3A`.

## Load-bearing ideas

**PDF/A-1 is "detect and reject", not "flatten".** ISO 19005-1 forbids PDF transparency groups
entirely, and PeachPDF has no engine to flatten CSS/SVG opacity, alpha gradients, or an SVG `<mask>`
into a PDF/A-1-legal form. Building one was out of scope, so instead every place that would emit a
transparency-group-requiring construct (`PdfGraphicsState.RealizePen`/`RealizeBrush`/
`RealizeFillColor`, `XGraphicsPdfRenderer.DrawImageWithOpacity`/`DrawImageMasked`) calls
`PdfATransparencyGuard.RequireAllowed` first, which throws `PdfAConformanceException` (an
`InvalidOperationException` subclass - callers just see `InvalidOperationException`, the subclass is
an implementation detail) under `PdfA1B`/`PdfA1A`. A document that never uses one of these features
still generates correctly under PDF/A-1.

**Found in the process: `PdfPage.cs:584`'s `TransparencyUsed = true` was unconditional** ("TODO: check
XObjects", never implemented) - every page got a `/Group << /S /Transparency >>` regardless of content.
This had to become real, incremental tracking (only the 5 call sites above ever set it `true`) for two
reasons: PDF/A-1 would otherwise be unreachable even for a transparency-free document, and it's the
same set of call sites the rejection check needed anyway - one code change serves both. This is a
genuine, if narrow, output change for *all* documents (not just PDF/A ones) - see the matching
migration note.

**Only 7 call sites needed touching for transparency, not the whole CSS/SVG paint layer.** Traced every
CSS/SVG/image feature that can require a transparency group (opacity, fill/stroke-opacity, alpha
gradient stops, `<mask>`, an image's own alpha-channel `/SMask`, a non-default `/BM` blend mode) and
found they all funnel through 7 low-level sites in `PdfGraphicsState`/`XGraphicsPdfRenderer`/
`PdfImage` - `FragmentPainter`/`SvgRenderer`/`PdfShading` call into them but never need their own
guard. Two of the 7 (an image's alpha-channel `/SMask` in `PdfImage.cs`, and `SetBlendMode`'s `/BM`
ExtGState - used by both COLRv1 color-font compositing and CSS `outline-color: invert`) were missed in
the first pass and only found by a post-change multi-angle review (see below) - both are ordinary,
easily-reachable content (any transparent PNG; the default browser focus-outline color keyword), so
this was a real, not theoretical, conformance gap before the fix. `mix-blend-mode`/`isolation`/CSS
`mask`/`filter`/`backdrop-filter` are confirmed unimplemented today (no parser/paint hookup at all), so
nothing to guard for them yet - if any is implemented later, it must add the same `RequireAllowed` call.
New `PdfAUnimplementedTransparencyFeatureTests.cs` pins today's true no-op behavior for all five (page
content-stream equality with/without the declaration, plus a direct assertion that none of PDF/A-1's
forbidden constructs appear anywhere in the saved file) - implementing any of them will fail the
content-stream-equality half first, which is the intended tripwire to add the same guard call as part
of that work rather than just updating the pinned expectation.

**The reject-and-track pairing was consolidated into one overload** -
`PdfATransparencyGuard.RequireAllowed(document, page, featureDescription)` runs the PDF/A-1 check and,
if it passes, sets `page.TransparencyUsed = true` in one call, replacing the original "call
`RequireAllowed`, then separately remember to set the flag" two-step every one of the 7 sites used to
repeat by hand (the exact shape a later reviewer flagged as one hand-paired call away from silently
reopening this gap for an 8th future call site). The original single-argument overload
(`RequireAllowed(document, description)`, no page/flag) stays for `PdfImage.cs`'s alpha-channel-`/SMask`
site, which is deliberately page-independent - unlike an ExtGState-driven fill/stroke/opacity, an
image's own `/SMask` is honored directly by the imaging model and doesn't require the containing page
to carry its own `/Group`.

**The ICC profile is the real, ICC-published `sRGB2014.icc`** (3024 bytes, fetched from
`https://registry.color.org/rgb-registry/profiles/sRGB2014.icc`, verified against its own `cprt` tag
and the ICC's stated "may be copied, distributed, embedded, made, used, and sold without restriction"
license at `registry.color.org/profile-library/#license`) - embedded as a real assembly resource
(`PdfSharpCore/Resources/ColorProfiles/`, csproj `EmbeddedResource`), not under `assets/` (dev/test
-only, never packaged - the existing font assets there follow that same split).

**XMP is its own opt-in (`EnableXmpMetadata`), forced on by `PdfAConformance`, not fused to it** - a
caller wanting an XMP stream for a DAM/archival pipeline without full PDF/A conformance can ask for
just that. `pdfaid:part`/`pdfaid:conformance` are derived from `PdfAConformance`, never independently
settable (would otherwise let a caller claim a conformance level the output doesn't meet).
`PdfDocumentMetadata.CustomXmpProperties` (typed as `XElement`, not a raw string) is the escape hatch
for arbitrary additional metadata, composing with rather than replacing the required entries.

**`xmp:CreateDate` has no legal "unknown" value**, so both `EnableXmpMetadata` and `PdfAConformance`
throw `InvalidOperationException` if no creation date is resolvable (no `<meta>` date in the source
HTML, no `PdfDocumentMetadata.CreationDate` override) - same "fail loudly, don't write a placeholder"
stance as the accessible-level missing-language check.

**`DateTimeOffset.DateTime` is the wrong conversion for a value `PdfDate`'s `"zzz"` format is about to
read** - found in the same review pass. `.DateTime` returns `Kind=Unspecified`, and .NET's `"zzz"`
custom format specifier treats an `Unspecified` value as if it were local time, computing the offset
from the *machine's current local time zone* rather than the offset the value actually carried -
silently mislabeling `/CreationDate` (right wall-clock digits, wrong offset) whenever the resolved
`DateTimeOffset`'s own offset isn't the host machine's local offset (e.g. `DateTimeOffset.UtcNow` on
any non-UTC machine). `.LocalDateTime` is the fix: it converts to the equivalent `Kind=Local` value
first, so `"zzz"` then reports the correct offset for that same instant.

**`AddPdfPages` is a repeatable public API, and `PdfAConformance` is a whole-document property** - a
caller can call it more than once to append pages to the same `PeachPdfDocument`. Nothing stopped two
calls from requesting different `PdfAConformance` values, which would leave the document's already
-written `/OutputIntents`/XMP claiming a conformance level some of its pages were never actually
painted under (no PDF/A-1 guard active during the "wrong" call). Fixed with a new
`PdfDocumentOptions.PdfAConformanceEstablished` flag: the first `AddPdfPages` call locks in the
document's conformance level, and a later call requesting a different one throws
`InvalidOperationException` rather than silently producing a self-contradictory file. A later call
requesting the *same* level is fine and no longer duplicates work either - `SetOutputIntent` is now
guarded on "not already present" (the ICC profile/output intent content never varies call to call, so
re-adding it only orphaned the first call's now-unreferenced ICC stream object).

## What was deliberately not done

- No flattening engine for PDF/A-1's transparency restriction - see above.
- No PDF/A-3 "attach an arbitrary file" API - PeachPDF has none today, and PDF/A-3 only *permits* the
  allowance, it doesn't require exercising it.
- No CMYK output intent - `PdfGenerateConfig` has no CMYK color-mode surface at all
  (`PdfDocumentOptions.ColorMode` defaults to and is never set away from `Rgb` by anything reachable
  from the public API), so only the sRGB intent is ever needed.
- `PdfCatalog`'s own hardcoded `_version`/`Version` string field was left untouched - it turned out to
  be genuinely dead (never written to any element, `IsPdfVersionAtLeast` that reads it has zero
  callers). The one thing that actually reaches the file's `%PDF-x.y` header is `PdfDocument.Version`
  (an existing, working, range-checked `int` property PeachPDF just never set before), which is what
  `PdfA2*`/`PdfA3*` now bump to `17`.

**Post-change review pass (CLAUDE.md's required 8-angle multi-agent review) found 4 real bugs the
initial implementation + its own tests missed** - all fixed in the same change: the two missed
transparency-guard call sites above (`PdfImage.cs` alpha `/SMask`, `SetBlendMode`'s `/BM`), the
multi-call `AddPdfPages` conformance-consistency gap, and the `DateTimeOffset`/`"zzz"` date-offset bug.
Worth remembering: a diff that builds its own tests around the intended design (5 call sites, single
-call usage) can still pass 100% while missing gaps outside the specific shape those tests exercise -
the cross-file caller/callee trace and the removed-behavior audit (searching for *every* PDF construct
requiring a transparency group, not just the ones the diff itself touched) are what actually found
these, not line-by-line reading of the changed hunks.

## Evidence

- `PdfAConformanceTests.cs` (37 cases) + `PdfAUnimplementedTransparencyFeatureTests.cs` (5 cases):
  OutputIntent/ICC presence, XMP packet parsed as real XML with correct `pdfaid:part`/`conformance` and
  Info-dict-matching `dc:title`/`dc:creator`/`dc:description`/`pdf:Keywords`, PDF version per level, no
  `/Encrypt`/`/LZWDecode`, accessible-level `/Lang`/`MarkInfo.Marked`/empty-`/Alt`-on-no-`alt`, missing
  -language and missing-creation-date throws, `EnableXmpMetadata`/`PdfAConformance` independence,
  `CustomXmpProperties` composing with `pdfaid:*`, PDF/A-1 throwing for opacity/gradient-alpha/fill
  -opacity/an alpha-channel image/a non-default blend mode while succeeding (with no `/Group`) for
  transparency-free content, PDF/A-2 succeeding (with a `/Group`) for the same opacity case, multi-call
  `AddPdfPages` conformance-mismatch throwing and same-conformance succeeding with exactly one
  `/OutputIntents` entry, Info/XMP date agreement on the same instant regardless of host time zone, and
  the five still-unimplemented properties pinned as true no-ops under PDF/A-1.
- `dotnet test ... --filter "FullyQualifiedName~PdfAConformanceTests|FullyQualifiedName~PdfAUnimplementedTransparencyFeatureTests|FullyQualifiedName~PdfMetadataIntegrationTests"`:
  51+5 passed, 0 failed. A full-suite run on this machine hit a genuine, pre-existing `dotnet test`
  -host-crash instability (unrelated to this change - see the "Windows crash root cause" memory entry)
  twice in a row; the last attempt before the crash had run 8980/9871 tests with the two failures both
  being this change's own (since-fixed) date regression, and 0 unrelated failures. Full-suite
  confirmation is still outstanding as of this note - re-run before relying on this as complete
  regression coverage.
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings, 0 errors (re-verify after this note's fixes).
- New `pdf_a_conformance` TestHarness showcase (`PdfA2A`, alongside the existing `tagged_pdf` one).
