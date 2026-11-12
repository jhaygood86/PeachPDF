# Flex and grid items paint in item order, so extracted text reads in order

**Before.** A flex or grid item that was a block holding only inline content painted after a sibling item that was itself a flex container, so
text extracted from the PDF could come out `Signup Shop` for items written `Shop`, `Signup`. Rendering was the same.

**Now.** Every item of a flex or grid container paints in one pass, in item order, so the text reads in that order. Items with a non-default
`order` still paint in document order (a known gap).

Closes #1668 and #1440.
