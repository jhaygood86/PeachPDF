# fontconfig is asked once per generic family, not once per document

Pure performance. No behaviour change.

## What was wrong

`LinuxSystemFontResolver.ResolveGenericFamily` did a full fontconfig round trip on every call —
`FcPatternCreate`, `FcPatternAddString`, `FcConfigSubstitute`, `FcDefaultSubstitute`, `FcFontMatch`,
then marshalling the family name back out of native memory.

It was called far more often than it looks. `PdfGenerator` holds its `PdfSharpAdapter` as an
**instance** field, and that adapter's constructor resolves every generic family
(`serif`/`sans-serif`/`monospace`/`cursive`/`fantasy`) plus `system-ui`. Callers construct a
`PdfGenerator` per document, so rendering N documents made **7N identical native round trips**,
every one of them returning the same answer.

A sampled CPU profile put 2.67% of all non-waiting engine work in `FcConfigSubstitute` alone, with
`FcFontMatch` on top of that, all under this one method.

## Why caching is safe

The answer cannot change while the process runs. `fcConfig` is a `Lazy<IntPtr>` over
`FcInitLoadConfigAndFonts`, so fontconfig's configuration is loaded exactly once and every later
query is asked of that same immutable config. A font installed mid-process would not be seen
without a reload either way, so the cache changes no result — only how often the round trip is paid.

The delegate handed to `GetOrAdd` is cached in a static field rather than written inline. A method
group converted at the call site allocates a fresh delegate every call, which would have put back a
per-call allocation while removing a per-call native round trip — the same trap #977 fixed for the
GSUB/GPOS lookup caches.

## Evidence

Sampled thread-time profiles, 26-document corpus, 15 warm-up sweeps past the plateau, 4 cores,
60-second window, waiting threads excluded:

| frame | before | after |
| --- | ---: | ---: |
| **`FcConfigSubstitute`** | **2.67%** | **0.00%** |
| `List<CSS.Token>..ctor(IEnumerable)` | 13.20% | 13.77% |
| `Enumerable.Select().ToArray()` | 5.23% | 5.32% |
| `GraphicsAdapter.DrawString` | 4.65% | 4.65% |

Total work samples in the same window fell 140,082 to 136,329 — this is the first of these changes
where the sampler saw measurably *less* work, rather than the same work redistributed.

**Throughput, sustained rather than burst:** 45-second holds at concurrency 4 after a 15-sweep
warm-up, interleaved, three reps each:

| | docs/s |
| --- | --- |
| before | 92.97, 93.01, 92.30 |
| after | **95.77, 95.62, 95.18** |

**+2.85%**, winning every rep, with under 1% spread on each side. Worth recording that the
instrument matters as much as the change: the burst probe used earlier in this work has a 10-15%
run-to-run spread and could not have resolved this at all. A warmed, sustained hold can.

`ResolveGenericFamily_OnLinux_AsksFontconfigOncePerFamily` asserts by **reference**, not equality:
every uncached resolution marshals a fresh string out of native memory, so two uncached calls can be
equal but never the same instance. `Assert.Equal` would pass against unfixed code. Mutation-verified
— bypassing the cache fails it.

- Full net8.0 suite: 10,582 passed, 9 skipped, 0 failed.
- 26-document corpus: 26 pass / 0 review / 0 fail, byte-identical after normalisation.
- Rebuild: 0 warnings, 0 errors.
