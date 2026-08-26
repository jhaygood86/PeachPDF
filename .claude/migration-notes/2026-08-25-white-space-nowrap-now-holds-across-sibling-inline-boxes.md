# `white-space: nowrap` now holds across sibling inline boxes

**Landed:** 2026-08-25 — `CssLayoutEngine.FlowBox`'s `wrapNoWrapBox` mechanism didn't check whether
the containing block itself already forbade wrapping
**Doc section:** docs/html-css-support.md § [Text Layout](../../docs/html-css-support.md#text-layout)

`white-space: nowrap` on a block correctly prevented wrapping within a single inline box's own text,
but content split across more than one sibling inline box - even a plain `<span>`, not just `<b>` or
similar - could still wrap onto a second line box. This affected any `nowrap` block containing inline
markup, and specifically undermined the common `overflow: hidden; white-space: nowrap; text-overflow:
ellipsis` "truncate" idiom whenever the truncated text contained inline elements: the content ended
up spread across multiple, individually non-overflowing lines instead of one long overflowing line
that `text-overflow` could actually truncate.

A `white-space: nowrap` block's content - regardless of how many inline element boundaries it
crosses - now always renders as a single line box, as the spec requires. A `nowrap` block containing
only plain text, or a *nested* element that re-declares its own `white-space: nowrap` inside an
otherwise normally-wrapping block (which correctly moves as a whole unit to a new line when it
doesn't fit), are both unaffected by this change.
