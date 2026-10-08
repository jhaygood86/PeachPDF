# A grid container that is a flex item is sized from the container, not its tracks

A flex item with `width: auto` takes its max-content width as its flex base size
([css-flexbox-1 §9.2](https://www.w3.org/TR/css-flexbox-1/#algo-main-item)); a grid container's max-content
width is the sum of its tracks plus gaps. PeachPDF sizes a `display: grid` (or `inline-grid`) flex item from
the flex container's available width instead: two 50pt tracks in a 300pt flex row measure 290.36pt, not
100pt, and push the next item past the container edge (`zz` at x=310pt). A nested flex container, a block
with a width, and an `inline-grid` in a plain block container all measure correctly in the same fixtures.

**History.** `display: grid` flex items measured 290.36pt before inline-level items were blockified, so this
is not a regression. An `inline-grid` flex item measured 24.14pt before that change — wrong too, and its
children were not laid out at all — and agrees with `display: grid` (290.36pt) now
([recent fix](../recent-fixes/2026-10-08-inline-level-flex-and-grid-items-are-blockified.md)).

Filed as [issue #1663](https://github.com/jhaygood86/PeachPDF/issues/1663).
