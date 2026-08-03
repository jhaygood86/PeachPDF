# The `@container` convergence loop is bounded at 4 passes, not guaranteed to converge

`HtmlContainerInt.PerformLayout`'s container-query convergence loop (1 baseline pass + up to 3
refinement passes, each a full re-parse/re-cascade/re-layout) stops either when a size query
container's resolved size stops changing pass-over-pass, or when the pass cap is hit — whichever
comes first. On cap exceeded, the last pass's result is accepted silently rather than detecting or
specially resolving the non-convergence. This mirrors the same "bounded, not perfect" stance already
accepted for `UseVariablePageWidth`'s own 3-iteration reflow loop (see the "named-page L/R reflow"
note in `docs/architecture.md`'s per-page reflow section).

CSS Containment 3 doesn't define a pass limit; a spec-conformant UA is expected to reach a stable
result. A pathological stylesheet where a container's own size depends, directly or transitively
(e.g. through `fit-content`/auto sizing of content that is itself `@container`-gated against that
same container's own breakpoint), on its own `@container` condition could in principle oscillate
indefinitely and never settle within 4 passes. No real-world reproduction is known to actually hit
this — real `@container` usage doesn't construct such cycles — but the gap is real in principle.
Tracked as [#614](https://github.com/jhaygood86/PeachPDF/issues/614).
