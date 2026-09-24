# `float` and `clear` accept `inline-start` and `inline-end`

**Before:** `float: inline-start`, `float: inline-end`, `clear: inline-start` and `clear: inline-end` were invalid
values and were dropped, so the element did not float and did not clear.

**Now:** they work as [CSS Logical Properties](https://www.w3.org/TR/css-logical-1/#float-clear) defines them, against
the containing block's `direction`: in an `ltr` block `inline-start` is the left side and `inline-end` the right, and
in an `rtl` block the reverse. A stylesheet that already used them for browsers now renders the floats.
