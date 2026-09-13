# MathML/tagged-PDF-math tests register the bundled math font directly, not via a CSS data: URI

Test-only performance fix. No production behaviour change (the underlying CSS-engine cost this works
around is real and still present - see "Follow-up" below).

## What was wrong

GitHub Actions job-step timestamps (`gh run view <id> --json jobs`) showed the Windows `Test (net8.0)`/
`(net10.0)`/`(net11.0)` steps holding steady at 3-6 minutes each from 2026-08-31 through the push
immediately before `d0134788` ("Add MathML 3 rendering with native PDF 2.0 embedding", #1019), then
jumping to 14/16/16 minutes in the very next push - a uniform ~3x increase across all three TFMs. That
commit's actual diff to shared layout/paint/parse code was cheap (a handful of `box is CssBoxSvg or
CssBoxMath` type checks), so the regression wasn't a global per-render slowdown - it had to be
something about the ~150 tests the PR added.

Direct measurement confirmed it: 179 MathML/tagged-PDF-math tests averaged ~218ms each versus ~14ms
each for a comparably-sized, non-math layout test class (`FlexboxIntegrationTests`/
`MulticolLayoutIntegrationTests`) - roughly 16x slower per test, uniformly, regardless of which of the
five affected test files or which specific test ran.

Isolating why (all measured directly, not inferred):

- `PdfSharpAdapter`/`FontResolver`/`TtfFontDescription` construction and direct font registration
  (`adapter.AddFont(stream, name)`) each cost ~1ms - not the cause.
- Parsing the same HTML with the font referenced via an `<img src="data:...">` attribute: ~11ms - fast.
- Parsing the *same-length* value through a CSS `url(data:...)` - either `@font-face src` or
  `background-image` - regardless of whether any element actually used it: ~550-700ms, every time,
  with zero improvement across repeated calls in the same process. Not font-specific, not MathML-specific: a `background-image: url(data:...)` with no `<math>` in sight was exactly as slow.
- Raw `StringBuilder` char-append over the same 2-million-character string: ~6ms. `Uri.UnescapeDataString`
  and `Convert.FromBase64String` on it: ~1ms combined. So it wasn't tokenizing or decoding either.
- `GC.GetAllocatedBytesForCurrentThread()` around one `SetHtml` call parsing the CSS `url(data:...)`:
  **~200MB allocated and 2-4 Gen2 collections**, for a single ~2MB CSS value. Gen2 collections are
  exactly what a sampled CPU profile of the same run showed as `UNMANAGED_CODE_TIME` dominating the
  trace (GC/runtime-internal time).

**Correction, found after this fix landed**: the paragraph above originally attributed that ~200MB to
`Token.Data`'s (`src/PeachPDF/CSS/Tokens/Token.cs:57`) re-materializing its backing string on every
access. That guess was wrong in magnitude, not just detail. Instrumenting `Token.Data`'s getter
directly showed it was read only 2-3 times for the whole declaration (~4-6MB, real but a small
fraction). Allocation checkpoints through `DomParser.GenerateCssTree` then found the actual dominant
cost - 134.8MB, 71% of the total - in `CssBidiParagraphResolver.AssignBidiLevels`, which had no guard
against `display: none` content and was running full Unicode bidi/script resolution over the
`<style>` tag's own raw text. See
[2026-09-12-bidi-resolver-skips-display-none-subtrees.md](2026-09-12-bidi-resolver-skips-display-none-subtrees.md)
for that fix - it is what actually explains most of this slowdown, on top of the font-registration fix
this file describes.

## The fix (test-only)

Not a change to `Token`/the CSS engine - see "Follow-up" for why. Instead, the four MathML/tagged-PDF-
math test files stopped embedding the bundled 1.5MB `StixTwoMath-Regular.ttf` as a CSS `@font-face`
data: URI (`BundledFonts.FontFaceRule`) and register it directly instead - the same real font, the same
effective family registration, none of the CSS `url()` parsing:

- Added `BundledFonts.RegisterFont(adapter, fontPath, familyName)` - a thin wrapper over
  `PdfSharpAdapter.AddFont(Stream, string?)`, the ~1ms path already measured above.
- `MathLayoutIntegrationTests.cs`: both layout helpers call it on the adapter before `SetHtml`, and the
  CSS no longer needs an `@font-face` rule at all - just `math { font-family: TestMath; }`.
- `LayoutHarness.LayoutAsync` (the shared 962-call-site harness) gained an optional
  `configureAdapter` hook that runs on the freshly-constructed adapter before `SetHtml` - additive,
  every other call site's behavior is unchanged. `MathPaintTests.cs` uses it to register the font the
  same way, since it goes through the harness rather than building its own `HtmlContainerInt`.
- `MathSmokeTests.cs`/`TaggedPdfMathFormulaTests.cs` use the public `PdfGenerator` API
  (`GeneratePdf`), which only exposes font registration under the font's own sniffed name
  (`PdfGenerator.AddFontFromStream(Stream)`, no name override). Rather than rename every test's CSS/
  assertions from `TestMath` to `STIX Two Math`, they call `generator.AddFontFamilyMapping("TestMath",
  "STIX Two Math")` after registering it - the family name every test already used keeps working
  unchanged.

`BundledFonts.FontFaceRule` itself is untouched and still used by other, non-math tests with much
smaller bundled fonts, where this cost is negligible.

## Why this is safe

Every affected test still renders through the exact same real `StixTwoMath-Regular.ttf` bytes, under
the same family name its markup/assertions already reference (`TestMath`) - only *how* the font gets
registered on the adapter/generator changed (a direct API call instead of a CSS declaration that
resolves to the same registration). None of these tests are about CSS `@font-face` parsing itself
(that has its own dedicated tests elsewhere); MathML layout/paint/tagging is what they verify, and that
is unchanged by which registration path supplied the font.

## Evidence

All 179 previously-affected tests, net8.0, single-threaded:

| | before | after |
| --- | ---: | ---: |
| Duration | 39s | 1s |
| Passed / Failed | 179 / 0 | 179 / 0 |

Full `PeachPDF.Tests` suite, net8.0, single-threaded
(`dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0 -- xunit.maxParallelThreads=1`),
same machine, combined with the unrelated font-cache fix in
[2026-09-12-pdfsharpadapter-reuses-cached-system-font-names.md](2026-09-12-pdfsharpadapter-reuses-cached-system-font-names.md):

| | original baseline | after both fixes |
| --- | ---: | ---: |
| Duration | 2m 33s | 1m 5s |
| Passed / Skipped / Total | 11031 / 9 / 11040 | 11031 / 9 / 11040 |

**~57.5% reduction**, identical pass/skip count both times.

- Full net8.0 suite, both before and after: 11031 passed, 9 skipped, 0 failed.
- Rebuild (`dotnet build PeachPDF.slnx -t:Rebuild`): 0 warnings, 0 errors.

## Follow-up

`Token.Data` allocating a fresh string on every access (rather than the ~2-3x measured here) is still
a real, independent PeachPDF CSS-engine cost for any token whose `.Data` genuinely gets read more than
once - see
[2026-09-12-token-data-becomes-span-only.md](2026-09-12-token-data-becomes-span-only.md) for that fix
(`Token.Data` now returns `ReadOnlySpan<char>`; the allocating string getter is gone).
