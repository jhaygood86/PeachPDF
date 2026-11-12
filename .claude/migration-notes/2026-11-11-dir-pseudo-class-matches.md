# `:dir()` matches by directionality

Before (v0.9.23 included): `:dir(ltr)` and `:dir(rtl)` parsed but never matched, so a rule using them was silently dead. Now
they match an element by its own `dir` attribute, otherwise its parent's, and left-to-right at the root (`dir="auto"` and `<bdi>`
are resolved from their first strong character first), as Selectors 4 §8.1 defines. A stylesheet that carried a `:dir()` rule
for a browser will now apply it in the PDF too.

Related, the same release: `mod(1px, 2deg)`-style mixes of an angle with a length inside `min()`/`max()` are no longer accepted
as one value.
