# The forced-colors media feature is evaluated

Before: `@media (forced-colors: none)` was an unknown feature, so the block never applied (and `(forced-colors: active)` was equally false).

Now: `forced-colors` reports `none`, as a PDF is never drawn under a forced palette: `(forced-colors: none)` and `not (forced-colors: active)` match, `(forced-colors: active)` and the boolean `(forced-colors)` do not.
