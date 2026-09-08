# A relocated table no longer repeats its abandoned header

A table that carries `break-inside: avoid` (or `page-break-inside: avoid`) and does not fit in what
is left of a page moves whole to the next one. When such a table also repeats a `<thead>`, the
headers belonging to the abandoned attempt were drawn as well.

**Before.** Three copies of the header row: one stranded near the bottom of the page the table had
left, with no table beneath it, and two drawn exactly on top of each other at the top of the page
the table moved to. Overprinted text looks bolder or slightly smudged rather than obviously
doubled, so the usual first sign was the stray header on the previous page.

**Now.** One header, at the top of the page the table lands on — which is what a browser draws.

**When a document author would notice.** Only where all three hold: a table with a `<thead>`, an
avoiding `break-inside` on it (a common house style for keeping a small table together, and note
that the UA print stylesheet supplies it for the header group itself, not for the table), and a
position on the page that leaves too little room. A table that splits across the boundary is
unaffected and repeated its header correctly before and after.

Nothing needs changing in a document. Output that was previously worked around by removing
`break-inside: avoid` from such tables can have it back.
