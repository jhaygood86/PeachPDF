# Fix two real PDF/A conformance gaps found by an actual validator, not caught by v0.9.16's own tests

Found by running the `pdf_a_conformance` showcase (shipped in v0.9.16) through a real third-party
PDF/A validator (`pdfEngine`) - the first genuine ISO 19005 validation this feature had actually seen.
v0.9.16's own tests only checked structural markers (`/OutputIntents` presence, XMP well-formedness,
etc.), never the actual per-font/per-image dictionary completeness a real validator checks - exactly
the gap the original implementation plan's own testing section warned about ("run it through veraPDF
manually... this is the real proof the feature works") but which never actually happened before ship.

## The two bugs

1. **`PdfCIDFont` never wrote `/CIDToGIDMap`** (ISO 19005-2 §6.2.11.3 / ISO 32000-1 Table 117: a
   CIDFontType2 dictionary must contain this key - a stream or the name `/Identity`). PDF/A does not
   allow relying on the key's own spec default (`Identity`, when simply absent) - it must be explicit.
   Since PeachPDF always embeds text as Type0/CID fonts (`PdfType0Font`/`PdfCIDFont`), this fired on
   *every* piece of text in *every* PDF/A document - not a rare case.
2. **`PdfImage` could still write `/Interpolate true`** (ISO 19005-2 §6.2.8, present in every PDF/A
   part: the key, if present, must be `false`). `XImage.Interpolate` defaults to `true` for every
   image, so this fired on any PDF/A document containing a plain `<img>`.

## Load-bearing ideas

**`/CIDToGIDMap /Identity` isn't just spec-legal here, it's the actually-correct value** -
`OpenTypeFontface.CreateFontSubSet` (the only source of this class's embedded font bytes) never
renumbers glyph indices when building a subset: it walks every original glyph slot and zeroes out the
ones that go unused, but keeps the `loca` table at its original length. A CID written into the content
stream is always the *original* font's glyph index, and since indices are preserved unchanged, it's
also the *subset* font's own glyph index - Identity mapping is exact, not an approximation. So the fix
is simply to always write the key, unconditionally, in the constructor - no per-font Identity-vs-stream
decision was ever needed.

**`/Interpolate` is suppressed by conformance level, not made globally `false`** - `PdfImage` gained a
scoped `AllowInterpolate` property (`_image.Interpolate && Options.PdfAConformance == None`) rather
than just never writing the key at all, so ordinary (non-PDF/A) output keeps its existing smoothing
hint unchanged. Both write sites (JPEG and FLATE image paths) route through the same property.

## What this means for v0.9.16

Both bugs shipped in the v0.9.16 release (PR #892) - any PDF/A output generated with that version is
non-conformant in these two specific ways. This fix needs its own release; see the recent-fix/migration
-note convention for how that gets folded into the next version's notes.

## A third thing found along the way - not a bug, but a real trap for local validation

Regenerating the showcase via `dotnet run ... -c Debug` and validating it with veraPDF surfaced a
*third* apparent failure: ISO 19005-2 §6.1.9 ("spacingCompliesPDFA"), 48 failed checks, one per
indirect object. Traced to `PdfWriter`'s constructor: `_layout = PdfWriterLayout.Verbose;` is compiled
`#if DEBUG` only - a Debug build injects a `% PeachPDF.PdfSharpCore.Pdf.XxxType` comment after every
`N G obj` header (and extra separator lines), which breaks the exact "obj/endobj must each be
immediately followed by an EOL marker" rule this clause checks. A Release build (what `publish.yml`
actually packs and ships - `PdfWriterLayout` defaults to `Compact`, its zero-value enum member, when
the `#if DEBUG` branch doesn't compile in) never has this comment at all. Regenerating the same
showcase with `-c Release` and re-running veraPDF: **PASS** (`2a`), confirming this was a real but
Debug-build-only artifact, not a production defect - the doc comment on `PdfWriterLayout.Verbose`
already says as much ("useful for debugging purposes only... only Compact or Standard should be used
for production purposes"). Left as-is; noted here so a future PDF/A validation pass doesn't waste time
rediscovering it. **Always validate a `-c Release` build's output, never a Debug one.**

## Evidence

- Two new tests in `PdfAConformanceTests.cs`: `EmbeddedFont_AlwaysHasExplicitIdentityCidToGidMap`
  (regex-asserts `/CIDToGIDMap /Identity` in the saved bytes) and
  `Image_UnderPdfAConformance_NeverGetsInterpolateTrue` (asserts no `/Interpolate true` under PDF/A,
  *and* that the same image still gets it without PDF/A - proving the guard is correctly scoped, not a
  global behavior change).
- Regenerated the `pdf_a_conformance` showcase and confirmed directly against the raw saved bytes:
  `/CIDToGIDMap /Identity` present on all 3 embedded fonts, zero `/Interpolate` occurrences anywhere.
- **Independently confirmed with veraPDF 1.30.2** (installed unattended via its IzPack console
  installer's `-options` auto-install file - `java -jar cli-1.30.2.jar --format text -v <file>`)
  against a `-c Release` build of the showcase: `PASS ... 2a`, zero rule failures - real ISO 19005-2
  conformance, not just the structural marker checks this repo's own test suite can reach.
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings, 0 errors.
- Full `PdfAConformanceTests` suite (39 cases): all passing.
