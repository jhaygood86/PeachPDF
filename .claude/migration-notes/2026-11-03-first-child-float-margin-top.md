# A float in inline flow now applies its `margin-top`

A `float` that is flowed with inline content (for example the first child of a block) used to be placed with its
border box at the line top whatever its `margin-top`. It now sits `margin-top` below the line's top edge (above it for
a negative value), as it already did after a block sibling and as browsers do.
