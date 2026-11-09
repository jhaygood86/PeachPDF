# A word after an inline-block that fills its line wraps to the next line (#2296)

`<div style="width:52pt"><span style="display:inline-block">w9004_1</span> w9004_2</div>` left `w9004_2` on the first line,
past the block's right edge (x=80 against an edge at 72). Chrome wraps it. It happened when the inline-block was wider than
its block, or as wide as it (the 44pt and 52pt cases, not 60pt), and a large share of the documents with text past the page
edge in the generated corpus were this.

**Cause, found by tracing the wrap decision.** An inline-block is flowed as a block of its own, so the outer line holds its
*rectangle* and none of its words (`Line.Words` is empty; `IsAtLineStart` is already false because of the rectangle). The wrap
decision looks back at the line's last word to ask whether a break is allowed between it and the incoming word
(`HasOrdinaryWrapOpportunityBefore`); with nothing to look back at it answered "no", the word overflowed and stayed. A replaced
element (`<svg>`, `<img>`) is a word of the line, which is why those already wrapped.

**Fix.** When the line has no word but does hold an atomic inline, the answer is "yes" unless the word suppresses a wrap before it
(`LineHoldsOnlyAnAtomicInline`). CSS Text 3 §5.1 treats an atomic inline as an object replacement character, which allows a break
either side of it. A line with a word before the inline-block (`text <inline-block> word`) already wrapped, through that word.

**Not done.** The same lookup for the intrinsic-width walk (the other call of `HasOrdinaryWrapOpportunityBefore`) is unchanged:
not looked at here.

**Evidence.** The three widths of the issue and the control widths; the tests fail without the change for the widths narrower than
the inline-block (checked by reverting it). Corpus of 400 generated documents, `main` against the branch: lost words 19,639 ->
19,461, doubled 7,014 -> 7,031; 85 documents improve and 24 get worse, mostly by a word doubled where a new line changes where a page
breaks. Three documents lose one word `main` drew; one reduced (a heading beside a 450pt float with no room: the mechanism of
#2316, drawn before only because the line above sat a line higher), two reductions did not finish.
