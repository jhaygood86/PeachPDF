# A `display: none` child with a height moves a table cell's centred content out of it

`CssBox.GetMaximumBottom`, which a cell's vertical alignment still takes as one of its two measures, has a fallback for a box with an explicit `height` that does not skip a `display: none` box, so the hidden box's height counts as content: a `div` with `display:none;height:100px` after the text in a middle-aligned cell puts the text 40px above the cell's top where it is centred in a browser (tracking issue #2255). `CellContentBottom` skips such a box, but the old measure it is combined with does not.
