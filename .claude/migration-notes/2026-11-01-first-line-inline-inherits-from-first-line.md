# Inline elements on a ::first-line line inherit from ::first-line

Before: every word on the first line of a block with a `::first-line` rule was given the block's `::first-line` style, whatever inline element held it, and the line box was sized from each inline's own cascaded font. `#p::first-line { font-size: 10pt }` on a 40pt paragraph holding `text<span>x</span>more` therefore gave a 48pt first line (an empty `<span></span>` did the same), and a `<span>` that declared its own `color` or `font-size` was painted in the `::first-line` one.

Now: each inline element, pseudo-element and run of text on the first line has a style that inherits from `::first-line` with its own matching declarations applied over it (CSS Pseudo-Elements 4 section 2.1.3). The example above gives a 12pt line; a `<span>` that declares its own `font-size` or `color` keeps it. Lines after the first are unchanged.
