# `font-weight: bolder`/`lighter` moved from the CSS 2.1 table to the CSS Fonts 4 table

**Before:** `bolder`/`lighter` stepped from the parent's resolved `font-weight` using CSS 2.1 §15.6's
worked table, which only has breakpoints at 400/500(/700). Concretely: `bolder` gave 400 below a parent
weight of 400, 700 up to and including 500, and 900 above that; `lighter` gave 100 up to and including
500, 400 up to and including 700, and 700 above that; a `bolder` parent weight above 900 (e.g. 950) was
clamped down to 900.

**Now:** the same keywords follow CSS Fonts 4 §2.2.1's table, which has breakpoints at 100/350/550/750/900
instead: `bolder` gives 400 below a parent weight of 350, 700 from 350 up to (not including) 550, 900 from
550 up to (not including) 900, and leaves a weight of 900 or more unchanged (no longer clamped to 900);
`lighter` leaves a weight under 100 unchanged, gives 100 from 100 up to (not including) 550, 400 from 550
up to (not including) 750, and 700 at 750 or more.

The two tables agree at every multiple of 100 (all CSS 2.1's table tabulates), so a document that only
ever used whole-hundred parent weights (100, 200, … 900) sees no change. A document with a parent weight
that is not a multiple of 100 - including a fractional weight such as `font-weight: 520.5`, which the
engine has supported since fractional `font-weight` landed - can see a different rendered weight for
`bolder`/`lighter` text now than before, in these bands: a `bolder` parent weight in (350,400) or (500,550)
(e.g. 380 or 520 now step to 700, not 400/900), a `lighter` parent weight under 100, in (500,550), or in
(700,750) (e.g. 520 now steps to 100 instead of 400; 720 now steps to 400 instead of 700; a sub-100 parent
weight is now left unchanged instead of forced down to 100), and a `bolder` parent weight above 900 (now
left unchanged instead of clamped to 900).

Confirmed against `docs/html-css-support.md` and `docs/usage-examples.md` at the last release tag: both
described the CSS 2.1 table, so this is a genuine behavior change relative to the last release, not
something that already shipped described differently.
