# `cqw`/`cqh`/`cqi`/`cqb`/`cqmin`/`cqmax` resolve to 0 with no eligible ancestor query container

Per CSS Values and Units / CSS Containment 3, a container-relative length unit with no eligible
ancestor query container in the relevant axis falls back to the corresponding small-viewport unit
(`cqw`→`svw`, `cqh`→`svh`, `cqi`→`svi`, `cqb`→`svb`, `cqmin`→`svmin`, `cqmax`→`svmax`), not to `0`.

PeachPDF has no viewport-unit implementation at all — `vw`/`vh`/`vmin`/`vmax` already resolve to a
hardcoded `0` in `Length.ToPixels` (the same file `cq*` units are implemented in). `cq*` follows the
exact same fallback chain the spec describes, it just terminates at a numerator (`sv*`) PeachPDF
doesn't implement yet, landing on `0` for the same reason `vw`/`vh` do.

This only affects the misuse case — a `cq*` unit with no `container-type: size`/`inline-size`
ancestor anywhere above it. The idiomatic case (a `cq*` unit inside the subtree of the element
declaring `container-type`) resolves correctly against the real container size, which
`CssBox.FindNearestQueryContainer` finds. Tracked as
[#615](https://github.com/jhaygood86/PeachPDF/issues/615).
