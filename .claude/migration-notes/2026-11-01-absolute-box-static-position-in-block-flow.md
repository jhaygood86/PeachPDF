# An absolutely positioned box in block flow sits at its static position

Before: with `top`/`left` (or both `right`/`bottom`) left `auto`, an absolutely positioned box sat at its containing block's top-left padding corner, on top of whatever was there: `<div style="position:relative"><p>para</p><div style="position:absolute">x</div></div>` drew the box over the paragraph.

Now: on each axis where both offsets are `auto`, a box in block flow sits where it would have been in flow (CSS 2.1 sections 10.3.7 and 10.6.4): below the previous in-flow sibling and at the parent's content edge, plus its own margin. An axis with an offset set is unchanged. A box among inline content and a box in a `direction: rtl` parent keep the previous placement.
