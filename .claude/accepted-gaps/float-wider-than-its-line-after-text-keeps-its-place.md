# A float wider than its line, placed after text, keeps its place and the text beside it can pass the container edge (#1850)

**Gap:** `CssLayoutEngine.FlowFloatChild` moves the words before a float beside it, or starts the float on the next line, only when the float's margin box is no wider than the line (`FloatFitsLine`). A wider float (for example a 344px left float in a 173px column) keeps the place it was given, and the words after it start at its far margin edge, which can lie outside the container and off the page.

**Why:** a word at the start of a line always fits, so the line beside a float wider than the line is not moved below the float (CSS 2.1 §9.5). Reopening the line for such a float sent the last words of the container past its edge, where `main` kept them, so that case keeps the placement `main` has. Fixing it needs the line placement to move a line below a float it cannot fit beside.

**Pinned by:** `FloatWiderThanItsLine_AfterText_KeepsEveryWordInsideTheContainer`.
