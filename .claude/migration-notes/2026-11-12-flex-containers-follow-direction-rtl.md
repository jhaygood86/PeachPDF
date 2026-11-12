# A flex container with `direction: rtl` is mirrored

**Before.** `direction: rtl` had no effect on a flex container: a row ran left to right and `justify-content: flex-start` packed
against the left edge.

**Now.** A `row` runs from the right edge to the left (`row-reverse` from the left), `flex-start`/`flex-end`/`start`/`end` follow
that side, `left`/`right` stay physical, and in a column `align-items: flex-start` aligns items to the right edge, as in browsers.
Documents that set `direction: rtl` (or `dir="rtl"`) on a flex container, or inherit it from `<html dir="rtl">`, are mirrored.

Closes #2336.
