# A ligature lookup allocates nothing per glyph position

Pure performance. No output byte changes.

## What was wrong

`GsubShaper.ApplyLigatureLookup` tries a match at every position in the glyph run, and every attempt
allocated before it had looked at anything:

- `TryMatchLigature` set `skippedOffsets = []` as its first statement, ahead of the coverage test
  that decides whether there is any work at all. Most positions match nothing, so most of those
  lists were built and dropped without ever being written to.
- Each candidate ligature allocated its own `matched` and `skipped` lists, again before knowing
  whether the candidate would match.
- `lookup.Subtables` is typed `IReadOnlyList<T>`, so the `foreach` over it boxed `List<T>`'s struct
  enumerator once per position. The same defect, in the same shape, as the GPOS subtable walks fixed
  in #968 - GSUB was simply not looked at then.

Five sibling subtable walks in the same file (single, multiple, alternate, reverse-chain, sequence
context) had the identical boxing and are converted the same way.

## Why it started mattering now

None of this is new, but #984 made it hot. `ccmp` and `locl` are Type 4 (ligature) lookups in real
fonts, and that change enables them for every run rather than only for Arabic and USE text. Ordinary
Latin text that previously ran no ligature lookup at all now runs one over every glyph position, so a
per-position allocation that used to be paid by a minority of runs is now paid by all of them.

## The load-bearing idea

`skippedOffsets` is `null` on the failure path rather than an empty list, declared
`[NotNullWhen(true)]` so the compiler knows the caller may only read it after a `true`. The two
match lists become a `LigatureMatchScratch` owned by `ApplyLigatureLookup` and cleared per candidate
rather than reallocated - they are created lazily, on the first candidate actually tried, so a run
whose glyphs no subtable covers still allocates nothing.

Clearing per candidate, not per position, is what makes reuse safe: a candidate that fails partway
leaves entries behind that the next candidate must not see, and `skipped` is handed out to
`ApplyLigatureAt` on a match and read there before the next call, so nothing ever observes a stale
buffer. `TwoLigaturesInOneRun_DoNotCarryTheFirstMatchsSkippedGlyph` is the test that holds this: two
ligatures form in one run, the first over a skipped mark, and an uncleared list re-inserts that mark
after the second ligature as well.

`ApplyLigatureAt` also skips building its `skippedGlyphs` list when nothing was skipped, which is the
ordinary case even on a match.

## Evidence

Measured on a 26-document corpus, medians of 3 interleaved reps, Release, net8.0, server GC,
`FONTCONFIG_FILE` pinned so font resolution cannot drift between runs:

| | allocated | vs `main` |
| --- | ---: | ---: |
| `main` | 2,638.5 MB | - |
| this change | 2,444.4 MB | **-194.1 MB, -7.4%** |

Run-to-run spread was under 0.1% on each side. For scale, that is more than the whole of #984's own
cost (+181 MB): the correctness fix now runs at a net saving rather than a net cost, because the
per-position allocation it exposed had been there all along on the `liga`/`clig` path.

Both new tests are mutation-verified - each of the four parts of the change was reverted in turn and
the suite failed each time:

| reverted | result |
| --- | --- |
| eager `skippedOffsets = []` | allocation test fails |
| `foreach` over `Subtables` | allocation test fails |
| `skipped.Clear()` | reuse test fails |
| `matched.Clear()` | reuse test fails |

A third test, `ALigatureInvokedFromAContextualRule_StillForms`, drives the other route into
`ApplyLigatureAt` - a Type 5 contextual rule invoking a ligature lookup as a nested lookup at one
position, which owns its own scratch rather than the run walk's.

- Full net8.0 suite: 10,534 passed, 9 skipped, 0 failed - including #984's own
  `DefaultIgnorableShapingTests` and `EmojiSequenceCompositionTests`, so emoji sequence composition
  and default-ignorable hiding are unchanged.
- Rendered PDFs are byte-identical to `main` across all 26 corpus documents after normalising
  `/CreationDate`, `/ID` and the random font subset tags.
- Diff coverage on the changed source: 100% (28 of 28 coverable added lines).
- Rebuild is 0 warnings, 0 errors.
