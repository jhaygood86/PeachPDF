# Lines at a slice boundary of a tall scroll container are kept

Before: a capped scroll container (`overflow` other than `visible` with a height) taller than a page lost any
line that crossed a page boundary: the first page cut it at the clip and the next page did not draw it.
After: both pages draw it, each showing its own part, so no text goes missing. Same for inline-blocks taller
than a page.
