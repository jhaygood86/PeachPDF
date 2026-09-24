# A float or inline-block taller than a page no longer loses the lines that do not fit

**Before:** a `float` placed among inline text, or an `inline-block` holding block content, taller than the rest of
its page showed only the lines that fit on that page; the rest were never drawn. A `float: top`/`bottom`/
`top-bottom`/`snap` taller than the page lost the lines that fell above it.

**Now:** all of the content is drawn, running on across as many pages as it needs. A page float taller than the
page's content band starts at the top of its page; nothing is reserved for it, so in-flow content around it can
overlap it.
