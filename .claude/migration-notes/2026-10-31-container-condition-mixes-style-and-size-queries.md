# `@container` conditions can mix `style()` and size features

**Before:** only one form was read per rule. `@container (min-width: 300px) and style(--theme: dark)` ignored the
`style()` half, and `@container style(--a: 1) and style(--b: 2)` ignored everything after the first `style()`.
A container named `not`, `and` or `or` was also read as a name.

**Now:** the whole `<container-condition>` is evaluated, with `and`/`or`/`not` and parentheses over size features
and `style()` queries (CSS Containment 3 §3). A leaf with no eligible container is unknown and never matches, even
under `not`. `not`, `and`, `or` and `none` are no longer taken as a container name. A malformed mixed condition
matches nothing.

**Why:** a rule that used a mixed condition previously applied (or not) on the strength of half its condition.
