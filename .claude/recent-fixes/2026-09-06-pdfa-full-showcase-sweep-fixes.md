# Full-showcase PDF/A sweep: build a reusable harness, find and fix three more real gaps

Follow-up to [2026-09-06-pdfa-real-validator-conformance-gaps.md](2026-09-06-pdfa-real-validator-conformance-gaps.md),
which fixed two bugs found by validating a single showcase. This time the ask was broader: convert
*every* showcase (109 at the time) to PDF/A and validate all of them with veraPDF, on the theory that
a single showcase's content can't exercise every code path the feature touches.

## The harness

A `PDFA_SWEEP=1` env var switch in `PeachPDF.TestHarness/Program.cs`: for every showcase, in addition
to its normal save, clone the showcase's own `PdfGenerateConfig` (same page size/margins/fonts/etc.,
via a `ClonePdfAConfig` helper - a real object clone, not a shared mutable instance, since two
generations must not observe each other's `PdfAConformance`/`Metadata` overrides) with
`PdfAConformance = PdfA2B` and a fixed `CreationDate`, generate again, and write the result to
`<outputDir>/pdfa-sweep/<slug>.pdf`. A generation failure is caught and logged per-showcase rather than
aborting the sweep - the point of a sweep is to see every failure in one pass, not stop at the first.
Then `java -jar cli-1.30.2.jar --format text -v -r <dir>` validates the whole directory in one run.

## The three bugs found

1. **`PdfAnnotation.Initialize` never set `/F`** (ISO 19005 §6.3.2: every annotation dictionary must
   carry an explicit `/F` flags key). Every showcase with a link (`<a href>`) failed. Fix: set
   `Elements.SetInteger(Keys.F, (int)PdfAnnotationFlags.Print)` in `Initialize()` - unconditional, not
   gated on `PdfAConformance`, since a plain "annotation with the sane default flag" is correct for
   every render, not just PDF/A ones (mirrors the `/CIDToGIDMap` fix's "always write the compliant
   default" shape from the prior note).
2. **`PdfWriter.WriteStream` only wrote the trailing EOL conditionally** (ISO 19005 §6.1.7.1: a stream's
   content, then EOL, then `endstream`, unconditionally). The existing code was
   `if (_lastCat != CharCat.NewLine) WriteRaw('\n');` - skipping the EOL whenever a stream's last content
   byte happened to already equal `0x0A`. That's not the same as "the stream already ends in a proper
   EOL marker" for binary content (a font subset or image whose last byte is coincidentally `0x0A` is
   arbitrary binary data, not a line terminator) - and skipping it desyncs the previously-computed
   `/Length` value from the actual byte count written, which is exactly what tripped the rule. Fix: make
   the trailing `WriteRaw('\n')` unconditional.
3. **`.notdef` (glyph index 0) could reach a text-showing operator** (ISO 19005-2 §6.2.11.8, present in
   every PDF/A part). `XGraphicsPdfRenderer.DrawString` shapes text through `descriptor.Shape(...)` and
   writes each glyph's CID straight into the `Tj` string, with no check for whether shaping actually
   found a glyph. A character with no coverage in the requested font *and* no fallback font covering it
   (e.g. `devanagari_use`/`bengali_gujarati_tamil_use`/`svg_arabic_devanagari_shaping`'s Latin fallback
   font not covering their own script's characters, or an emoji showcase using a font with no color-glyph
   coverage) shapes to glyph 0 and gets written anyway. Fix: `RequireNoMissingGlyphsForPdfA` walks the
   shaped glyph list right after `Shape()` returns and throws before any `Tj` byte is written, if
   `PdfAConformance != None` and any glyph's index is 0.

## Load-bearing ideas

**Detect-and-reject, not "pick a substitute glyph."** PeachPDF has no glyph-substitution/notdef-fallback
mechanism, and inventing one only for the PDF/A path would be new rendering behavior with its own
correctness burden. Throwing (the same "fail loudly, name the fix" stance as every other PDF/A rejection
in this feature) is consistent with the entire feature's design and correctly identifies a genuine
content problem: PDF/A-2/3 don't forbid transparency the way PDF/A-1 does, so this rejection is not a
"could add a flattener" gap - there is no way to *render* a codepoint that has no glyph anywhere.

**The exception must stay a `PdfAConformanceException`, not a plain `InvalidOperationException`.**
`FragmentPainter.cs`'s paint loop has a broad `catch (Exception ex)` that rewraps any painting failure
into `HtmlRenderException: Exception in box paint`, discarding the original message. This is correct
for unexpected paint bugs, but wrong for a deliberate PDF/A validation failure - the caller needs the
actual actionable message ("the font X has no glyph for..."), not a generic wrapper. Reused (and
generalized) the `PdfAConformanceException` marker type `PdfATransparencyGuard.cs` already defined for
the transparency-rejection case, rather than adding a second exception type - one class now documents
both use cases, and `FragmentPainter`'s existing `catch (PdfAConformanceException) { throw; }`
special-case (added for transparency) covers this rejection for free.

## A debugging trap worth recording: `dotnet run --no-build` after a project-only rebuild

After fixing all three bugs, rebuilding only `PeachPDF/PeachPDF.csproj` and then re-running the sweep
via `dotnet run --project PeachPDF.TestHarness/PeachPDF.TestHarness.csproj ... --no-build` produced
confusing results: generation succeeded for all 109 showcases (no `.notdef` rejections at all), but
veraPDF still failed identically on `emoji.pdf`. `--no-build` skips the step that normally cascades a
project reference's rebuild into copying the updated DLL into the *referencing* project's output
directory - so the TestHarness was still running against a stale `PeachPDF.dll` that predated the
`.notdef` fix, silently. Confirmed by decompressing the generated PDF's content stream directly
(`zlib.decompress` in Python) and finding a literal `<0000> Tj` still present. **Fix: after changing the
library, do a real (non-`--no-build`) build of whatever executable references it** - a full
`dotnet build PeachPDF.TestHarness/PeachPDF.TestHarness.csproj -c Release --framework net8.0` before the
`dotnet run ... --no-build` sweep, not just of the library project alone.

## Evidence

- First sweep (before these fixes): 91 PASS, 18 FAIL across exactly these 3 rules (§6.3.2-1 ×8,
  §6.2.11.8-1 ×8, §6.1.7.1-1 ×2).
- Final sweep (after all three fixes, on a verified fresh Release build of both the library and the
  TestHarness): generation produced 101/109 PDFs; the other 8 threw the expected, unwrapped
  `PdfAConformanceException` (not a generic `HtmlRenderException`) for the `.notdef` case -
  `list_style_type`, `content_image`, `text_overflow`, `print_catalog`, `emoji`, `devanagari_use`,
  `bengali_gujarati_tamil_use`, `svg_arabic_devanagari_shaping` (each already has a narrow/mismatched
  font-family choice in its own showcase HTML, which is what should reject - not a regression in
  otherwise-valid showcases).
- veraPDF 1.30.2 run recursively over all 101 successfully-generated files: **101 PASS, 0 FAIL** - both
  the F-key and stream-Length fixes confirmed genuinely effective across the whole showcase set, not
  just theoretically correct.
- Two new regression tests added to `PdfAConformanceTests.cs`:
  `LinkAnnotation_AlwaysHasExplicitFFlags_RegardlessOfPdfAConformance` and
  `MissingGlyph_NoFallbackCoversCharacter_ThrowsPdfAConformanceException` (both also assert the
  non-PDF/A path is unaffected, matching this file's existing "prove the guard is scoped, not global"
  convention). No dedicated regression test was added for the stream-Length/EOL fix specifically - it's
  implicitly covered by every existing PDF/A test that saves a document with embedded font/image binary
  content (any of which would corrupt every subsequent object's byte offsets if `/Length` desynced), and
  the veraPDF full-sweep run above is the actual proof this class of bug is fixed, not a unit assertion
  on an internal writer detail.
