# Contributing to PeachPDF

Thanks for your interest in contributing. This document covers how to build, test, and submit changes.

## Getting started

- The solution is at `src/PeachPDF.slnx`. PeachPDF targets .NET 8 and .NET 10, plus .NET 11 (currently a release candidate) when the SDK you're building with supports it — see [net11.0 locally](#net110-locally) below. A plain `dotnet build`/`dotnet test` with only the .NET 10 SDK installed works exactly as it always has; nothing extra is required unless you specifically want to build or test against net11.0.
- All `dotnet` CLI commands below assume your working directory is `src/` — the projects (and their relative paths in this doc) are rooted there.

## Building and testing

Run the test suite with a single target framework:

```
dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0
```

`PeachPDF.Tests` multi-targets net8.0 and net10.0 by default (net11.0 joins the list only when you're building with an SDK that supports it — see below). A bare `dotnet test` (no `--framework`) builds and runs the full suite (3000+ tests) once per resolved target framework in one invocation, which roughly doubles (or triples, with net11.0 active) local build/test time — always pass `--framework net8.0` for routine local runs. Only add an explicit net10.0 or net11.0 run if you suspect an issue specific to that target.

### net11.0 locally

The repo's tracked `global.json` pins a `10.0.100` floor with `rollForward: "latestMajor"` and `allowPrerelease: true`, and `src/Directory.Build.props` only adds `net11.0` to the multi-targeted projects' `TargetFrameworks` when the SDK actually in use is 11.0.100 or newer. Combined, this means no configuration is needed either way: with only the .NET 10 SDK installed, `dotnet` resolves to it and net11.0 is quietly not offered; install the [.NET 11 RC SDK](https://dotnet.microsoft.com/download/dotnet/11.0) alongside it and `dotnet` automatically promotes to the RC SDK, bringing net11.0 into the list — no `global.json` edits, local or committed.

```
dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net11.0
```

(That command only works once the RC SDK is actually installed — see above.)

The `peachpdf` command-line tool has its own test project, `PeachPDF.Cli.Tests` (net10.0-only, since the CLI is net10.0-only):

```
dotnet test PeachPDF.Cli.Tests/PeachPDF.Cli.Tests.csproj
```

The font and text engine has its own test project too, `PeachDrawing.Text.Tests` (the font parsers and tables, shaping, bidi, script itemization, hyphenation, and the public API of `PeachDrawing.Text`; it must not reference PeachPDF):

```
dotnet test PeachDrawing.Text.Tests/PeachDrawing.Text.Tests.csproj --framework net8.0
```

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
- Avoid writing tests against `FontFactory` (in `src/PeachDrawing.Text`) (and OpenType neighbors) without care — it caches resolved fonts in `static readonly Dictionary` fields shared process-wide, and xUnit's parallel test-class execution makes new tests here a real order-dependent-flakiness risk.

## Documentation

When you add or change a user-facing feature, update the relevant doc page in the same PR — not as a follow-up:

- [docs/architecture.md](docs/architecture.md) — how HTML becomes a PDF
- [docs/html-css-support.md](docs/html-css-support.md) — HTML/CSS compatibility matrix
- [docs/supported-svg-features.md](docs/supported-svg-features.md) — SVG compatibility matrix
- [docs/usage-examples.md](docs/usage-examples.md) — API usage examples
- [docs/testing.md](docs/testing.md) — reader-facing overview of how the project is tested (the suite, CI, coverage gate, and two-renderer rasterization); the commands and conventions here are the contributor-facing complement
- [README.md](README.md) / [docs/getting-started.md](docs/getting-started.md) — cross-link new features from these where relevant

If a change gives PeachPDF a new visible rendering capability, add or update a showcase in `src/PeachPDF.TestHarness/Program.cs` in the same change. Several real rendering-correctness bugs (paint-order issues, broken masks, no-op gradient spread methods) were only caught by visually exercising a showcase, not by automated tests alone.

## Architecture conventions

- Don't write two independent parsers for the same CSS value grammar across layers. If both the CSS-OM/parsing layer and a later render/resolution layer need to understand a value's grammar, extract it into one shared internal class both call (e.g. `CalcParser`, `BackgroundPositionGrammar`/`BackgroundSizeGrammar`). Only the final numeric resolution that genuinely depends on runtime-only information should differ between layers.
- The `Html/Adapters` layer (`RGraphics`/`RAdapter`/`RPen`/etc.) is the abstraction boundary between layout/paint logic and the concrete PDF backend (`PdfSharpCore`). New rendering primitives get added here first, then implemented in `GraphicsAdapter`/`XGraphics`/`XGraphicsPdfRenderer`. If you add a new abstract `RGraphics` member, update the test-only mocks (`SpyGraphics`, `RecordingGraphics`) too.
- Before building new PDF-writing infrastructure (patterns, soft masks, shadings), check whether `PdfSharpCore` already has an unused primitive for it — `XForm`/`PdfFormXObject`, `PdfTilingPattern`, `PdfSoftMask` have all been found pre-existing-but-uncalled at various points.

## Pull requests

- CI runs the test suite on `windows-latest`, `ubuntu-latest`, and `macos-latest` against .NET 8, .NET 10, and .NET 11, and enforces the 90% diff-coverage gate described above.
- `@jhaygood86` is the default code owner for the entire repository and will be requested for review automatically.
- Keep PRs scoped to one change; include tests and doc updates in the same PR rather than as follow-ups.

### Adversarial review prompt example

Most of the defects that slip past a passing test suite in this codebase are the same few kinds: content that silently disappears on a page break, a fix that changes ordinary documents it was never meant to touch, state that drifts each time the tree is laid out again, docs and gap notes that describe behaviour the change has since altered, and tests that pass on both the old and the new code. The prompt below asks a reviewer to hunt for exactly those, by measuring instead of reading. Give it to a human reviewer or to an AI coding agent, replacing `{PR_NUMBER}`, and run it on your own branch before you ask for review; a PR that already survives it moves through review much faster.

It is an example, not a required template. Adapt the sections to the kind of change, and skip the ones that do not apply. The prompt itself says which checks to run for which kind of change.

````text
You are an independent, adversarial reviewer for a pull request in jhaygood86/PeachPDF (a pure .NET
HTML -> PDF library). Review PR #{PR_NUMBER}. Assume the change is wrong until you have measured otherwise.
Do NOT push commits, comment on the PR, approve, merge or close anything, and do not spawn further agents.
Report findings only.

## Ground rules
- Read CLAUDE.md at the repo root first and apply all of it. Key rules: the whole solution must build with 0
  warnings (`dotnet build PeachPDF.slnx -t:Rebuild` from `src/`); run tests with
  `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0` and never bare `dotnet test`; spec claims
  are checked against real spec text fetched from w3.org/whatwg.org, not recalled; paint-affecting changes are
  rasterized through BOTH PDFium (pypdfium2) and MuPDF (PyMuPDF) and viewed; diff coverage must be >= 90%;
  no AI attribution in commits or PR text (a grep for "claude" false-positives on `.claude/` paths, so check
  `Co-Authored-By`, `Generated with`, `claude.ai/code`, `noreply@anthropic` instead); no issue/PR numbers in docs/**
  or README.
- Never trust the PR's own tests, description, coverage number or notes as evidence. Reproduce everything.
- Baseline = `git merge-base origin/main <head>`. Build baseline and head in their own `git worktree`s (never
  `git stash`, never the stale local `main`; prefix `ref:path` git commands with `MSYS_NO_PATHCONV=1` on Git Bash
  for Windows). Build FRESH binaries for both (`dotnet build PeachPDF.Cli/PeachPDF.Cli.csproj -c Release -o <dir>`
  from each worktree's `src/`) and confirm the two PeachPDF.dll files differ. Never reuse binaries from earlier runs.
- Reference renderer for layout/pagination is headless Chrome (`--headless --disable-gpu --print-to-pdf=... --no-pdf-header-footer`,
  or `--screenshot` for non-paged checks). Measure it; do not guess what a browser does.
- Only stop processes you started, by PID. Never kill all python/dotnet processes.
- Write scratch files outside the repo. Remove every worktree you create and shut down the build server when done.

## Step 0: intake and gates
1. `gh pr view {PR_NUMBER} --json title,body,author,isDraft,isCrossRepository,headRefOid,mergeable,statusCheckRollup,commits,files,closingIssuesReferences`
   and `gh pr diff {PR_NUMBER}`. Fetch with `git fetch origin pull/{PR_NUMBER}/head:pr-{PR_NUMBER}`.
2. Report these gates up front (they decide whether the PR may merge, independent of your findings):
   author, draft status, CI state per platform, head SHA, whether `origin/main` has moved past the merge-base.
3. If the PR is from a fork/external contributor: read the entire diff for anything beyond the stated purpose
   (workflow/CI files, csproj/props/global.json/targets, scripts, network or file-system access in tests,
   hidden or unusual characters, generated binaries). Check what the test workflow would execute and with which
   permissions/secrets before anything is run.
4. Verify every issue the PR or its `.claude/**` files cite exists and check whether it is open or closed.
5. Classify the change and record it, because it decides which sections below apply:
   layout/fragmentation | paint/PDF output | CSS parsing/cascade/registry | SVG | fonts/text shaping | HTML parsing |
   API/CLI | performance | docs/notes only | tests/tooling only | mixed.

## Applicability matrix (run what the classification calls for; say explicitly which sections you skipped and why)
- layout/fragmentation: sections 1-10 all.
- paint/PDF output: 1, 2, 4, 5 (rasterization), 6, 7, 8, 9, 10; fuzz only if layout is affected.
- CSS parsing/cascade/registry: 1, 2, 7, 8, 9, 10, plus generator diagnostics (PPG codes) and value grammar tests.
- SVG / fonts / HTML parsing: 1, 2, 4, 6, 7, 8, 9, 10; add a real-document render comparison.
- performance: 1, 4 (timings are the main evidence), 6, 7, 10.
- docs/notes only: 8, 9 (verify every claim against code and measurements), 10.
- tests/tooling only: 1, 7 (do the tests fail on baseline and pass on head?), 10.

## 1. Read the diff adversarially
List every function changed and every caller of each. For anything that moves boxes or lines, mutates layout
state, changes which page/fragmentainer content lands on, changes inheritance/cascade, or changes a shared
utility, audit ALL callers for double application, missed application, and state that survives into a later
layout pass. Look for statics, caches, epsilons, retry counts, magic numbers and heuristic allow-lists. An
allow-list of placements must also be checked against what is INSIDE the thing it allows. Check that the
change respects the architecture rules in CLAUDE.md (paint consumes only the fragment tree and never reads
CssBox geometry; paint is synchronous; new rendering primitives go through the RGraphics adapter layer; do not
write two parsers for one CSS grammar; the property registry is generated from css-properties.json).

## 2. Reproduce the claim
- The exact repro from the issue/PR: baseline vs head vs Chrome, PDFium AND MuPDF, and LOOK at the PNGs.
  Extract text and coordinates with PyMuPDF; measure, don't eyeball.
- Then at least 30 hand-written variants around the shape: different containers (block, table cell, list item,
  flex/grid item, float, inline-block, multi-column, abs/fixed), nesting, rtl/vertical writing modes, different
  page sizes (sweep a dimension in 0.5pt steps around any boundary to expose off-by-epsilon bugs).
- Any content present on the baseline or in Chrome but missing or duplicated on head is a blocker. Content
  drawn outside its page band or in a margin is a blocker. Being further from Chrome than the baseline is a
  candidate blocker.

## 3. Content-preservation fuzz (when layout, fragmentation, floats, tables or text are touched)
Generate >= 800 random documents using the constructs the change touches, with unique numbered words
(e.g. `w12_7`), small pages so pagination is heavy, and varied placements. Render with baseline and head and
compare per document: the set of source words present in the PDF, per-word multiplicity, page count, text
outside the content band, crashes/timeouts. Minimize every head-only loss or duplicate to a small repro.
Separate losses the change CAUSES from "exposed" pre-existing bugs (reproduce on baseline with the changed
property removed) and require an open issue for each exposed one. Target: zero head-only loss or duplication.

## 4. Ordinary documents must not change
Build >= 100 ordinary documents that do not exercise the fix (short cards, tables, floats, lists, forms,
footnotes, columns, images, links). Page content streams must be byte-identical to baseline, or each difference
must be explained and shown to move toward Chrome. Measure layout time on a 280-page ordinary document and a
document with thousands of the touched elements: the ratio head/baseline must be ~1.0 unless the change is
explicitly about performance. Check a large real-world-style document, not just toy inputs.

## 5. Repeated-layout stability and determinism
PdfGenerator lays out twice (measure pass then final pass under ShrinkToFit/ScaleToPageSize) and some documents
lay out repeatedly. Lay the same tree out 5-6 times with LayoutHarness.LayoutRepeatedlyAsync (see
FlexboxIntegrationTests.cs for the pattern) and assert every box's Location/size, word positions and line
positions are identical across passes AND equal to a fresh layout. Cover the repro plus any moved/relocated
content, table cells with vertical-align, rowspan across a page break, abs boxes, multicol and repeated table
headers. Render the same input twice through the CLI and byte-compare. Any drift is a blocker even if invisible today.

## 6. Every showcase
Run src/PeachPDF.TestHarness on baseline twice (to measure noise) and on head once. Compare page content streams
byte-for-byte, then rasterize differences in PDFium and MuPDF. Any changed showcase is a regression until proven
intended and judged against Chrome. If the change adds a visible capability there must be a showcase registered
through `SaveShowcaseAsync`, and its caption must be true against Chrome and the render.

## 7. Tests and mutation testing
- Run the full net8.0 suite; run every new/changed test class individually.
- Run the new tests against the BASELINE code and confirm they fail there. A test that passes on both is vacuous.
- Tests must assert box/line geometry after layout, or an ordered RGraphics call log (see CLAUDE.md Testing
  conventions), never only PDF content-stream substrings. Paint features need a test that fails if the feature
  is a no-op.
- Judge every edit to pre-existing tests on the merits: intent-preserving, or weakened to fit new behaviour?
- Mutation testing: revert or neuter ONE piece at a time (each condition, list entry, epsilon, guard, cache reset,
  early return), run the new test class plus related classes, then the full suite for survivors, and restore.
  Every survivor is a test gap: add a test or justify it as an equivalent mutant. List all survivors.
- Check that fixtures do not depend on system fonts: an unpinned `font-family` can pass on Windows and fail on
  Linux/macOS CI. New tests should pin a bundled font from `assets/fonts/`.

## 8. Spec and correctness
For every rule the change relies on, fetch the actual spec text (CSS 2.1, css-break-3, css-overflow-3,
css-position-3, css-transforms, css-text, css-backgrounds, SVG, HTML) and quote the sentence. Say where Chrome
deviates from the spec and which the change follows. Check computed-value vs used-value mistakes and "may" vs
"must" readings. Confirm the change is not just matching the PR's own citations.

## 9. Docs, notes and gap hygiene
- Every sentence added to docs/** must be true as tested; docs are free-standing: no `#NNN`, no GitHub issue
  links (MDN or w3.org/whatwg.org only).
- Closing a gap means deleting its `.claude/accepted-gaps/` file AND its docs limitation note in the same PR, and
  fixing links to files other PRs deleted (`git grep` every link target in new files). Check the state of every
  issue cited.
- Every genuine remaining spec deviation needs an accepted-gaps file backed by a real OPEN issue.
- Migration note (`.claude/migration-notes/`) for any user-visible rendering change: verify its "before" claims by
  building the LAST RELEASE TAG and measuring, and date it the day it lands on main (after the tag).
- Recent-fix and invariant notes must record what was found by running; names of functions/tests they cite must
  exist in the code; an invariant should be a reusable rule with a measured symptom, not narration.
- grep `.claude/` and `docs/` for statements the change now contradicts (also `docs/architecture.md`).
- Recording a gap/fix/note should add exactly one new file and edit nothing else (no index files).

## 10. Coverage, merge check and CI
- Diff coverage: run coverage on net8.0 with `src/PeachPDF.Tests/coverlet.runsettings`, `git add -A` new files in
  your worktree so they are counted, and compute coverage of the changed lines (diff-cover, or Cobertura plus
  `git diff -U0`). Require >= 90%.
- Trial-merge current `origin/main` in a scratch worktree (`git merge --no-commit --no-ff`); confirm no
  conflicts, rebuild with 0 warnings and run the new tests plus the related area (full suite if time allows).
  Re-run the main repro on the merged tree.
- CI: if a job failed, read its log. "Test process did not return valid JSON" / test-host startup failure is
  infrastructure and safe to re-run. A failing assertion, especially on only one OS, usually means an unpinned
  font fixture or a platform difference. Never treat red or pending CI as green.

## 11. PR hygiene (report as non-blocking unless it hides a defect)
- Is the PR reviewable? More than roughly 500 changed lines or several separable behaviours should be split into
  stacked PRs, each independently correct. Growth after review that folds in unrelated fixes is a red flag.
- `Fixes #N` for every issue it closes; an issue filed for every accepted gap and every exposed pre-existing bug.
- Clear description of what was wrong, what changed, what was measured, and what is deliberately not done.
- C# conventions and current language/API idioms per CLAUDE.md's post-change review pass; comments only where
  the WHY is non-obvious.

## Report format
Verdict: MERGE-READY or BLOCKING ISSUES FOUND. Begin with the intake gates (draft, CI, author, head SHA,
mergeability). Then, for a re-review, say plainly which earlier findings are fixed with your own numbers
(baseline vs head vs Chrome, per renderer). List each finding with an exact repro (HTML, measured
coordinates/page text/token counts, fuzz seed or document name) and separate BLOCKING from NON-BLOCKING. Then a
per-section evidence summary, and list any section you skipped or could not fully run and why. Do not report a
section as done unless you actually ran it.
````

## Third-party components

PeachPDF embeds a few third-party components directly in its source tree, each under its own license — see [THIRD-PARTY-LICENSES.md](THIRD-PARTY-LICENSES.md) for the full list (currently: an embedded PdfSharpCore fork, an adapted ExCSS-derived CSS parser, and bundled hyph-utf8 hyphenation pattern data). If your change touches one of those subtrees, make sure it stays consistent with that component's original license terms.
