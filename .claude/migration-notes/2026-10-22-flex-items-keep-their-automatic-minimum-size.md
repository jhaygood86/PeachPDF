# A flex item in a row no longer shrinks below its content

An item of a `display: flex` row whose `min-width` is `auto` (the initial value) used to be shrunk by `flex-shrink` past its min-content width: an `<img>` was squeezed under its natural width, a `nowrap` or long-word item was narrowed so its text overflowed it, and a row of `width`-carrying images all shrank to share the container. It is now floored at its automatic minimum size (css-flexbox-1 §4.5): the min-content width, or its definite `width` when that is smaller, and the shrink it cannot take goes to the other items. The line overflows its container instead, as in a browser. An item with `min-width: 0`, or with `overflow` other than `visible`/`clip`, still shrinks freely. A document that relied on items squeezing to fit now overflows (the `clip_path` showcase's seven 130px tiles did, and wrap now). Add `flex-wrap: wrap` or `min-width: 0` to get the old fit. Column containers and grid items are unchanged (see the accepted gap).

(`git show v0.9.23:src/PeachPDF/css-properties.json` has `min-width` with initial value `0`, which could not be told apart from an authored `min-width: 0`.)

An animation between `min-width: auto` and a length now flips half way instead of mixing from 0, because `auto` is no longer the same value as `0` (css-sizing-3 makes `auto` the initial value; it is not interpolable).
