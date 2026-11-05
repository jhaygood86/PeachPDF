# `:is()` and `:where()` drop an invalid argument instead of invalidating the rule

**Before.** `p:is(.a, :not-a-real-pseudo) { ... }` (or a list with an empty item, as a minifier can leave) was rejected as a whole, so the rule matched nothing
even for `.a`. Style sheets that carry a vendor pseudo-class or a newer selector inside `:is()`/`:where()` (Tailwind's variant selectors are written this way)
lost those rules silently.

**Now.** As Selectors 4 specifies for a forgiving selector list, the arguments that do not parse are dropped and the rest still match; a list with nothing valid
matches nothing and does not invalidate the rest of the rule's selector list. `:-webkit-any()` and `:-moz-any()` are unchanged (their lists are not forgiving).

Not verified against `v0.9.26-preview1` beyond the behaviour above; the reference page already described the list as forgiving.
