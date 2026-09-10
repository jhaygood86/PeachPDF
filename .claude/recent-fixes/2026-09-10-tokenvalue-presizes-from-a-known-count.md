# TokenValue presizes from a count it already has

Pure performance. No behaviour change.

## What was wrong

`TokenValue`'s only sequence constructor took `IEnumerable<Token>` and did
`new List<Token>(tokens)`. That overload presizes **only** when the source is an
`ICollection<T>`. `IReadOnlyList<T>` is not one — and neither is `TokenValue` itself, which
implements `IReadOnlyList<Token>` and stops there — so it enumerated the source and grew the
backing array 4, 8, 16, 32…, reallocating and copying at every step, for a sequence whose length
was known before the first element was read. It also allocated an interface enumerator to do it.

Converters build one of these per declared value (`Original = new TokenValue(tokens)` appears at a
dozen sites), and every one of them has `IReadOnlyList<Token>` as its static parameter type. So the
slow path was not an edge case, it was the only path.

## The change

A second constructor taking `IReadOnlyList<Token>`, which presizes from `Count` and copies by
index. Overload resolution picks it at every one of those call sites without touching them.

## Evidence

Sampled thread-time profiles of the engine rendering a 26-document corpus — 15 warm-up sweeps past
the plateau, 4 cores, 60-second window, waiting threads excluded:

| frame | before | after |
| --- | ---: | ---: |
| **`List<CSS.Token>..ctor(IEnumerable)`** | **7.29%** | **1.67%** |
| `Enumerable.Select().ToArray()` | 5.12% | 5.08% |
| `FcConfigSubstitute` | 2.67% | 2.65% |
| `GraphicsAdapter.DrawString` | 4.39% | 4.40% |

Roughly 5.6 percentage points of engine CPU, and 77% of that frame's own cost. Everything else is
unchanged; frames whose share rose did so because the same work is a larger fraction of a smaller
total.

**Allocation barely moves** — 2,260 MB to 2,254 MB over the corpus, −0.3%. That is the point worth
recording: the win here is enumeration and copying, not bytes. A token list is short, so growing it
wastes little memory and a lot of time. An allocation profile would have ranked this near the
bottom; it was the largest frame in a CPU profile.

`BuildingFromAKnownLengthList_GrowsItsArrayOnlyOnce` is mutation-verified: with the overload
removed the same 64-token build allocates 5,256 bytes against a 2,560-byte one-array floor, and the
test fails naming that number.

The test deliberately wraps its source so it exposes `IReadOnlyList<Token>` and nothing else. Handing
it the `List<Token>` directly would let the general overload presize from `ICollection<T>` and the
test would pass against unfixed code — the same trap that cost a first attempt at the GPOS
enumerator test.

- Full net8.0 suite: 10,576 passed, 9 skipped, 0 failed.
- Corpus of 26 real documents renders byte-identically after normalisation.
- Rebuild: 0 warnings, 0 errors.
