# `overflow-clip-margin` and `contain: inline-size` are not supported

- Paint containment clips to the overflow clip edge (css-contain-2 §3.4), which is the padding edge extended by `overflow-clip-margin`. That property does not exist here, so a paint-contained box (and an `overflow: clip` box) clips at the padding edge itself, its initial value; a margin an author sets is ignored.
- The `inline-size` keyword of `contain` (CSS Containment 3) is not in the grammar, so a declaration using it is invalid. `container-type: inline-size` is what the engine uses for query containers.

Why left: neither is needed by what containment does today; the clip would need the margin carried through the emitter's clip geometry. Tracking issue: [#2310](https://github.com/jhaygood86/PeachPDF/issues/2310).
