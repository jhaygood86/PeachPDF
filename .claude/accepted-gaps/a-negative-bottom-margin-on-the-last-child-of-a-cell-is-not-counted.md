# A negative bottom margin on the last child of a table cell is not counted when the cell aligns its content

`<td style="vertical-align:middle"><div style="margin-bottom:-10px">` in a 100px row puts the text at y=40.3 where Chrome has 45 (tracking issue #2260): the content ends at the bottom margin edge, above the block's bottom, so there is more room (CSS 2.1 sections 8.3.1 and 10.6.7). `CellContentBottom` measures it, but the old word-rectangle measure it is combined with by a maximum is the larger and does not see the margin. The same on the commit before `CellContentBottom` existed.
