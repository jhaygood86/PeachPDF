# A float after text no longer draws over that text

**Before:** A float placed after text on the same line was positioned at the top of that line, and the text already on the line stayed where it was, so the float covered it. Inside a multi-column container, a float after text was laid out in the column flow, so the text on its line was not moved beside it at all.

**Now:** The text already on the line moves beside the float when it still fits on the line. Otherwise the float starts the next line. Text after the float flows beside it either way. In a multi-column container, a float after text is laid out in the inline flow like any other float. Balancing the columns does not count the space the float takes, so the words in each column can differ from before.

**Why:** A browser moves the text on a line beside a float placed after it, or moves the float below the line when the text cannot share the line with it. PeachPDF drew the float over the text instead. The float now sits beside the text, as a browser lays it out.
