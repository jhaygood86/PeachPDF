# A float read live under several fragments of its container was drawn by each (tall-float corpus seeds 30, 39, 147, 17)

A multi-column container nested in an outer column can have a fragment in each outer column it reaches. When none of its inner columns holds a
float (a float taller than the page that the columns engine does not carry), the emitter reads the float live under every
fragment of the container it walks, and each fragment claims the float's words by its own region: with a float 507pt tall in a column
43pt wide, at the foot of one column and the top of the next, both did, at the same position. The float is one box with one position;
its words were in two fragments.

Fix: a word of a float read live (no captured instance; the walk's `capture` is null) is claimed once per slot walk, by the first
fragment that claims it (`_liveFloatWordsThisWalk`, cleared before each root walk). Deduplicating at the visit instead (draw the float
under the first fragment that reaches it) lost the float where that fragment is not the one whose region contains it (seed 276), and
skipping the live read for floats under a capture lost them wherever no fragment held them (`FloatWiderThanItsColumn_InNestedColumns_KeepsEveryWord`
failed): the claim is the place to decide.

Evidence: ordinary corpus unchanged (3 lost, 0 doubled); tall-float corpus doubled words 7 to 2 (seeds 30, 39, 147 to 0) and seed 17 loses
one word fewer; net8.0 suite green on Windows and Linux (Release); the new test fails on main.

Not done: seed 204 of the tall corpus still draws two words twice (a different path: it is not read live), and seed 212's nine lost words (a line
after a 316pt float inside a 22pt column) need the float's continuation to have a position of its own in the next column.