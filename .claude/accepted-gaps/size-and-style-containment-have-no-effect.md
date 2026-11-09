# `contain: size` and `contain: style` parse but do nothing

`contain` is parsed in full (`ContainGrammar.TryParseContain`), and `strict` / `content` expand to their keyword sets, but only layout and paint containment do anything (`DomUtils.AppliedContainment` returns the set, and the layout/paint readers look only at those two bits).

- **Size containment (CSS Containment 2 §3.1)** should size the box as if it had no contents: intrinsic sizes as if empty (no children, no `::before`/`::after`/`::marker`), a replaced element with natural width and height 0 and no natural ratio, then the contents laid out into that size and treated as monolithic when fragmenting. An auto-sized `contain: strict` box here is as large as its content, so a float inside it still grows it and `strict` is not a way to collapse a box to nothing.
- **Style containment (§3.3)** should scope `counter-increment`/`counter-set` and the quote keywords to the subtree. Counters and quotes are not scoped.
- The `inline-size` keyword of later levels of the spec is not recognised, so a declaration using it is invalid.
- `overflow-clip-margin` is not supported, so paint containment clips at the padding edge itself, which is its initial value.

Why left: each needs its own work (intrinsic sizing as if empty; counter-scope bookkeeping) that nothing in the containment change needed. Tracking issue: [#2308](https://github.com/jhaygood86/PeachPDF/issues/2308).
