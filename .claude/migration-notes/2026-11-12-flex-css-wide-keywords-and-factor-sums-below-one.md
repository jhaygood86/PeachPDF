# `flex: initial` is `0 1 auto`, and flex factors that add up to less than 1 no longer take all the free space

**Before.** `flex: initial` (and `inherit`/`unset`/`revert`) was read as the first longhand's keyword with the other two
taking their omitted-value fallbacks, so `flex: initial` gave `flex-basis: 0` and an item's `width` was ignored. An item
whose `flex-grow` (or the sum over the line) was below 1, such as `flex-grow: .3`, took all of the free space; `flex-shrink`
sums below 1 removed all of the overflow.

**Now.** A CSS-wide keyword on `flex` reaches `flex-grow`, `flex-shrink` and `flex-basis` as itself, so `flex: initial` is
`0 1 auto`. When the unfrozen items' flex factors add up to less than 1, only that fraction of the free space (or of the
overflow) is shared out (CSS Flexbox 1 section 9.7 step 4c), as in browsers: a `flex-grow: .3` item with 100pt of content in a
400pt row is 190pt wide, not 400pt.

Closes #2331 and #2356.
