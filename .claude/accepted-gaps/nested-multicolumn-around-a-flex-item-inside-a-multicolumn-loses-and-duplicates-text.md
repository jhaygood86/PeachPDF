# A multi-column container inside a flex item inside a multi-column container loses text and draws other text twice

A multi-column container inside a flex item that is itself inside a multi-column container, with content longer than a
page, loses some of its text and draws other text a second time. Measured on a 300pt x 200pt page with 20pt margins and
a document of 160 unique words (the document is in the tracking issue): 214 words are drawn, 38 of the document's words
are missing and 92 words are drawn twice. Which parts matter: removing the outer `columns` gives all 160 words once,
removing the inner `columns` still loses 7 words, removing the flex container still draws 110 words twice.

It is independent of `overflow`. It matters to the change that lets an auto-height `overflow: hidden` wrapper break,
because that wrapper used to stay whole and so hid it: with the wrapper as a hidden box the document draws 152 of 160
words before that change (8 lost) and 201 after (51 lost, 92 twice), while with a plain `<div>` in its place both
builds draw the 214 above. A hidden wrapper around a single engine is fine or better after the change: around a
two-column container, a table, a flex row and a grid it lost 24, 16, 24 and 24 words before and 0, 0, 12 and 12 after.

Not diagnosed further than the measurements above. The alternative that was considered and not taken is keeping any
wrapper that holds a multi-column container unbreakable: it would make the nested case behave as before, and give up
the improvement for the plain wrapper around columns.

Tracking issue: #1525.
