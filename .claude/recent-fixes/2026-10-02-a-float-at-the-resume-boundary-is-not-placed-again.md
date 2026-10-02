# A float after the last word of a kept line was placed again by the flow that resumed (doubled-float family, #1587 follow-up)

An inline flow that stops at a fragmentainer boundary discards the line it was building and records where the next one resumes
(`InlineBreakToken.ResumeWordIndex`). A float's ordinal in the flow is the number of words before it, so a float that comes right
after the last word of the line the pass *kept* has the ordinal of the first word of the next line, which is the resume
ordinal. The pass that stopped had placed it on the kept line, in the fragmentainer it was leaving; the resumed pass saw
ordinal >= resume ordinal, took it for the next line's, and placed it again in the next fragmentainer. `FloatsOfTheDiscardedLine`
(#1564) covers the opposite case, a float that was on the line that was discarded; it was empty here because the line closing
at that word had already been opened as a new, empty line when the break was taken.

Two documents of seed 112 (`d2`: `columns:3` > `columns:3` > ten words, a 75pt right float holding one word, one more word) drew
the float's word twice, at the foot of one outer column and the top of the next.

Fix: `CssLineBoxCoordinates.FloatsOnTheLastClosedLine` remembers the floats of the line most recently closed; when the flow
stops they are kept on the box (`FloatsKeptAtTheStop`), and the resumed flow takes them as already placed when it reaches one
at the resume ordinal (it still adds them to the inline floats, so later lines narrow around them).

Evidence: net8.0 suite green; corpora (ordinary and tall-float) unchanged apart from the documents this fixes; the new test
fails on main.

Not done: `d1b` (the other reduction of seed 112: a 122pt float holding two words) still doubles a word and has a different
cause; seeds 39, 204, 30 and 147 of the tall-float corpus are not this case either (unchanged by this).