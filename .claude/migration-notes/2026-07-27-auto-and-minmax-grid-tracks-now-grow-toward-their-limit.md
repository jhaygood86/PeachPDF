# `auto`/`minmax()` grid tracks now grow toward their growth limit

**Landed:** 2026-07-27 (32b1d2aa) — Grow a grid track toward its limit with the space that is there
**Doc section:** docs/html-css-support.md § [Grid](../../docs/html-css-support.md#grid)
**Verified against v0.9.6:** this note was not present in the `v0.9.6` tag's docs — confirmed genuine behavior change since 0.9.6, in scope for the next release notes.

An `auto` (and `minmax()`) track used to be sized straight to its max-content contribution and only ever grown from there, so a grid narrower than its own content overflowed itself and a `minmax()` track beside an `fr` stayed at its floor. Tracks are now maximized toward their growth limits with the space the container has, so a `minmax(60pt, 100pt)` beside a `1fr` in a 300pt grid reads 100/200 rather than 60/240, and a grid too narrow for its content wraps inside itself instead of painting outside it. See [Forward compatibility](#forward-compatibility).
