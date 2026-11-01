# font-size calc() mixing parent-relative and lazy units (follow-up to #2003)

A font-size `calc()` that also uses `rem`, a root-element unit, a viewport unit or a container unit has to stay lazy (it needs a
layout-time basis), and the Font area is inherited by reference, so every inheriting descendant measured the calc's `em`/`%`
terms against its own parent again (`calc(2em + 1rem)` under 20pt: 51pt for the declaring box, 113pt for its child).

**Fix:** `ResolveFontSizeValueComputation` still returns calc text for those, but first rewrites each parent-relative term
(`em`/`ex`/`ch`/`cap`/`ic`/`lh`/`%`, found by `ParentRelativeTerm()`) to its absolute `pt` value against the parent. What is left
(`rem`, `vw`, `cq*`...) does not depend on the parent, so re-resolving the text in a descendant gives the same size. This beat a
"declared here vs inherited" flag, which would have needed the same distinction for `font-size: inherit` and for areas cloned by
copy-on-write when a child changes an unrelated font property.

**Not done:** the viewport/container units are resolved the first time `ActualFont` is read, so a box whose font is read before the
page size is known (a text box, at word splitting) still sees 0 for them - the existing accepted gap
`font-size-container-relative-units-resolve-to-zero-for-text-content`. The tests therefore use `rem` as the lazy unit, not `vw`.

**Evidence:** three theory cases in `FontSizeInheritanceIntegrationTests` (`calc(2em + 1rem)`, `calc(100% + 1rem)`,
`calc(1ex + 2em + 1rem)`); the first two failed before (113pt vs 51pt, 42pt vs 31pt), the third was added after the change. Engine
16953/16953, `PeachPDF.Tests` green.
