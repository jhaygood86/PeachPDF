# A percentage `line-height` gives every line a height of zero

`line-height: 150%` draws consecutive lines on top of each other and gives their blocks no height, where a browser makes it 150% of the element's font size (tracking issue #2253). A reviewer pointed at `DerivedStyle.ActualLineHeight`, which resolves the percentage against the box's own height (0 while its lines are laid out); only the symptom was checked.
