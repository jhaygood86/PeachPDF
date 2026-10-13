# Cascade allocated a LINQ chain per box and re-tokenized every font-family

A fresh allocation profile of the 98,000-tag MathML benchmark (1.47 GB per render, 174,000 boxes) put 34% of allocation in
`CascadeApplyStyles`. Two pieces were pure waste:

- `CssData.GetAuthorStyleRulesForCascade` sorted each box's matched rules with `OrderBy/ThenBy/ThenBy/Select/ToList` - about a
  dozen iterator, delegate and key-array objects per box (~230 MB per render). Now a keyed insertion sort over a thread-static
  buffer (`SortForCascade`). The key (layer rank, specificity, document order) is total, so the order is the one the stable
  LINQ sort gave; `!important` still reverses the layer rank only.
- Every cssom-grammar validator (`font-family`, `content`, `transition`, ...) tokenized and converted the declaration text for
  every box that carried it. `GrammarMemo` remembers the answer per (grammar delegate, value): acceptance is a pure function
  of the string. Bounded (8,192 entries, values over 512 chars not kept), shared across documents and threads.

Also: the two filter lists a `DerivedStyle` allocated eagerly (348,000 empty lists live) are one shared empty list now.

**Traps.** The memo is keyed on the generated `static` lambda, one delegate per call site - a capturing lambda in the emitter
would give a new key every call and silently cache nothing. A unit test that fills a process-wide cache races with every other
test through the cache; the memo is an instance class with a shared default so tests use their own.

**Measured** (warm in-process): math-l 1,469 -> 1,151 MB per render, table-m 848 -> 717 MB; wall time within noise because the
GC pause (~1 s of 2.5 s on math-l) tracks the 600 MB live heap (CssBox 1.2 KB + DerivedStyle 0.9 KB per box = 53% of it), not
allocation volume. `DOTNET_GCgen0MaxBudget=512MB` alone took math-l from 2.59 s to 2.2 s (pause 956 -> 609 ms) - a runtime
setting, not something the library can ship.

**Evidence.** Release suite green; PyMuPDF words+drawings identical to main on 19 benchmark documents; 80-document fuzz identical.
