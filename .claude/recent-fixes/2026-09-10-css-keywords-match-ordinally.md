# CSS keywords match ordinally, not through the invariant culture

Pure performance. No behaviour change — see "What this is not", which is the important half.

## What was wrong

`css-properties.json` marked 33 properties `"keywordComparison": "invariant-ignore-case"`, and
`RegistryEmitter` turned that into `StringComparison.InvariantCultureIgnoreCase` in both the
generated validator and the generated canonicaliser. Invariant comparison is *linguistic*: it goes
through `CompareInfo.Compare`, which is roughly an order of magnitude more expensive than an
ordinal scan.

A sampled CPU profile of the engine rendering a 26-document corpus, warmed 15 sweeps past the
plateau and pinned to 4 cores, put **6.28% of all non-waiting work in `CompareInfo.Compare`, and
89.9% of that under one generated setter** (`Set_OutlineColor`). Nothing else in the top of that
profile was a string comparison at all.

## Why ordinal is right

css-values-4 §4.1 "Pre-defined Keywords" defines keyword matching as **ASCII
case-insensitive**: "Keywords are identifiers and are interpreted ASCII case-insensitively
(i.e., [a-z] and [A-Z] are equivalent)." Every keyword in `supportedValues` is ASCII —
checked, all 215 properties, none has a non-ASCII declared value. So
the invariant culture bought nothing the spec asks for, and `OrdinalIgnoreCase` is both correct
and cheap.

`invariant-ignore-case` is still accepted in the JSON and now maps to ordinal, rather than being
rejected. Rejecting it would fall through to the `Ordinal` default, which is *not* case-insensitive
at all — turning `INVERT` from a match into a non-match. That would be a far worse regression than
the culture-awareness being removed.

## What this is not

**It is not a correctness fix, and the first draft of this note wrongly said it was.**

The two comparisons genuinely differ in isolation: linguistic comparison gives format characters
zero weight, so `"in­vert".Equals("invert", InvariantCultureIgnoreCase)` is `true` where the
ordinal form is `false`. It is tempting to conclude the engine used to canonicalise
`outline-color: in­vert` into `invert`.

It did not. Driven end to end, such a declaration is rejected *before* the generated setter's
keyword comparison runs, so the property falls back to its initial value either way. That was
established the only way it could be — by reverting the emitter to `InvariantCultureIgnoreCase`,
rebuilding from clean, and getting **identical** results for a soft hyphen, a zero-width space, a
leading ZWSP and a trailing ZWJ, across both a `<color> | keyword` property and a keyword-only one.

So the culture-aware comparison was unreachable cost: it changed no output and consumed 6% of the
engine's CPU. `OutlineColorKeyword_MatchesAsciiCaseInsensitivelyAndNotLinguistically` is kept as a
guard on the contract and is documented as passing either way, so nobody later mistakes it for
evidence of a behaviour change.

## Evidence

Sampled thread-time profiles before and after, same corpus, same 15-sweep warm-up, same 60-second
window, 4 cores, waiting threads excluded:

| frame | before | after |
| --- | ---: | ---: |
| `CompareInfo.Compare` | **6.28%** | **0.00%** |
| `List<CSS.Token>..ctor(IEnumerable)` | 7.32% | 13.41% |
| `Enumerable.Select().ToArray()` | 4.99% | 5.18% |
| `FcConfigSubstitute` | 2.69% | 2.65% |
| `GraphicsAdapter.DrawString` | 4.43% | 4.50% |

The comparison is gone outright. Every other frame is unchanged; the ones whose percentages rose
did so because the same work is now a larger share of a smaller total, not because they got slower.

End-to-end throughput is **not** quoted here. The change is worth ~6% of CPU in a pipeline that is
not purely CPU-bound, and a bare-process run-to-run spread of 10-15% cannot resolve that from four
reps. The profile measures the thing that changed; wall clock would only measure the noise.

- Full net8.0 suite: 10,555 passed, 9 skipped, 0 failed.
- Source-generator suite: 119 passed, 0 failed.
- Mutation-verified: reverting either emit site fails three golden-file tests
  (`..._For_OrdinalIgnoreCase`, `..._For_The_Legacy_InvariantIgnoreCase_Spelling`,
  `..._For_A_Union_Length_Or_Keyword_Property`). The engine-level guard deliberately does not fail,
  for the reason given above.
- Rebuild: 0 warnings, 0 errors.
