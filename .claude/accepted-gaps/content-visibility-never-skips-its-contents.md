# `content-visibility` applies its containment but never skips the contents

`content-visibility: auto` turns on layout, style and paint containment (`ContainGrammar.Of`), and `hidden` turns on all four, so a box carrying either is an independent formatting context, a containing block, a stacking context and (paint) a clip. What neither does is skip anything:

- `hidden` should skip its contents, as if they were `display: none`. Its contents are still laid out and painted.
- `auto` should skip its contents while the element is not relevant to the user, and apply size containment then. Every element is treated as relevant, so nothing is skipped. A paged document has no viewport to be off-screen from, so this one may stay a documented difference.
- `contain-intrinsic-size` is not a property, so a skipped box could not take a placeholder size.

Why left: skipping needs layout, fragment-emission and query changes (the box's subtree must exist for `find`, focus and selection under `auto` and not under `hidden`), and no page seen so far needs it. Tracking issue: [#2309](https://github.com/jhaygood86/PeachPDF/issues/2309). See also [details-content-pseudo-and-content-visibility.md](details-content-pseudo-and-content-visibility.md).
