# How PeachPDF Is Tested

PeachPDF is a rendering engine, and rendering bugs are easy to ship and hard to notice — a PDF can contain exactly the right operators and still look wrong. This page describes how the project guards against that: the automated test suite, the continuous-integration pipeline that runs it, the coverage gate on every change, and the rasterization-based checks that verify output actually *looks* right rather than merely containing the expected tokens.

If you're contributing and want the exact local commands and conventions, see [CONTRIBUTING.md](https://github.com/jhaygood86/PeachPDF/blob/main/CONTRIBUTING.md) — this page is the higher-level overview of what runs and why.

## The test suite

The tests live in `src/PeachPDF.Tests`, an [xUnit](https://xunit.net/) project of {% if site.data.tests %}{{ site.data.tests.total }}{% else %}3,000+{% endif %} tests covering the HTML parser, CSS cascade, layout engines (block, inline, flex, table, multi-column), painting, the SVG subsystem, the font pipeline, and PDF output. The font and text engine, `PeachDrawing.Text`, has its own xUnit project, `src/PeachDrawing.Text.Tests`: the font parsers and tables, shaping, bidirectional text and script itemization, hyphenation, the Unicode conformance suites, and the public API of the package.

The suite multi-targets **`net8.0`, `net10.0`, and `net11.0`**, and all three target frameworks are first-class: continuous integration builds and runs the whole suite against each. (For routine *local* iteration you can run a single target framework to halve build/test time — see [CONTRIBUTING.md](https://github.com/jhaygood86/PeachPDF/blob/main/CONTRIBUTING.md) — but that's a local convenience, not the canonical way the project is validated.)

## Continuous integration

Every pull request and every push to `main` runs the [test workflow](https://github.com/jhaygood86/PeachPDF/blob/main/.github/workflows/test.yml) across a three-OS matrix — **`windows-latest`, `ubuntu-latest`, and `macos-latest`** — so platform-specific behavior (most often font discovery and text metrics) is exercised everywhere PeachPDF is expected to run. The work is split into parallel jobs so a run takes as long as its slowest leg rather than the sum of every step. On each OS:

1. three **test legs** run side by side: `main` (builds `PeachPDF.Tests` in Release, installs the **Playwright Chromium** browser its browser-backed tests use, and runs each target framework in turn), `engine` (the font and text engine's tests; every target framework on Ubuntu, net10.0 on Windows and macOS) and `small` (the CLI, drawing-abstraction, raster-backend and source-generator tests). All of them collect code coverage. macOS does not install the .NET 11 release-candidate SDK, so net11.0 is covered on Windows and Ubuntu;
2. a **coverage job** merges that OS's legs into one report and, on pull requests, enforces the **diff-coverage gate**; and
3. a separate job **builds the TestHarness** showcase generator (see [below](#the-showcase-harness)) and the Blazor WebAssembly demo.

A documentation-only change shouldn't pay for the full build/test cycle, but the workflow also feeds a required status check — so it always runs, and the heavy jobs above are gated on whether anything under `src/**` actually changed. When only docs change, they are skipped and the final `test-gate` job still reports success. That `test-gate` job is the single check to require: it fails if any test, coverage or build job failed or was cancelled. A newer push to the same pull request cancels its in-flight run, and every job has a timeout so a hung test host cannot hold a runner for hours.

## Coverage gate

Pull requests must meet **90% diff coverage** — the coverage of the lines the change actually touches, not the whole codebase. This is enforced by [diff-cover](https://github.com/Bachmann1234/diff_cover) comparing the change against its base branch, using [coverlet](https://github.com/coverlet-coverage/coverlet) output in Cobertura format (configured in `src/PeachPDF.Tests/coverlet.runsettings`). The HTML coverage report and the diff-coverage report are uploaded as build artifacts on every run, so a shortfall is easy to inspect.

The intent is simple: new and changed code arrives with tests, rather than leaving CI to discover the gap after merge.

## Rasterization-based verification (a development practice, not a CI gate)

A test that only asserts on substrings of the PDF content stream (`/SMask`, `Tj`, `/ShadingType`, and the like) is **not** proof that a feature renders correctly. A token can be fully present while the composed, positioned result is visually broken or blank — an entirely non-functional `<mask>` implementation once passed every substring test it had. For anything touching PDF graphics state — soft masks, patterns, clip paths, gradients, transparency groups, transforms — the automated suite prefers structural/adjacency assertions (for example, checking that a graphics-state operator and the drawing operator it modifies appear together) rather than a bare token search.

Beyond those automated assertions, the strongest check is to **rasterize the output and inspect the pixels** — and where transparency, soft-mask, or blend-mode output is involved, to rasterize with **two independent renderers, not one**:

- **[PDFium](https://pdfium.googlesource.com/pdfium/)** — the engine inside Chrome and Edge, and the stricter, more representative check.
- **[MuPDF](https://mupdf.com/)** — useful as a second opinion, but unusually lenient about transparency-group conformance: it will happily render content "correctly" that PDFium refuses.

Agreement between both is real evidence; a single lenient render that happens to look right is not. This two-renderer cross-check is **not part of the automated CI pipeline** — it's a manual verification step performed during development when a change touches graphics-state output, and it's a practice we recommend contributors run themselves before submitting such a change. (One way to do it is a short Python script using [`pymupdf`](https://pymupdf.readthedocs.io/) and [`pypdfium2`](https://github.com/pypdfium2-team/pypdfium2) to render the same page with each engine and compare the images.)

## The showcase harness

`src/PeachPDF.TestHarness` is a small runnable app that renders a gallery of feature showcases. It serves two purposes: it generates the PDFs behind the [Feature Showcase](showcase.html) on the documentation site, and it's a place to *visually* exercise a new capability. CI builds it on every source change so a break is caught before release day, not on it.

This matters because automated assertions alone have real blind spots. Several genuine rendering bugs — a paint-order regression, a broken soft mask, a gradient `spread-method` that was silently a no-op at render time — were caught only by looking at a showcase render, not by the token-level tests that were passing at the time.

## Benchmarks

`src/PeachDrawing.Text.Benchmarks` is a [BenchmarkDotNet](https://benchmarkdotnet.org/) project for measuring the font and text engine's hot paths and catching performance regressions. Today it covers TrueType and CFF hinting: a cold benchmark that loads every glyph of a bundled font at several sizes into an empty cache (with allocations reported), and a hot benchmark that looks glyphs up in a warm cache from one, four and eight threads at once. It reads the engine only through the public API, like the engine's tests, and isn't part of the per-PR gate or any CI job; it's run deliberately when a change is expected to affect performance. Run it from `src/`, in Release, with nothing else building or testing on the machine:

```
dotnet run -c Release --project PeachDrawing.Text.Benchmarks -- --filter "*HintingCold*"
dotnet run -c Release --project PeachDrawing.Text.Benchmarks -- --filter "*HintingHot*"
```

For timing whole-document rendering, the [showcase harness](#the-showcase-harness) has a `--benchmark` mode that times and measures the allocations of each showcase's render.

## See also

- [Architecture](architecture.md) — what each of these tests is verifying: the HTML → DOM → CSS → layout → paint → PDF pipeline.
- [CONTRIBUTING.md](https://github.com/jhaygood86/PeachPDF/blob/main/CONTRIBUTING.md) — exact local commands, testing conventions (layout-property assertions, `Canvas` recording mocks), and how to reproduce the coverage gate locally.
