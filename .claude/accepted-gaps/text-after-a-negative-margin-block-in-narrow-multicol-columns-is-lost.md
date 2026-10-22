# Text after a negative-margin block in a multi-column container with narrow columns can be lost (#1896)

**Gap:** in a multi-column container whose text sits directly in it, with `column-width: 60px`, floats among the text and a `float: none` block with a large negative `margin-top` (`-200px`), the text after that block is not laid out: seven of 123 words in the measured document, where `main` with the text inside an explicit `<div>` keeps them. Found by a fuzz comparing words kept against `main`; the document is `input/round7-R4-negative-margin-block.html` of the review evidence.

**Why:** not found. The loss remains with the text-beside-float shift and the post-wrap float clearance both disabled, so it comes from another part of the inline-run wrapping. It needs a negative margin larger than the content above it and columns of a few words, and the 1,400-document multicol fuzz of the review found no other instance.

**Not claimed:** that it happens with normal margins, or with a `<div>` around the text on `main`.
