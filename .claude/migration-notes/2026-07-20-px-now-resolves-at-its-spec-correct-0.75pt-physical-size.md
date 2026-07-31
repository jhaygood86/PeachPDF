# px lengths now resolve at their spec-correct 0.75pt physical size

**Landed:** 2026-07-20 (40107f96) — Document the spec-correct px unit contract and breaking change
**Doc section:** docs/html-css-support.md § [Length units](../../docs/html-css-support.md#length-units); also referenced from docs/getting-started.md
**Verified against v0.9.6:** already present verbatim in the `v0.9.6` tag's docs — this change **predates v0.9.6** (it shipped in v0.9.6 itself, per issue #150). It is not part of the 0.9.6→0.9.7 migration and should **not** be pulled into the next release notes; kept here only as the historical record of when the behavior actually changed.

Versions prior to this convention treated `1px` as `1pt` for non-font layout lengths, rendering px-sized content 33% larger than its true CSS size. Documents authored against that behavior saw px-derived lengths shrink by ×0.75 to their spec-correct physical size (`1px = 1/96in = 0.75pt`, matching browser print output). Absolute units (`pt`/`mm`/`cm`/`in`/`pc`) were unaffected, and font sizes in px already used the spec-correct ratio, so text set in px was also unaffected.
