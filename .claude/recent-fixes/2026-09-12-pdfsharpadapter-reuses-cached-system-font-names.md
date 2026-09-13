# PdfSharpAdapter registers system font families from FontResolver's cache instead of re-parsing every file

Pure performance. No behaviour change.

## What was wrong

`PdfSharpAdapter`'s constructor looped over every discovered system font path
(`FontResolver.SupportedFonts` — 326 files on a typical Windows box) and called
`TtfFontDescription.LoadDescription(fontPath)` on each one, just to read the family name back out and
register it via `AddFontFamily`. But `FontResolver`'s own static constructor had already opened and
parsed every one of those same files once, process-wide, into `_systemFamilies` (a
`FrozenDictionary<string, FontFamilyModel>`) — this loop was redoing that exact work, file I/O and all,
on **every single `PdfSharpAdapter` construction**.

`PdfGenerator`/`HtmlContainerInt`/`PdfSharpAdapter` are constructed extremely frequently across
`PeachPDF.Tests` (several hundred call sites directly, plus `LayoutHarness.LayoutAsync`'s 962 call
sites), so this tax was paid over and over across the suite rather than once per process. It is the
same shape of bug as the fontconfig round-trip fixed in
[2026-09-10-fontconfig-is-asked-once-per-family.md](2026-09-10-fontconfig-is-asked-once-per-family.md) —
repeated, per-instance, process-invariant work — just one loop earlier in the same constructor, and not
covered by that fix.

## The fix

`FontResolver` now exposes `SystemFamilyDisplayNames`, an `IEnumerable<string>` over
`_systemFamilies.Values.Select(f => f.Name)` — the family display names already sitting in the
process-wide cache, no file I/O. `PdfSharpAdapter`'s constructor iterates that instead of
`FontResolver.SupportedFonts`, dropping the per-construction cost from "open and parse up to 326 font
files" to "iterate an already-built in-memory dictionary of a few hundred entries."

`FontFamilyModel.Name` is exactly the value `TtfFontDescription.FontFamilyInvariantCulture` produced
during `FontResolver.ParseSystemFonts`/`DeserializeFontFamily` — the same string the old loop computed,
not an approximation of it. A file that fails to parse is already filtered out once, by
`ParseSystemFonts`'s own try/catch, so it never appears in `_systemFamilies` — the old loop's per-file
try/catch is no longer needed either.

## Why this is safe

`_systemFamilies` is built once, at process start, from the exact same `TtfFontDescription.LoadDescription`
parse the old loop was repeating — reading it back out later changes nothing about which families end up
registered, only how many times the underlying files get opened and parsed to arrive at the same answer.

## Evidence

Full `PeachPDF.Tests` suite, net8.0, single-threaded
(`dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0 -- xunit.maxParallelThreads=1`),
same machine, immediately before/after (`git stash` isolating just this change):

| | before | after |
| --- | ---: | ---: |
| **Reported test duration** | 2m 33s | 1m 29s |
| **Wall clock (`dotnet test`)** | 2m 38s | 1m 35s |
| **Passed / Skipped / Total** | 11031 / 9 / 11040 | 11031 / 9 / 11040 |

**~42% reduction** in single-threaded wall-clock for the full suite, with an identical pass/skip count
both times (11031 passed, 9 skipped, 0 failed) — confirming no behaviour change.

This was found and measured, not guessed: `.claude/recent-fixes` and CLAUDE.md's `--framework net8.0`
guidance were read first for prior art in this exact area, `PdfSharpAdapter`'s constructor was read
line-by-line against `FontResolver`'s (which showed the two loops already overlapping in what they
compute), and the before/after numbers above came from actually stashing this change and re-running the
full suite twice on the same machine — not from estimating file-count × parse-cost.

- Full net8.0 suite, both before and after: 11031 passed, 9 skipped, 0 failed.
- Rebuild (`dotnet build PeachPDF.slnx -t:Rebuild`): 0 warnings, 0 errors.

## Related finding, fixed separately

A separate, unrelated regression was found in the same investigation: the MathML PR (#1019,
`d0134788`) increased per-TFM CI test time roughly 3x in the very next push after it merged (confirmed
via `gh run view <id> --json jobs` job-step timestamps, bisected to that exact commit). That was not a
consequence of the font-reparsing tax this fix addresses — see
[2026-09-12-mathml-tests-bypass-css-for-large-bundled-font-registration.md](2026-09-12-mathml-tests-bypass-css-for-large-bundled-font-registration.md)
for what it actually was and how it was fixed.
