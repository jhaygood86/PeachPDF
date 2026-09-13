# Font checksum was recomputed on every lookup, cache hits included

## What was wrong

`XFontSource.GetOrCreateFrom(byte[] bytes)` (`src/PeachPDF/PdfSharpCore/Drawing/XFontSource.cs`) called
`FontHelper.CalcChecksum(bytes)` — an O(n) Adler32-style scan of the *entire* font byte buffer —
**unconditionally, before checking whether the font source was already cached**. `FontFactory`'s
`FontSourcesByKey` (a `static readonly Dictionary<ulong, XFontSource>`) is correctly process-wide, so a
repeat request for the same font should be a cheap cache hit — except computing the key needed to even
*check* that cache required paying the full scan every single time, cache hit or not.

Worse, for a system font reached via per-codepoint/glyph-coverage fallback (CJK, emoji, symbols — the
content 97 of 661 test files touch), `FontResolver.GetFont` (`src/PeachPDF/Fonts/FontResolver.cs`) called
`File.ReadAllBytes(fontPath)` fresh on every call — a brand-new `byte[]` instance, re-read from disk,
every time, for files that can be several megabytes (e.g. Segoe UI Emoji on Windows).

`PdfSharpAdapter`/`HtmlContainerInt`/`FontResolver` instances are constructed fresh per test by design
(hundreds of direct call sites, ~962 via `LayoutHarness.LayoutAsync` — see
[2026-09-12-pdfsharpadapter-reuses-cached-system-font-names.md](2026-09-12-pdfsharpadapter-reuses-cached-system-font-names.md)),
so every fresh `FontResolver`'s empty per-instance caches meant the *first* character in each test
needing a given fallback/custom face re-read and re-hashed that font from scratch — even though the exact
same bytes had already been hashed, and even already cached in `FontFactory.FontSourcesByKey`, by an
earlier test moments before.

## Why it went unnoticed

Without code coverage, this is invisible: the checksum loop's inner body is 1-2 ALU operations that JIT
to a handful of fast instructions, so even thousands of full-buffer re-hashes cost a few seconds of real
wall-clock — never showed up in the wall-clock/allocation investigation behind the three fixes already
landed in this PR ([2026-09-12-pdfsharpadapter-reuses-cached-system-font-names.md](2026-09-12-pdfsharpadapter-reuses-cached-system-font-names.md),
[2026-09-12-mathml-tests-bypass-css-for-large-bundled-font-registration.md](2026-09-12-mathml-tests-bypass-css-for-large-bundled-font-registration.md),
[2026-09-12-bidi-resolver-skips-display-none-subtrees.md](2026-09-12-bidi-resolver-skips-display-none-subtrees.md)).
Those fixes cut the local single-threaded no-coverage suite from 2m33s to 43s, but CI (which collects
code coverage via coverlet on every TFM) only improved ~9% on the same PR (Windows `Test (net8.0)`:
5m2s → 4m36s) — a huge, unexplained gap between local and CI improvement.

Found by analyzing the actual Cobertura coverage data coverlet already produces: `FontHelper.CalcChecksum`
was the single hottest line in the entire 11,000+-test suite — ~8x hotter than the CSS lexer's combined
per-character work across every test, ~65x hotter than the next-heaviest legitimate per-character
text-shaping work (bidi/script tables). Coverlet's dynamic instrumentation inserts a hit-counter write
per sequence point and defeats inlining/vectorization; for a loop whose real per-iteration cost is 1-2
ALU ops, that overhead is a large *relative* multiplier, paid billions of times — turning an invisible
few seconds into the dominant contributor to CI wall-clock.

## The fix

Two changes, both needed together, neither touching the deliberate per-instance-vs-process-wide
correctness split for custom fonts (`InstanceGlyphTypefacesByKey`,
`InstanceFontResolverInfosByTypefaceKey`, `_CustomFonts` are untouched):

1. **`FontResolver.GetFont`** now caches a system font's bytes by path in a
   `static readonly ConcurrentDictionary<string, byte[]>`, via `GetOrAdd(fontPath, File.ReadAllBytes)`.
   System font paths are already `static readonly` (stable for the process's lifetime, same rationale as
   `_systemFamilies`), so this is safe and makes `GetFont` return the *same* `byte[]` instance for a given
   path across every call, every resolver instance, for the process's lifetime — no more repeat disk reads.
2. **`XFontSource.GetOrCreateFrom`** now memoizes the checksum by buffer *reference* in a
   `static readonly ConditionalWeakTable<byte[], object>` (the same established pattern already used for
   process-wide, GC-safe caches in `src/PeachPDF/Text/GposPositioner.cs`/`GsubShaper.cs`) before calling
   `FontHelper.CalcChecksum`. Combined with fix 1 (a stable `byte[]` reference for system fonts), the
   expensive scan now runs once per *distinct font file* for the whole test run, not once per test.

## Evidence

Measured locally (same machine, Release config, `dotnet test ... --collect:"XPlat Code Coverage"
--settings coverlet.runsettings`, isolating just this fix via `git stash`):

| | before | after |
| --- | ---: | ---: |
| **Coverage-collecting full-suite duration** | 4m 10s | 1m 0s |
| **Wall clock (`dotnet test`)** | 4m 23s | 1m 15s |
| **`FontHelper` cumulative line hits (from the Cobertura report itself)** | 12,135,944,228 | 2,577,831,324 |
| **Passed / Skipped / Total** | 11025 / 9 / 11034 | 11025 / 9 / 11034 |

**~76% reduction** in coverage-collecting wall-clock, **~79% reduction** in the exact hit count that
identified the bug in the first place — direct confirmation this is the mechanism, not a coincidental
improvement from something else. The remaining ~2.58 billion hits are legitimate: every genuinely
distinct font used across the suite still needs its checksum computed once (a real first-time cost, not
a redundant one), plus custom/`@font-face`-registered fonts (which allocate a fresh `byte[]` per
`AddFont` call, so the reference-identity memo naturally can't collapse those across different
registrations of conceptually-the-same bytes — a smaller, separate, and much lower-value target than the
system-font path this fix targets).

Pass/skip/total counts are identical before and after, confirming this is a pure performance fix with no
change to which font or face any test resolves to.
