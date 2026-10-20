# A `rowspan` cell is painted once

Before (v0.9.23 included): a cell spanning N rows was drawn 2N-1 times on top of itself, so its text looked bold and a PDF
reader extracted it 2N-1 times. Now its content, background and borders are drawn once, and its text extracts once. Nothing
moves: the drawn positions are the ones the extra copies already had.
