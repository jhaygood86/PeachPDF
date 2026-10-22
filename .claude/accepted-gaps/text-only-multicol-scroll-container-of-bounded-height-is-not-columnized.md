# A text-only multi-column scroll container of bounded height is not laid out in columns (#1849)

**Gap:** `DomParser.CorrectInlineBoxesParent` does not wrap the text of a multi-column container that clips its content (`overflow` other than `visible`) and has a `height` or `max-height` (`IsHeightBoundedScrollContainer`). The text keeps the single inline flow it had on `main`, across the container's full width.

**Why:** wrapped, the text reaches the columns engine, which skips words when it fills columns of a bounded height (`width:300px; columns:3; overflow:hidden; height:100px`, 40 words: Chrome and the unwrapped layout show 15 inside the box, the columns engine 5). A hand-written `<div>` around the text on `main` skips the same words, so this is the existing column word skip (#1818; see [the float-after-text fix](../recent-fixes/2026-10-20-a-float-after-text-moves-the-text-beside-it.md)), not a new defect. Keeping the old path means text-only content that was correct before stays correct; a fix needs overflow columns (css-multicol-1 §3.3) or an overflow policy for a continuation, in the columns engine.

**Not claimed:** that the wrapper and a hand-written `<div>` give the same words in every fixed-height scroll container. They do not, which is why this case is left out of the wrapper.
