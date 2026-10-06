# Underline, overline and line-through no longer move with an inline box's vertical padding or border

Before: an inline element (`<span>`, `<a>`) with `padding-top`/`padding-bottom` or a top/bottom border drew its
`text-decoration` lines displaced by that padding or border: an underline rode up through the text with bottom
padding, an overline rose with top padding. After: the lines sit where they would on the same text without the
padding or border, as in a browser.
