# PeachPDF

Pure .NET HTML → PDF rendering library. No external process dependency (no Puppeteer/wkhtmltopdf/gs) — HTML parsing, CSS cascade, layout, and PDF writing all run in-process. Targets .NET 8 and .NET 10.

## Documentation map

Read these before making non-trivial changes in their area — they are the source of truth, not this file:

- [docs/index.html](docs/index.html) — doc site marketing-style landing page (hero, feature cards, guide cards)
- [docs/why-peachpdf.html](docs/why-peachpdf.html) — "Why PeachPDF?" marketing page: HTML/CSS-as-the-API pitch, free/open-source pitch, comparison table vs other .NET PDF libraries (IronPDF, PuppeteerSharp, DinkToPdf, wkhtmltopdf, Prince, QuestPDF, PDFsharp/MigraDoc, Aspose)
- [docs/showcase.html](docs/showcase.html) — feature showcase gallery; cards (thumbnail + PDF + HTML-source links) are driven by `site.data.showcases`, which pages.yml generates at site build time by running the TestHarness (`docs/showcase/` and `docs/_data/showcases.json` are gitignored build output, never source)
- [docs/getting-started.md](docs/getting-started.md) — install, quick start, thread safety / guide index
- [docs/architecture.md](docs/architecture.md) — how HTML becomes a PDF: parser, DOM, CSS, layout, painting, PDF renderer
- [docs/testing.md](docs/testing.md) — reader-facing overview of how the project is tested: the xUnit suite, the CI matrix, the 90% diff-coverage gate, and two-renderer (PDFium + MuPDF) rasterization verification (contributor-facing commands/conventions live in `CONTRIBUTING.md`)
- [docs/html-css-support.md](docs/html-css-support.md) — full HTML/CSS compatibility matrix (elements, properties, selectors, at-rules, gaps, extensions, PDF metadata extraction, tagged PDF)
- [docs/supported-svg-features.md](docs/supported-svg-features.md) — full SVG compatibility matrix (inline `<svg>` and standalone), rendered as real vector PDF content
- [docs/usage-examples.md](docs/usage-examples.md) — copy-pasteable API usage (local HTML, MHTML, HTTP fetch, thread safety, fonts, enabling tagged PDF, ASP.NET Core/Azure Functions)
- [docs/cli.md](docs/cli.md) — the standalone `peachpdf` command-line tool: install (per-platform Native AOT binaries), usage, and the full argument reference
- [docs/support.md](docs/support.md) — free (GitHub issues) and paid (Peach State Technologies) support options
- [docs/sponsorship.md](docs/sponsorship.md) — GitHub Sponsors info; sponsors get paid support under the same terms as customers
- [docs/license.md](docs/license.md) — BSD 3-Clause license text, third-party component licenses, license FAQ
- [README.md](README.md) — package overview, install, quick start, fonts

Not documentation, but read alongside it — the internal dev notes, one file per entry:
[.claude/accepted-gaps/](.claude/accepted-gaps/) (limitations already argued through; don't relitigate
one without new information) and [.claude/recent-fixes/](.claude/recent-fixes/) (the reasoning and
traps behind each recent change). See [Out of scope / accepted gaps](#out-of-scope--accepted-gaps-dont-relitigate-without-new-information)
and [Recent fixes](#recent-fixes) below.

When you add or change user-facing features, update the relevant doc page (and its `README.md`/`docs/getting-started.md` cross-links) in the same change, rather than as a follow-up — this repo's convention (established by the SVG 1.0 coverage work) is docs land with the feature.

User-facing documentation (`docs/**` and `README.md`) must be **free-standing**: it may cross-link other doc pages, [MDN](https://developer.mozilla.org), or — only where MDN has no suitable page — the upstream specification on its official URL (`w3.org`/`whatwg.org`). Do **not** cite GitHub issues or PRs in documentation (no `#123` references, no `github.com/.../issues/...` links) — an issue number is not durable, reader-facing reference material. (This applies to the docs and README only; the internal dev notes — `.claude/recent-fixes/**` and `.claude/accepted-gaps/**` — may still reference issues, since they are engineering history, not documentation. `docs/support.md` linking the issue **tracker** as a support channel is also fine — that is a support instruction, not a behavior citation.)

When you open a pull request that fixes a tracked GitHub issue, reference that issue in the PR **description** with a closing keyword (`Fixes #123` / `Closes #123`, one per issue the PR resolves) so GitHub links and auto-closes it on merge. The PR description is exactly where issue references belong — it is engineering/review context, not reader-facing documentation, so this is the complement to the docs rule above, not an exception to it.

Keep commit messages and PR descriptions free of Claude/AI attribution: do **not** add `Co-Authored-By: Claude …` or `Claude-Session:`/session-URL trailers, do not include a "Generated with Claude Code" footer or a `claude.ai/code/session_…` link in a PR body, and do not otherwise mention Claude or the assistant in a commit message or PR description. Author them as ordinary project history.

If a new or changed feature gives PeachPDF a new visible capability, add a new showcase or update an existing one in `src/PeachPDF.TestHarness/Program.cs` in the same change — this is also how several rendering-correctness bugs (paint-order, broken masks, no-op gradient spread methods) were actually caught, since automated tests alone missed them (see Testing conventions below). Every showcase must go through the `SaveShowcaseAsync(slug, category, title, description, html, config)` helper in Program.cs — that call is what registers it in `showcases.json`, which the docs site's build (pages.yml) uses to render the card for it on peachpdf.net/showcase.html; a showcase saved by hand-rolled `File.Write*` calls would silently never appear on the site.

## Project layout

- `src/PeachPDF/` — the library. Notable subtrees: `CSS/` (CSS-OM: tokenizer, parser, value converters), `Html/Core/` (DOM, cascade, layout, paint handlers), `Html/Adapters/` (`RGraphics`/`RAdapter`/etc. abstraction layer), `Svg/` (native SVG tree builder + renderer), `Fonts/` (all font handling — namespace `PeachPDF.Fonts`: OpenType table parsing under `Fonts/OpenType/`, `FontFactory`/`FontResolver`/`FontFamilyModel`/`Woff2Converter`/glyph-outline + `COLR`/`CPAL` decoders; note the OpenType readers are PDFsharp-origin, so their license headers stay intact), `PdfSharpCore/` (embedded fork of PDFsharp — see below, has its own [LICENSE](src/PeachPDF/PdfSharpCore/LICENSE.md)).
- `src/PeachPDF.Tests/` — xUnit test suite, multi-targets net8.0/net10.0.
- `src/PeachPDF.Cli/` — the `peachpdf` command-line tool (net10.0-only, NativeAOT-published; conventional command-line argument grammar). Also serves as the library's AOT smoke test (a successful `dotnet publish -p:PublishAot=true` proves the whole pipeline AOT-compiles and runs) — replacing the removed `PeachPDF.AotSmokeTest`. The published native binary's assembly name stays `PeachPDF.Cli` (naming it `peachpdf` collides case-insensitively with the `PeachPDF` library and breaks project-reference restore); the release workflow renames it to `peachpdf` when packaging.
- `src/PeachPDF.Cli.Tests/` — xUnit v3 test suite for the CLI (net10.0-only, to reference the net10-only CLI). Library-behavior changes the CLI depends on are tested in `PeachPDF.Tests` instead (they exercise `PeachPDF` internals); `.github/workflows/test.yml` runs both projects' coverage into one directory so the diff-coverage gate covers the CLI too.
- `src/PeachPDF.TestHarness/` — a runnable showcase app for visually exercising features (`Program.cs`).
- `src/PeachPDF.Demo.BlazorWasm/` — the in-browser demo (net10.0-only, Blazor WebAssembly), published to `peachpdf.net/demo/` by `pages.yml`. It is also the library's **WebAssembly smoke test**: a browser has no system fonts, no file system and no Brotli decoder, so it exercises exactly the constraints a server host hides. See its `README.md` for the WOFF-not-WOFF2 and `hyphens: auto` limitations that fall out of the missing Brotli decoder.
- `assets/fonts/` — **every** font asset the repo bundles, shared by Tests, TestHarness and the demo (each links what it needs into its own output; the demo stages its copies into `wwwroot/fonts/`, which is gitignored build output). Licences sit beside the fonts as `<Name>.LICENSE.txt`; the generator/conversion scripts live here too. Nothing here ships in the library or its NuGet package.
- Working directory for `dotnet build`/`dotnet test` is `src/` — if a shell session resets to repo root, commands silently fail with "Project file does not exist"; always confirm you're in `src/` first.
- If the .NET SDK is missing (e.g. a fresh sandboxed/web session), install it with `apt` — `sudo apt-get install -y dotnet-sdk-10.0` (add `aspnetcore-runtime-8.0` so the net8.0 target framework can run). The `dot.net/v1/dotnet-install.sh` route is blocked in this environment (the Microsoft CDN host returns 403 through the egress proxy), so apt is the working path. `global.json` pins SDK 10.0.100 (rollForward `latestFeature`), which the apt `dotnet-sdk-10.0` (10.0.1xx) satisfies.

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

### Build warnings

**Before opening a pull request, the whole solution must build with zero warnings.** Not just the projects you touched — a warning anywhere is a warning the next change has to read past, and the ones that accumulate here (`CS1573` missing `<param>` tags, `CS8602` possible null dereference) are exactly the ones that make a real defect invisible in the noise.

```
dotnet build PeachPDF.slnx -t:Rebuild
```

Rebuild rather than a plain `build`: an incremental build skips up-to-date projects and reports their warnings not at all, so a clean-looking `dotnet build` can hide every warning in the solution. Fix what you find, including warnings you did not introduce — the doc comment or null annotation is a two-line fix, and leaving it is what got it there.

Avoid writing tests against `PeachPDF.Fonts.FontFactory` (and OpenType neighbors) without care — it caches resolved fonts in `static readonly Dictionary` fields shared process-wide, and xUnit's parallel test-class execution makes new tests here a real order-dependent-flakiness risk against the rest of the suite.

## Post-change review pass

After a non-trivial code change is otherwise done (implementation + tests passing + coverage checked), spin up a review agent against the diff (uncommitted changes, or the changes in the current branch/PR) covering:

1. **C# coding conventions** — [Microsoft's C# coding conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions).
2. **Current C# language version** — this repo doesn't pin `LangVersion` (see `src/Directory.Build.props`), so each TFM already floats to its own SDK-default latest (net8.0 → C# 12, net10.0 → C# 14, etc.) — flag code that reads like an older-version holdover (e.g. missing collection expressions, primary constructors, pattern-matching improvements) where a newer construct would be a genuine clarity/correctness win, not a style-only rewrite.
3. **Current .NET APIs for the target frameworks** — prefer the modern API over an older equivalent still in the codebase out of habit. Since this repo multi-targets `net8.0;net10.0`, if a meaningfully-better API only exists on the newer TFM, gate it with `#if NET10_0_OR_GREATER` (falling back to the net8.0-compatible approach in `#else`) rather than skipping it — don't downgrade the whole codebase to the lowest common denominator when the newer target can do better.
4. **Current HTML/CSS/SVG spec compliance** — check changes touching parsing/cascade/layout/paint against the actual current WHATWG/W3C spec text for the relevant module(s) (HTML parsing/living standard, the specific CSS module(s) involved — Box, Text, Position, Paged Media, Fragmentation, etc. — and/or the relevant SVG module), not against assumptions or this repo's existing (possibly incomplete) behavior. Cross-check against [docs/html-css-support.md](docs/html-css-support.md) / [docs/supported-svg-features.md](docs/supported-svg-features.md) for known, already-accepted gaps before flagging something as a new defect.

Report findings the same way `/code-review` does; fix what's actually wrong, and record any newly-confirmed accepted gap in the relevant doc rather than leaving it as a silent finding.

**When a change deliberately leaves a CSS/SVG/HTML spec violation out of scope, file a GitHub issue for it** — one that states what the gap is, the specific spec rule it violates, and why it was out of scope — and reference that issue number in the accepted-gap note. An accepted gap that is a genuine spec deviation must be *tracked as an issue*, not only recorded as prose (mirroring the existing `#151`/`#152`/`#166` references in `.claude/accepted-gaps/`). This complements the docs rule above: the accepted-gap file carries the `#NNN` reference, while the reader-facing `docs/**` note describes the same limitation without citing the issue.

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
- The internal layout unit is 1 PDF point, and CSS `px` resolves spec-correctly at **1px = 1/96in = 0.75pt** everywhere (`Length.PointsPerPx`, the only place the ratio lives — issue #150 removed the old "1px = 1pt except fonts" dual convention). A `width: 96px` box lays out 72 units wide; image/SVG intrinsic pixels are ×0.75 too. When writing layout tests where the unit isn't the point of the test, prefer `pt` in fixtures so expected values read literally.

## Architecture conventions

- **The fragment tree is the layout↔paint contract.** Layout **emits** it: `HtmlContainerInt.LayoutDocument` hands each fragmentainer pass's slots to `FragmentEmitter` as that pass ends, and `FragmentEmitter.Finish` (at the end of `HtmlContainerInt.PerformLayout`) materializes an immutable `FragmentTree` of per-page `FragmentainerFragment`s; **paint consumes only that** and must never read geometry off `CssBox`. New paint code takes a `BoxFragment` and reads its `Lines`/`Words`/`Children`/`WholeBoxRect`; the box is for style and handler dispatch only. Fragment rectangles are already fragmentainer-local (`local.Y = documentY - (PageTopOf(k) - MarginTop)`), so there is no per-page offset to apply — if you find yourself needing one, you're reading the wrong thing. `BoxFragment.OriginY` exists for the handful of coordinates layout still records in document space (`ColumnRuleSegments`, `PageBreakBottoms`) and for mapping an ancestor's `overflow` clip into the fragment's space. Two box kinds get explicit structure in the emitter because they aren't reachable via `CssBox.Boxes`: a `CssProxyBox`'s repeated header/footer source subtree (descended through the proxy's own `BoxGeometrySnapshot`) and a `CssSpacingBox`'s spanned cell. Two rules to know before touching emission: **only what landed in one slot is a per-pass fact** (which per-line rectangles, which words) — anything defined over the *whole* box (its own bounds, §6.2's unbroken strip, the overflow clip, `WholeBoxRect`) is resolved at materialization, because a box that continues into a later fragmentainer has not had its height applied on the pass that freezes this slot; and **a frozen slot can be re-opened** (`FragmentEmitter.InvalidateFor`), because a box only reaches its epilogue on the pass that completes it, so §4.3's movers can relocate content out of a fragmentainer already emitted. Any new geometry-mutation path that can reach back across a pass has to notify. See [docs/architecture.md §6](docs/architecture.md).
- **Paint lives in `Html/Core/Paint/`, never on `CssBox`.** `FragmentPainter` (partials: main walk, `.Decorations`, `.Text`) is the paint phase; `StackingOrder` owns CSS 2.1 Appendix E ordering (the two predicates layout also needs, `NeedsStackingHoist`/`IsStackingContextBox`, stay in `DomUtils`). A painter instance paints exactly one page, so per-page paint state is its own — don't put it back on the container or the box tree. **Paint is synchronous**: everything drawn was resolved during layout, so a painter must never fetch or decode anything. A box whose content the generic paint can't express gets an `IFragmentContentPainter` (`Html/Core/Paint/Content/`) selected by type in `FragmentContentPainters.For` — adding a replaced element means adding a content painter and one arm there, and exposing on the box only the already-resolved content that painter reads. The four replaced kinds that share a shape (`<img>`, `<object>`/`<video>`, inline `<svg>`, `<iframe>`) derive from `ReplacedFragmentPainter`; add to it rather than repeating the clip/background/border/content-rect sequence.
- **Don't write two independent parsers for the same CSS value grammar across layers.** If both the CSS-OM/parsing layer and a later render/resolution layer need to understand a value's grammar (tokenization + classification), extract it into one shared internal class both call — only the final numeric resolution that genuinely depends on runtime-only information (percentage-against-box-size, `calc()` evaluation) should differ between layers. Precedent: `CalcParser` (shared across `PeachPDF.CSS` ⇄ `PeachPDF.Html.Core`), `BackgroundPositionGrammar`/`BackgroundSizeGrammar` (shared between the CSS-OM converters and `BackgroundLayerResolver`).
- The `Html/Adapters` layer (`RGraphics`/`RAdapter`/`RPen`/etc.) is the abstraction boundary between layout/paint logic and the concrete PDF backend (`PdfSharpCore`). New rendering primitives (tiling, soft masks, dash patterns, etc.) get added here first, then implemented in `GraphicsAdapter`/`XGraphics`/`XGraphicsPdfRenderer`. Check whether an abstract `RGraphics` member you're adding needs updates to the test-only mock implementations (`SpyGraphics` in `TransformIntegrationTests.cs`, `RecordingGraphics` in `CssLayoutEngineTablePageBreakTests.cs`).
- Before building new PDF-writing infrastructure (patterns, soft masks, shadings), check whether `PdfSharpCore` already has an unused primitive for it (`XForm`/`PdfFormXObject`, `PdfTilingPattern`, `PdfSoftMask` have all been found pre-existing-but-uncalled at various points) rather than assuming it needs to be built from scratch.
- A `/Luminosity` soft mask's `/G` form AND the content form it masks both need their own `/Group << /S /Transparency /CS ... /I true >>` transparency-group dictionary for spec-conformant readers to actually apply the mask — `XForm`/`PdfFormXObject` don't set this automatically.

## Out of scope / accepted gaps (don't relitigate without new information)

Each gap lives in its own file under [.claude/accepted-gaps/](.claude/accepted-gaps/), named for the
gap; [.claude/accepted-gaps/README.md](.claude/accepted-gaps/README.md) indexes them by title.
**Read the file covering an area before treating behaviour there as a defect** — each one records a
limitation already argued through once, often with an approach that was tried, measured and rejected,
so "fixing" it without new information re-runs work that has already been done and lost.

Unlike the recent fixes below, **these do not expire.** A gap file is deleted only when the gap is
actually closed, and closing one means deleting the file *and* removing the matching limitation note
from the user-facing page in `docs/**`. Add a file when a change deliberately leaves a gap behind;
if the gap is a genuine spec deviation, file the tracking issue described under "Post-change review
pass" above and reference it from the file.

## Recent fixes

Each fix lives in its own file under [.claude/recent-fixes/](.claude/recent-fixes/), named
`YYYY-MM-DD-<slug>.md` for the date it landed on `main`;
[.claude/recent-fixes/README.md](.claude/recent-fixes/README.md) indexes them newest-first by title,
so you can find the one that covers an area from the index alone rather than by reading the folder.
**Read the entries touching an area before making a non-trivial change there** — they carry the
reasoning, the alternatives already measured and rejected, and the traps that cost real debugging
time, which is exactly the context that keeps a change from re-deriving or re-breaking something.

When you land a non-trivial change, add one file rather than appending here. Say what the
load-bearing idea was, what was found by running it rather than by reading it, what was deliberately
not done and why, and what evidence the conclusion rests on (suite/showcase/diff-coverage results).
A defect a future change could plausibly reintroduce is worth more words than the diff itself.

**A fix is not recent once it is more than 30 days old — delete the file.** By then anything a
reader still needs should already live somewhere durable: user-facing behaviour in `docs/**` /
`README.md`, and a deviation we have decided to live with in
[.claude/accepted-gaps/](.claude/accepted-gaps/), which does not expire (with its tracking issue). If some of it does not, migrating it *is part of deleting the file*, not
a follow-up — the deletion is the last moment that knowledge is guaranteed to be written down
anywhere. A stale entry is worse than no entry: it describes code that has since moved on, and
every future change pays to read past it.

## Thread safety

A `PdfGenerator` instance is not thread-safe — never call it concurrently or reuse one instance across overlapping renders. Use a separate instance per thread/request/batch item; see [Thread safety](docs/usage-examples.md#thread-safety) in usage-examples.md.
