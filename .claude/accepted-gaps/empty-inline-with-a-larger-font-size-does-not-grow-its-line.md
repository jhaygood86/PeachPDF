# An empty inline with a larger `font-size` does not grow its line (#1310)

**Gap:** an empty inline element with its own larger `font-size` (for example a positioned badge
wrapper, `<span style="position: relative; font-size: 40pt"><span style="position: absolute">…`) does not
make the line box taller. Chrome grows the line to the inline's own strut, as for an inline with text.
CSS 2.1 §10.8 sizes a line from the inline boxes in it, and §9.4.2 still generates one for an empty
inline element.

**Where:** the line's height comes from the words placed on it, and an empty inline places none. The
zero-width containing block `CssLayoutEngine.EmptyInlineContainingBlockFor` gives such an inline does use
its own font height, so that containing block extends above a line sized from the surrounding text.

**Why out of scope:** reported in review of the #1299 follow-up. It predates that change for any empty
inline; #1299 only made it visible by anchoring positioned boxes to the empty inline. Fixing it means
letting a wordless inline take part in line-height computation, which that change did not touch.
[The empty inline's own `vertical-align`](empty-positioned-inline-ignores-its-own-vertical-align.md) is
the same missing piece.
