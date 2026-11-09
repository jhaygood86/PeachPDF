# `content-visibility: auto` never skips its contents

`content-visibility: hidden` skips its contents (`DomParser.SkipHiddenContents` gives them `display: none` after the cascade and the box keeps layout, style, paint and size containment). `auto` turns on layout, style and paint containment but is treated as always relevant to the user, so nothing is ever skipped and it never gets size containment from being skipped:

- In a paged document every page is shown, so "not relevant to the user" is nothing, which is also what browsers do when printing. That part is not a deviation.
- On a screen (`MediaType.Screen`, a host that scrolls) `auto` should skip what is off the viewport and give it its `contain-intrinsic-size`. Layout is a single pass that learns where a box is only after it has laid out, so skipping by position needs a layout, a decision against the viewport rect and a second layout, and again whenever the host scrolls. Not done.
- `contentvisibilityautostatechange` and the find/focus/selection reveal of skipped `auto` contents have no counterpart: there is no script and no find.
- `hidden=until-found` still maps to nothing, and `::details-content` still cannot be styled (see [details-content-pseudo-and-content-visibility.md](details-content-pseudo-and-content-visibility.md)).

Tracking issue: [#2309](https://github.com/jhaygood86/PeachPDF/issues/2309).
