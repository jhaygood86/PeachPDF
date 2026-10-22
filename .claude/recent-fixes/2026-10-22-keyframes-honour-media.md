# `@keyframes` inside `@media` honours the query (#1822)

`RegisteredKeyframes.BuildRegistry` enumerated rules with `CssData.EnumerateRulesRecursive`, which descends into `@media` unconditionally (right for `@font-face`/`@property`, whose guards are per-candidate-box and have nothing to test against). A keyframes rule is document-wide, so `@media screen { @keyframes k }` was registered under the print medium, and a later non-matching copy could replace an earlier matching one.

Fix: `EnumerateRulesRecursive`/`FlattenRules` take an optional `MediaQueryContext`; with one, an `@media` block is descended only when `MediaQueryMatcher.Matches` accepts it. `DomParser` passes the same context it builds for the cascade. Only keyframes pass one; other registries keep the unconditional descent. `@container` around keyframes is still descended unconditionally.
