# A relatively positioned last child's offset is counted as content of a table cell

`<td style="vertical-align:middle"><div style="position:relative;top:15px">` in a 100px row puts the text at y=47.8 where Chrome has 55 (the line centred, then drawn 15px lower; tracking issue #2259). A cell's alignment takes the larger of two measures of where its content ends: `CellContentBottom`, which uses the child's static position (CSS 2.1 section 9.4.3), and the old word-rectangle measure, whose words carry the offset and win. The same on the commit before `CellContentBottom` existed.
