# A `calc()` height containing a percentage now computes to `auto` against an indefinite containing block

`height: calc(100% - 5px)` (or any `calc()`/`min()`/`max()`/`clamp()` containing a percentage) against a
containing block with no definite height of its own used to be resolved against that block's
content-driven height and painted at that value, while the box after it was placed as if the height were
`auto`. It now computes to `auto`, exactly like a bare percentage height already did, per CSS 2.1 §10.5. A
browser has always treated both the same way.

This is new relative to v0.9.19, where the height-resolution code detected "this is a percentage" by
checking whether the value ends in `%` — true for `50%`, false for `calc(100% - 5px)`, which ends in `)`.
The fix is a real check of the value's parsed `calc()` expression tree for a percentage anywhere in it,
not a change to how `auto` itself is computed.

Nothing changes for a `calc()` height with no percentage in it (`calc(4px + 6px)`), or for any height
against a containing block whose own height *is* definite.
