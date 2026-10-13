# A pass that places nothing no longer runs to the 100,000-pass cap

A 7 KB fuzz document took about 15 minutes to render and came out with 2 pages, most of its text missing. A narrow
`inline-block` (40pt) holding a block and text that contains no-break spaces, followed by a `<br>`, makes the body's inline run
resume at the same word on every fragmentainer: each pass places nothing and hands back the same token one slot later.

**Why nothing caught it.** The driver ends a run that "arrives somewhere it has been" by comparing tokens, and every
`BreakToken` carries the slot it resumes in - which advances each pass - so the repeat is never equal. The cap
(`MaxFragmentainers`, 100,000) did end it, 100,001 passes later, and the last-resort monolithic relayout then ran at slot
100,001, which is why the page text was lost, not just slow. (`TableBreakToken` already documents this same trap for collection
references; it never covered the slot.)

**The change.** `IsSameProgress` compares two tokens with the slot index neutralized at every level (block chain, inline run,
table and its unfinished cells); `LayoutDocument` counts consecutive passes whose outgoing token is the same progress as the
one they entered with, and after 16 takes the same last-resort route the exact-repeat check takes. 16 rather than 2 because a
page-selector pattern (`:nth()`, `:left`/`:right`) legitimately makes slots differ for a pass or two with a repeating token.
Flex and grid tokens are not compared (never "the same"), so they keep only the exact checks.

**Result.** The document renders in 1.7 s instead of ~15 minutes, with all of its text on 6 pages (main produced 2, dropping the
rest). Everything else is unchanged: 19 benchmark documents, 199 other fuzz documents and 220 showcase PDFs are identical.

**Not done.** Why the pass places nothing in the first place (the inline-block run after a forced break) is a separate layout
defect; this change only stops it from costing minutes and losing content.
