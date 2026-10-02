# A float wider than its inner column, in nested columns, was drawn nowhere (seed 112)

The fallback #1582 added (`IsOverflowNoColumnClaims`: a word no holder of its box claims goes to the first holder) stood down when the
box was also held by another outer column's fill, so seed 112's float stayed lost. The float (75pt wide, right-floated) lies
in an inner column 22pt wide, so it sticks out to the left of that column's extent: the holder's region, which starts at the
column's own left edge, does not contain its word. The container is filled again under the next outer column, and that fill
holds the float too, with the first fill's geometry. The stand-down was there because the first version of the fallback
drew such a word twice, once per fill.

Fix: ask the fills in the order they were made and let only the first participant claim the word, once nobody has by the walk's own
verdict. Three details that each cost a failure when they were left out:

- a holder's claim counts only if the walk would not withdraw it afterwards (`YieldsToEarlierColumn`,
  `StartsInAnotherInstance`), or the word is dropped between the check and the claim (`d1b`, a reduction of seed 112);
- an instance takes part when it holds the box **or a box it lies in** (`HoldsOrDescendsFromAHeld`): the walk reaches a box
  through a held ancestor and reads its live geometry when the snapshot does not hold it, so a column can claim a word of a
  box it does not hold. Without that the float was claimed by the inner column that held its parent and, by fallback, by
  the next one (`t1`: 'w2_66' twice);
- the open-right region and the same vetoes are applied to every instance asked, as in the walk.

- the box must lie wholly in the band, not merely overlap it. A line across a band's edge is given to one band or the other by the\n  ordinary straddle rule, and a fallback that saw only 'nobody here claims it' claimed it for the band that had just refused it. On\n  Windows the one-pixel metric difference hid it; on Linux CI ABreakBelowTheContainersOwnChild_ClaimsEveryWordExactlyOnce failed\n  (a row at y=199.7 on a 200pt band, claimed by both pages). Reproduced with the suite in Release under WSL.\n\nEvidence: corpus (300 documents, page-edge visibility): lost 4 to 3 and doubled 6 to 0 together with #1586 (seed 112 to 0;
the 3 left are seed 27's words that now lie past the page edge); net8.0 suite green; the lost-word test fails on main.

Not done: two other reductions of seed 112 (`d1b`, `d2`: a float in two columns at different positions) double a word on
main and still do. They are the float laid out twice, which this does not touch.