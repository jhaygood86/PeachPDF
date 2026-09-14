# Shrink-to-fit lines and positioned descendants now keep their specified order

Shrink-to-fit boxes whose child lines have different padding or borders now choose the widest complete
line. Previously PeachPDF could combine the content width from one line with decoration from another,
creating a width that no actual line required. Decoration on nested boxes is also accumulated through
the full containment chain. Affected floats, absolutely positioned boxes, and automatic table columns
may therefore become narrower or wider and can reflow their contents.

Positioned descendants at the default stack level now paint in document-tree order across positioning
schemes. A `position: relative` descendant no longer necessarily paints before or after an overlapping
`position: absolute`, `fixed`, or `sticky` descendant merely because the two use different positioning
schemes.
