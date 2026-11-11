# A float after text differs from a browser's line layout inside columns, and `shape-outside` is not applied everywhere (#1784, #1785)

Outside a multi-column container a float placed after text now lands where a browser puts it: the line moves beside the float when the words so far fit
beside it (a `white-space: nowrap` line too), and otherwise the float goes below the line while the text after it carries on along the line it was on
(`CssLayoutEngine.FlowFloatChild`: `ShiftLineBesideFloat`, then `PlaceFloatAtLineTop` at the line's bottom). Measured against Chromium on 294 generated documents
(a float of three widths and two heights on either side, after 3 to 20 words and before 3 to 25, with and without `white-space: nowrap`, and two left floats after one
run; `FloatAfterTextMatchesBrowserTests` keeps a slice): 790 of 6,925 words differed before and none do now.

**Still different:**

- **Inside a multi-column container** the line is closed and the float opens the next one, as before (`blockBox.HtmlContainer.CurrentFragmentainer.HasOwnBand`). A
  float placed below a line that goes on does not fit the column fill's own bookkeeping (`FloatsPlacedOnThisLine`, the crowded-line shift's refusals):
  `LineBelowACrowdingFloat_WithNegativeMarginBottom_ClearsItsBorderBox` draws words over the float when it is tried. A `nowrap` line there is left as it is.
- **A float placed on a column's own line that is wider than the column** (`<div style="columns:2;width:347px">w1 w2 w3 w4 <div style="float:left;width:260px;height:40px">F</div> w5 w6 w7 w8</div>`):
  the float moves to the next column and the words after it overlap it, the last one starting 64px past the container, where a browser puts the float
  below the text of its own column. The crowded-line shift refuses for a float of the line itself (`ShiftEmptyLineBelowCrowdingFloats`, `FloatsPlacedOnThisLine`),
  which is also what keeps a multi-column container in a table cell from being divided into columns (see
  [the table-cell gap](a-multicol-container-with-a-float-in-a-table-cell-is-not-columnized.md)). With a margin on the float, or a right float, the document is placed correctly.
- **`shape-outside` is not supported by the shift rule** (#1785). Text wraps around the float's rectangle where a shaped float is involved, because the line's
  words are moved against the rectangle. A shape-aware line extent in `ExtentBesideFloats` would be needed.
- **Whitespace-only text nodes on both sides of a float** (`<span>a</span> <div style="float:left"></div> <span>b</span>`) lose their space: the two words are
  glued with no break opportunity between them, where a browser keeps one space. Running text and words with their own spaces are unaffected.

**Crowded lines:** the post-wrap `ShiftEmptyLineBelowCrowdingFloats` call fires whenever a wrapped line is crowded by floats, not only by floats wider than the line. On the reviewer's float-first documents (320) and scroll containers with block children and floats (160), 84 and 43 differ from `main`. The net effect is positive (words over a float 760 to 561, word loss 70 to 0; 5 float-first documents and 1 scroll document get worse). Documents with floats up to 45% of the container are identical to `main`.
