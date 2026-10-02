# A flex item holding a vertical box with a declared width is as wide as that box

**Before (v0.9.20):** a flex item (or any shrink-to-fit box) containing a `writing-mode: vertical-*` box with a declared `width`
measured the box's text as if it were horizontal, so the item came out wider or narrower than the box depending on how many
words the text had; a neighbouring item could be drawn over it, or sit far away.

**Now:** the item is as wide as the vertical box's declared outer width (or its label, if that is wider), as in browsers. A
vertical box with `width: auto` is measured as before.
