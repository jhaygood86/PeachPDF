# An empty positioned inline ignores its own `vertical-align` (#1308)

**Gap:** an inline whose only content is an absolutely positioned box has no line fragment, so
`CssLayoutEngine.EmptyInlineContainingBlockFor` gives it a zero-width containing block on the line
while that box is laid out. Its block extent is one font height from the line's `BaselineY` less the
inline's ascent, so it always sits on the baseline. The inline's own `vertical-align` (`super`, `top`, a
length) is not applied. CSS 2.1 §10.8.1 applies `vertical-align` to every inline-level box, and
§10.1 / CSS Positioned Layout 3 §2.1 form the containing block from that box wherever it ends up.

**Where:** `ApplyVerticalAlignment` moves only words and line `Rectangles`, and an empty inline has
neither, so no offset is ever computed for it. `EmptyInlineContainingBlockFor` reads the line's
baseline directly.

**Why out of scope:** found in the post-change review of the #1299 follow-up. The common shape, a badge
or tooltip wrapper with no `vertical-align`, is correct without it, and computing the offset means
resolving the inline's `vertical-align` against the line outside the vertical-align pass, or teaching
that pass about a box with no words. #1307 is the same missing piece for an empty atomic `inline-block`,
which paints in the wrong place. A fix there that gives the empty box a rectangle the pass can move is
likely the shape to reuse here. Since #1310 the empty inline's extent does size its line
(`FlowBox`'s `PlaceEmptyInline`), but always as a baseline-aligned box, so a fix here has to change what
that contributes as well as where the containing block sits.
