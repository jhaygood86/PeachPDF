# Contributing to PeachPDF

Thanks for your interest in contributing. This document covers how to build, test, and submit changes.

## Getting started

- The solution is at `src/PeachPDF.sln`. PeachPDF targets .NET 8 and .NET 10.
- All `dotnet` CLI commands below assume your working directory is `src/` — the projects (and their relative paths in this doc) are rooted there.

## Building and testing

Run the test suite with a single target framework:

```
dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0
```

`PeachPDF.Tests` multi-targets net8.0 and net10.0. A bare `dotnet test` (no `--framework`) builds and runs the full suite (3000+ tests) twice in one invocation, which roughly doubles local build/test time — always pass `--framework net8.0` for routine local runs. Only add an explicit net10.0 run if you suspect a net10.0-specific issue.

To run a subset of tests:

```
dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0 --filter "FullyQualifiedName~Svg"
```

## Code coverage

CI enforces **90% diff coverage** on pull requests — the coverage of lines you actually changed, not the whole codebase — via `diff-cover` against the PR base branch (see `.github/workflows/test.yml` and `src/PeachPDF.Tests/coverlet.runsettings`, Cobertura format).

Reproduce this locally before opening or updating a PR:

```
dotnet test --collect:"XPlat Code Coverage" --settings PeachPDF.Tests/coverlet.runsettings --results-directory coverage
```

If your changed lines fall short of 90%, add tests to close the gap rather than relying on CI to catch it — a red diff-coverage check is one of the more common reasons a PR stalls in review.

## Testing conventions

- **A passing test that only checks PDF content-stream substrings (`/SMask`, `Tj`, `/ShadingType`, etc.) is not proof a feature renders correctly.** A token can be fully present while the actual composed/positioned result is visually broken or blank. For anything touching PDF graphics state — soft masks, patterns, clip paths, gradients, transparency groups, transforms — prefer structural/adjacency assertions (e.g. regex-checking that a `gs` and the `Do` it modifies appear on the same `cm` line), or better, rasterize the output and look at it.
- **Rasterize with two renderers, not one**, when verifying transparency/soft-mask/blend-mode output. MuPDF is unusually lenient about transparency-group conformance and can render content "correctly" that a stricter, more representative engine (PDFium — Chrome/Edge's engine) refuses. Agreement between both is real evidence; a single MuPDF render that looks right is not.
- When implementing a new SVG or CSS **paint** feature, a parser-level "did it parse into the right enum/value" test is not sufficient on its own — add an integration test that would fail if the feature were a complete no-op at render time.
- **Layout engine changes** (`CssLayoutEngine`/`CssLayoutEngineFlex`/`CssLayoutEngineTable`/`CssLayoutEngineColumns`, `CssBox.PerformLayoutImp`) need unit tests that assert the relevant `CssBox`'s properties after layout (`Location`, `ActualRight`/`ActualBottom`, etc.), not just that layout completes without throwing. Assert on every box the change affects, including children when the change affects child placement. See the harness pattern in `FlexboxIntegrationTests.cs`/`MulticolLayoutIntegrationTests.cs`: build a `HtmlContainerInt` + `PdfSharpAdapter`, call `PerformLayout` directly, then walk the box tree by id/class and assert positions/sizes.
- **Painting changes** need unit tests that confirm the actual sequence of calls made to the `RGraphics` adapter layer, not just that painting completes or that some token shows up in the final PDF. Use a test-only `RGraphics` mock (see `SpyGraphics` in `TransformIntegrationTests.cs`, `RecordingGraphics` in `CssLayoutEngineTablePageBreakTests.cs`) that records each invocation, then assert on the recording. When order across different call types matters, record into a single ordered log.
- Avoid writing tests against `PdfSharpCore.Fonts.FontFactory` (and OpenType neighbors) without care — it caches resolved fonts in `static readonly Dictionary` fields shared process-wide, and xUnit's parallel test-class execution makes new tests here a real order-dependent-flakiness risk.

## Documentation

When you add or change a user-facing feature, update the relevant doc page in the same PR — not as a follow-up:

- [docs/architecture.md](docs/architecture.md) — how HTML becomes a PDF
- [docs/html-css-support.md](docs/html-css-support.md) — HTML/CSS compatibility matrix
- [docs/supported-svg-features.md](docs/supported-svg-features.md) — SVG compatibility matrix
- [docs/usage-examples.md](docs/usage-examples.md) — API usage examples
- [README.md](README.md) / [docs/index.md](docs/index.md) — cross-link new features from these where relevant

If a change gives PeachPDF a new visible rendering capability, add or update a showcase in `src/PeachPDF.TestHarness/Program.cs` in the same change. Several real rendering-correctness bugs (paint-order issues, broken masks, no-op gradient spread methods) were only caught by visually exercising a showcase, not by automated tests alone.

## Architecture conventions

- Don't write two independent parsers for the same CSS value grammar across layers. If both the CSS-OM/parsing layer and a later render/resolution layer need to understand a value's grammar, extract it into one shared internal class both call (e.g. `CalcParser`, `BackgroundPositionGrammar`/`BackgroundSizeGrammar`). Only the final numeric resolution that genuinely depends on runtime-only information should differ between layers.
- The `Html/Adapters` layer (`RGraphics`/`RAdapter`/`RPen`/etc.) is the abstraction boundary between layout/paint logic and the concrete PDF backend (`PdfSharpCore`). New rendering primitives get added here first, then implemented in `GraphicsAdapter`/`XGraphics`/`XGraphicsPdfRenderer`. If you add a new abstract `RGraphics` member, update the test-only mocks (`SpyGraphics`, `RecordingGraphics`) too.
- Before building new PDF-writing infrastructure (patterns, soft masks, shadings), check whether `PdfSharpCore` already has an unused primitive for it — `XForm`/`PdfFormXObject`, `PdfTilingPattern`, `PdfSoftMask` have all been found pre-existing-but-uncalled at various points.

## Pull requests

- CI runs the test suite on `windows-latest`, `ubuntu-latest`, and `macos-latest` against both .NET 8 and .NET 10, and enforces the 90% diff-coverage gate described above.
- `@jhaygood86` is the default code owner for the entire repository and will be requested for review automatically.
- Keep PRs scoped to one change; include tests and doc updates in the same PR rather than as follow-ups.

## Third-party components

PeachPDF embeds a few third-party components directly in its source tree, each under its own license — see [THIRD-PARTY-LICENSES.md](THIRD-PARTY-LICENSES.md) for the full list (currently: an embedded PdfSharpCore fork, an adapted ExCSS-derived CSS parser, and bundled hyph-utf8 hyphenation pattern data). If your change touches one of those subtrees, make sure it stays consistent with that component's original license terms.
