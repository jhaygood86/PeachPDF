# PeachPDF

Pure .NET HTML → PDF rendering library. No external process dependency (no Puppeteer/wkhtmltopdf/gs) — HTML parsing, CSS cascade, layout, and PDF writing all run in-process. Targets .NET 8 and .NET 10.

## Documentation map

Read these before making non-trivial changes in their area — they are the source of truth, not this file:

- [docs/index.md](docs/index.md) — doc site landing page / guide index
- [docs/architecture.md](docs/architecture.md) — how HTML becomes a PDF: parser, DOM, CSS, layout, painting, PDF renderer
- [docs/html-css-support.md](docs/html-css-support.md) — full HTML/CSS compatibility matrix (elements, properties, selectors, at-rules, gaps, extensions)
- [docs/supported-svg-features.md](docs/supported-svg-features.md) — full SVG compatibility matrix (inline `<svg>` and standalone), rendered as real vector PDF content
- [docs/usage-examples.md](docs/usage-examples.md) — copy-pasteable API usage (local HTML, MHTML, HTTP fetch, thread safety, ASP.NET Core/Azure Functions)
- [README.md](README.md) — package overview, install, quick start, fonts

When you add or change user-facing features, update the relevant doc page (and its `README.md`/`docs/index.md` cross-links) in the same change, rather than as a follow-up — this repo's convention (established by the SVG 1.0 coverage work) is docs land with the feature.

If a new or changed feature gives PeachPDF a new visible capability, add a new showcase or update an existing one in `src/PeachPDF.TestHarness/Program.cs` in the same change — this is also how several rendering-correctness bugs (paint-order, broken masks, no-op gradient spread methods) were actually caught, since automated tests alone missed them (see Testing conventions below).

## Project layout

- `src/PeachPDF/` — the library. Notable subtrees: `CSS/` (CSS-OM: tokenizer, parser, value converters), `Html/Core/` (DOM, cascade, layout, paint handlers), `Html/Adapters/` (`RGraphics`/`RAdapter`/etc. abstraction layer), `Svg/` (native SVG tree builder + renderer), `PdfSharpCore/` (embedded fork of PDFsharp — see below, has its own [LICENSE](src/PeachPDF/PdfSharpCore/LICENSE.md)).
- `src/PeachPDF.Tests/` — xUnit test suite, multi-targets net8.0/net10.0.
- `src/PeachPDF.TestHarness/` — a runnable showcase app for visually exercising features (`Program.cs`).
- `src/PeachPDF.Benchmarks/` — BenchmarkDotNet project.
- Working directory for `dotnet build`/`dotnet test` is `src/` — if a shell session resets to repo root, commands silently fail with "Project file does not exist"; always confirm you're in `src/` first.

## Critical: `dotnet test` invocation

**Always pass `--framework net8.0`.** The test project multi-targets net8.0 and net10.0; a bare `dotnet test` builds and runs the ~3000+ test suite twice in one invocation. Repeated bare invocations across a long session have crashed the user's Windows machine from cumulative build/test load. Single-target coverage is sufficient for regressions; only run net10.0 explicitly if a net10.0-specific issue is actually suspected.

```
dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0
dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0 --filter "FullyQualifiedName~Svg"
```

Batch verification — run the full suite once after a meaningful chunk of related changes, not after every individual file edit.

### Coverage

CI enforces 90% diff coverage on PRs (`diff-cover` against the PR base, via `.github/workflows/test.yml` + `src/PeachPDF.Tests/coverlet.runsettings`, Cobertura format). To reproduce locally:

```
dotnet test --collect:"XPlat Code Coverage" --settings PeachPDF.Tests/coverlet.runsettings --results-directory coverage
```

Before considering any non-trivial code change complete, run the command above and check diff coverage on the lines you changed. If new/changed code falls short of the 90% diff-coverage threshold CI enforces, add tests to close the gap before finishing — don't leave it for CI to catch.

Avoid writing tests against `PdfSharpCore.Fonts.FontFactory` (and OpenType neighbors) without care — it caches resolved fonts in `static readonly Dictionary` fields shared process-wide, and xUnit's parallel test-class execution makes new tests here a real order-dependent-flakiness risk against the rest of the suite.

## Testing conventions

- **A passing test that only checks PDF content-stream substrings (`/SMask`, `Tj`, `/ShadingType`, etc.) is not proof a feature renders correctly.** A token can be 100% present while the actual composed/positioned result is visually broken or blank — this exact gap let a fully-broken `<mask>` implementation pass 16/16 tests. For anything touching PDF graphics state — soft masks, patterns, clip paths, gradients, transparency groups, transforms — prefer structural/adjacency assertions (e.g. regex-checking that a `gs` and the `Do` it modifies appear on the same `cm` line) or, better, actually rasterize the output and look at it.
- **Rasterize with two renderers, not one**, when verifying transparency/soft-mask/blend-mode output — MuPDF is unusually lenient about transparency-group conformance and can render content "correctly" that a stricter, more representative engine (PDFium — Chrome/Edge's engine) refuses. Agreement between both is real evidence; a single MuPDF render that looks right is not.

  ```bash
  python3 -m pip install --quiet pymupdf pypdfium2 pillow   # once per machine

  python3 -c "
  import fitz
  doc = fitz.open('path/to/file.pdf')
  doc[PAGE_INDEX].get_pixmap(dpi=150).save('out_mupdf.png')
  "
  python3 -c "
  import pypdfium2 as pdfium
  pdf = pdfium.PdfDocument('path/to/file.pdf')
  pdf[PAGE_INDEX].render(scale=2.0).to_pil().save('out_pdfium.png')
  "
  ```
  Then view both PNGs with the Read tool and compare.
- When implementing a new SVG (or CSS) **paint** feature, a parser-level "did it parse into the right enum/value" test is not sufficient on its own — always add an integration test that would fail if the feature were a complete no-op (a prior gradient `spreadMethod` bug shipped with only a parser test and was a no-op at render time for months).
- **Layout engine changes** (`CssLayoutEngine`/`CssLayoutEngineFlex`/`CssLayoutEngineTable`/`CssLayoutEngineColumns`, `CssBox.PerformLayoutImp`) need unit tests that assert the relevant `CssBox`'s properties after layout — `Location`, `ActualRight`/`ActualBottom` (size), etc. — not just that layout completes without throwing. Assert on every box the change affects, including children when the change affects child placement (e.g. a multi-column child's `Location.X`/`Location.Y`, a flex item's sizing). Use the lightweight harness pattern in `FlexboxIntegrationTests.cs`/`MulticolLayoutIntegrationTests.cs`: build a `HtmlContainerInt` + `PdfSharpAdapter`, call `PerformLayout` directly, then walk the box tree by id/class and assert positions/sizes — no full PDF generation needed.
- **Painting changes** need unit tests that confirm the actual sequence of calls made to the `RGraphics` adapter layer — which calls, in what order — not just that painting completes or that some token shows up in the final PDF (see the content-stream-substring pitfall above). Use a test-only `RGraphics` mock (see `SpyGraphics` in `TransformIntegrationTests.cs`, `RecordingGraphics` in `CssLayoutEngineTablePageBreakTests.cs`) that overrides the relevant methods to record each invocation, then paint through it and assert on the recording. When order *across different call types* matters (e.g. background drawn before border, a clip pushed before the content it clips), record into a single ordered log rather than the separate per-call-type counts/lists those two existing mocks use — extend the pattern to fit, don't duplicate a parallel one.
- Don't copy test files verbatim from upstream `empira/PDFsharp` into `src/PeachPDF.Tests/PdfSharpCore` — this fork is a ~2016-era snapshot (write-only, no `PdfReader`/`Lexer`/`ContentReader`, no Attachments/Signatures/Security/PdfA/Forms/Metadata, different font subsystem). A 96-file verbatim batch yielded only 5 portable files; the rest had to be deleted. Fresh, fork-native tests are the better path to closing coverage gaps.

## Value normalization to know before asserting in tests

- Named CSS colors are normalized to `rgb(r, g, b)` form at parse time (`color: blue` → `CssBox.Color == "rgb(0, 0, 255)"`), except `initial` which resolves to the literal string `"black"`.
- `em` font-sizes are eagerly converted to points at cascade time in the `CssBoxProperties.FontSize` setter (relative to the parent's actual font size in pt), not kept symbolic — assert against the converted `pt` value, not the original `em` string.

## Architecture conventions

- **Don't write two independent parsers for the same CSS value grammar across layers.** If both the CSS-OM/parsing layer and a later render/resolution layer need to understand a value's grammar (tokenization + classification), extract it into one shared internal class both call — only the final numeric resolution that genuinely depends on runtime-only information (percentage-against-box-size, `calc()` evaluation) should differ between layers. Precedent: `CalcParser` (shared across `PeachPDF.CSS` ⇄ `PeachPDF.Html.Core`), `BackgroundPositionGrammar`/`BackgroundSizeGrammar` (shared between the CSS-OM converters and `BackgroundLayerResolver`).
- The `Html/Adapters` layer (`RGraphics`/`RAdapter`/`RPen`/etc.) is the abstraction boundary between layout/paint logic and the concrete PDF backend (`PdfSharpCore`). New rendering primitives (tiling, soft masks, dash patterns, etc.) get added here first, then implemented in `GraphicsAdapter`/`XGraphics`/`XGraphicsPdfRenderer`. Check whether an abstract `RGraphics` member you're adding needs updates to the test-only mock implementations (`SpyGraphics` in `TransformIntegrationTests.cs`, `RecordingGraphics` in `CssLayoutEngineTablePageBreakTests.cs`).
- Before building new PDF-writing infrastructure (patterns, soft masks, shadings), check whether `PdfSharpCore` already has an unused primitive for it (`XForm`/`PdfFormXObject`, `PdfTilingPattern`, `PdfSoftMask` have all been found pre-existing-but-uncalled at various points) rather than assuming it needs to be built from scratch.
- A `/Luminosity` soft mask's `/G` form AND the content form it masks both need their own `/Group << /S /Transparency /CS ... /I true >>` transparency-group dictionary for spec-conformant readers to actually apply the mask — `XForm`/`PdfFormXObject` don't set this automatically.

## Out of scope / accepted gaps (don't relitigate without new information)

- SMIL animation, scripting, `<cursor>`/`<view>`, legacy SVG glyph-outline fonts, `<filter>`+`fe*` primitives, `<foreignObject>`, `icc-color` in SVG.
- CSS/SVG `opacity` double-blending in nested transparency groups (a real pre-existing gap, cross-cutting fix not yet attempted).
- `background-attachment` (no visual effect possible in a static PDF).
- `:visited`/`:active` (and other interaction-state pseudo-classes) never match, by design — there is no browsing history or interaction/hover state in a static PDF renderer. `CssData.DoesSelectorMatch(PseudoClassSelector, ...)` intentionally only matches `:root` and `:link`.
- A MimeKit HTML-tokenizer bug hoists a `<style>` element out of a nested inline `<svg>` block when parsed through the full HTML pipeline, so inline `<svg><style>` never applies. Standalone/`<img src="x.svg">` SVG is unaffected. Fix would live in `HtmlParser.cs`'s box-tree construction for foreign content, not in SVG-specific code.
- `::marker` marker-box width/height/margin/padding/alignment (the CSS Lists Level 3 "marker box" layout model) is not implemented — the spec itself (§3.1.1) declares this layout "not fully defined" and restricts applicable properties to `content`/`color`/font properties/`direction` (all of which PeachPDF fully supports on `::marker`, including real per-item numbering via the `list-item` counter and `<ol start>`/`<ol reversed>`/`<li value>`) — no browser implements marker-box sizing either, for the same reason. See [Pseudo-elements](docs/html-css-support.md#pseudo-elements).
- Tagged PDF: anonymous (CSS-generated, e.g. `display: table-cell` on a `<div>`) table structure cannot have its `TR`/`TH`-or-`TD`/`THead`/`TBody`/`TFoot` tagging overridden via `-peachpdf-pdf-tag-type` — the synthesized anonymous boxes have no source element for any selector to match. Real `<table>`/`<tr>`/`<td>` markup is required for override control. See [Tagged PDF (PDF/UA) Support](docs/html-css-support.md#tagged-pdf-pdfua-support).
- `@font-face`'s `unicode-range` descriptor is parsed but not honored — every source applies to the full character range regardless of a declared subset. Real per-codepoint source selection would require per-character font selection, a much larger undertaking than this repo's per-run resolution model.
- Font selection happens per-run (per `CssBox`), not per-character — a run with mixed-script text or symbols the resolved font lacks glyphs for shows missing-glyph boxes for those characters rather than pulling matching glyphs from a fallback font. No CSS/Fonts-spec requirement mandates per-character fallback; it's a much larger text-shaping undertaking than this plan's scope. See [Font selection is per-run, not per-character](docs/html-css-support.md#font-selection-is-per-run-not-per-character).

## Thread safety

A `PdfGenerator` instance is not thread-safe — never call it concurrently or reuse one instance across overlapping renders. Use a separate instance per thread/request/batch item; see [Thread safety](docs/usage-examples.md#thread-safety) in usage-examples.md.
