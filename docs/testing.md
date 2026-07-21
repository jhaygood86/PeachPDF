# How PeachPDF Is Tested

PeachPDF is a rendering engine, and rendering bugs are easy to ship and hard to notice — a PDF can contain exactly the right operators and still look wrong. This page describes how the project guards against that: the automated test suite, the continuous-integration pipeline that runs it, the coverage gate on every change, and the rasterization-based checks that verify output actually *looks* right rather than merely containing the expected tokens.

If you're contributing and want the exact local commands and conventions, see [CONTRIBUTING.md](https://github.com/jhaygood86/PeachPDF/blob/main/CONTRIBUTING.md) — this page is the higher-level overview of what runs and why.

## The test suite

The tests live in `src/PeachPDF.Tests`, an [xUnit](https://xunit.net/) project of 3000+ tests covering the HTML parser, CSS cascade, layout engines (block, inline, flex, table, multi-column), painting, the SVG subsystem, the font pipeline, and PDF output.

The suite multi-targets **`net8.0` and `net10.0`**, and both target frameworks are first-class: continuous integration builds and runs the whole suite against each. (For routine *local* iteration you can run a single target framework to halve build/test time — see [CONTRIBUTING.md](https://github.com/jhaygood86/PeachPDF/blob/main/CONTRIBUTING.md) — but that's a local convenience, not the canonical way the project is validated.)

## Continuous integration

Every pull request and every push to `main` runs the [test workflow](https://github.com/jhaygood86/PeachPDF/blob/main/.github/workflows/test.yml) across a three-OS matrix — **`windows-latest`, `ubuntu-latest`, and `macos-latest`** — so platform-specific behavior (most often font discovery and text metrics) is exercised everywhere PeachPDF is expected to run. On each OS the job:

1. restores and **builds** `PeachPDF.Tests` in Release,
2. **builds the TestHarness** showcase generator (see [below](#the-showcase-harness)),
3. installs a **Playwright Chromium** browser for the rasterization-based tests,
4. runs `dotnet test` with code-coverage collection, and
5. on pull requests, enforces the **diff-coverage gate**.

A documentation-only change shouldn't pay for the full build/test cycle, but the test job is also a required status check — so it always runs, and the heavy steps above are gated on whether anything under `src/**` actually changed. When only docs change, those steps are skipped and the job still reports success.

## Coverage gate

Pull requests must meet **90% diff coverage** — the coverage of the lines the change actually touches, not the whole codebase. This is enforced by [diff-cover](https://github.com/Bachmann1234/diff_cover) comparing the change against its base branch, using [coverlet](https://github.com/coverlet-coverage/coverlet) output in Cobertura format (configured in `src/PeachPDF.Tests/coverlet.runsettings`). The HTML coverage report and the diff-coverage report are uploaded as build artifacts on every run, so a shortfall is easy to inspect.

The intent is simple: new and changed code arrives with tests, rather than leaving CI to discover the gap after merge.

## Rasterization-based verification

A test that only asserts on substrings of the PDF content stream (`/SMask`, `Tj`, `/ShadingType`, and the like) is **not** proof that a feature renders correctly. A token can be fully present while the composed, positioned result is visually broken or blank — an entirely non-functional `<mask>` implementation once passed every substring test it had. For anything touching PDF graphics state — soft masks, patterns, clip paths, gradients, transparency groups, transforms — the suite prefers structural/adjacency assertions (for example, checking that a graphics-state operator and the drawing operator it modifies appear together) or, better, **rasterizes the output and inspects the pixels**.

Where transparency, soft-mask, or blend-mode output is involved, the discipline is to rasterize with **two independent renderers, not one**:

- **[PDFium](https://pdfium.googlesource.com/pdfium/)** — the engine inside Chrome and Edge, and the stricter, more representative check.
- **[MuPDF](https://mupdf.com/)** — useful as a second opinion, but unusually lenient about transparency-group conformance: it will happily render content "correctly" that PDFium refuses.

Agreement between both is real evidence; a single lenient render that happens to look right is not. CI provisions a Chromium browser (via Playwright) precisely so the render-and-compare tests can run in the pipeline rather than only on a developer's machine.

## The showcase harness

`src/PeachPDF.TestHarness` is a small runnable app that renders a gallery of feature showcases. It serves two purposes: it generates the PDFs behind the [Feature Showcase](showcase.html) on the documentation site, and it's a place to *visually* exercise a new capability. CI builds it on every source change so a break is caught before release day, not on it.

This matters because automated assertions alone have real blind spots. Several genuine rendering bugs — a paint-order regression, a broken soft mask, a gradient `spread-method` that was silently a no-op at render time — were caught only by looking at a showcase render, not by the token-level tests that were passing at the time.

## Benchmarks

`src/PeachPDF.Benchmarks` is a [BenchmarkDotNet](https://benchmarkdotnet.org/) project for measuring rendering throughput and catching performance regressions. It isn't part of the per-PR gate; it's run deliberately when a change is expected to affect performance.

## See also

- [Architecture](architecture.md) — what each of these tests is verifying: the HTML → DOM → CSS → layout → paint → PDF pipeline.
- [CONTRIBUTING.md](https://github.com/jhaygood86/PeachPDF/blob/main/CONTRIBUTING.md) — exact local commands, testing conventions (layout-property assertions, `RGraphics` recording mocks), and how to reproduce the coverage gate locally.
