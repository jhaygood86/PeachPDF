# A table's `width` and `max-width` now resolve `calc()`

Previously, a `<table>` whose `width` or `max-width` was a `calc()` expression (most commonly
`calc(100% - <length>)`, e.g. to compensate for a margin) had that value silently ignored. The gate
that decided whether a width was "specified" only recognized a bare `<number><unit>` and treated
anything else — `calc()` included — as if no width had been set at all, so the table fell back to
sizing itself from its content instead. Because that fallback sizing is unaware of the table's own
margin, a table with a margin-compensating `calc()` width could end up wider than intended and
overrun its containing block.

`calc()` (and `min()`/`max()`/`clamp()`) now resolve for a table's `width`/`max-width` exactly as
they already did for every other box's width.

Incidentally, a literal `width: 0` or `max-width: 0` on a `<table>` is now also treated as a
definite zero-length value rather than falling back to the same "unspecified" sizing — consistent
with how a literal `0` is already treated everywhere else in the codebase. The table is still never
sized narrower than its content's own minimum width, so this does not produce a genuinely
zero-width table, only one that no longer grows past that minimum the way an actually-unspecified
(`auto`) width would.
