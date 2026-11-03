# An absolutely positioned box among inline content uses its static position

With `top`/`left` (or `right`/`bottom`) both `auto`, `position: absolute` among inline content used to land at the containing
block's padding corner. It now sits at its place on the line: after the preceding word, at the line's top.
