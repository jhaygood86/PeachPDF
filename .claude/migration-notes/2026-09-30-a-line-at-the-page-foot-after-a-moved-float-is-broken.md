# A line at the page foot after a float that moved to the next page is broken

**Before.** A float with no room at the foot of a page (three lines, so that splitting it would leave a widow) moved to the top of the next page. The paragraph after it stayed where it was, and a line of it that crossed the foot of that page was not broken onto the next page: it was painted there and cut in two by the page clip.

**Now.** That line moves to the next page whole, like any other line that does not fit. A document that had the cut line shows it complete, and the lines after it shift down by one line.

**Why.** The layout cursor had followed the float to the next page, so the paragraph's lines were checked against the wrong page. It now goes back to the paragraph's own page once the float is placed.
