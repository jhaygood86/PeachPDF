# A nested table in a cell with a declared width is half a point narrow

`<td width="80"><table><tr><td width="80">` with a 20px box inside: the outer cell is exactly Chrome's 61.5pt, the nested table 59.5pt against Chrome's 60 and the nested cell 56.5 against 57. The half point is not understood, was not chased, and is not pinned by a test (the outer cell and an inner cell wider than it are). Tracking issue #2399.
