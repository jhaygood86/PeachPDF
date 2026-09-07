# CSS scientific-notation numbers now parse correctly

Any CSS numeric value written in scientific notation - `1e2`, `1.5e-3`, `1E2`, a length like `1e2px`,
or a percentage like `1e2%` - used to be silently mis-tokenized: the value's leading digits and the
`e`/`E` were split off as a bogus one-letter dimension (e.g. `1e2` read as "the number 1 with unit
`e`" followed by a stray `2`), so the value was effectively discarded or misread by whatever CSS
property consumed it, with no warning.

It's now parsed correctly as CSS Syntax Level 3 §4.3.13 describes: `1e2` is the number `100`, `1e2px`
is the length `100px`, and `1e2%` is the percentage `100%`. A document that happens to use scientific
notation in its CSS (uncommon by hand, but a possible output shape from CSS produced or transformed by
other tooling) will now be measured/rendered using the intended value instead of a silently-wrong one.
