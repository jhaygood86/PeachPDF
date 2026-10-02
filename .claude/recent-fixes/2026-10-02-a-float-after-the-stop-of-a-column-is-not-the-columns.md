# A float after the point a column's flow stopped was drawn by the column that left it (doubled-float family)

A multi-column fill captures each column's geometry from the boxes the column holds and leaves out what lies *beyond* it, read off the
break record. For a column whose inline flow stopped mid-block, the walk stopped at the inline record: its words are marked per
word, but the flow's floats that come after the stop are not words, and they were left in the column's snapshot. They were never
placed by that fill: they hold the measurement pass's geometry, and then the geometry the next column laid them out with. A float
wider than the column puts that over the first column too, so the emitter walked the box under the held parent, read the box's
live geometry (the later column's) and claimed it: the float's words were in two fragments, the first column's with the second
column's position.

Two changes:

- `AddFloatsAfter`: the floats of the stopped flow whose ordinal is past the resume ordinal are beyond the column (ordinals
  count words, spaces included; a float has the ordinal of the word after it, so one at the resume ordinal was placed on a line
  the flow kept or discarded and is not added);
- the emitter's ordinary child walk skips a float a column's snapshot does not hold (`ChildrenOf`), the same safety net the
  nested-container path has: the box is read through the snapshot, and a box the snapshot lacks has only a later column's geometry.

The overflow fallback's participation test (`HoldsOrDescendsFromAHeld`) had to follow: a float must be held itself, and nothing
inside one that is not is reached. With the walk skipping such a float but the test still counting the column as a participant,
the compact `t1` lost its float (nobody claimed it: the column that had been claiming it by live geometry was skipped, and the
holder was told someone else would).

Reductions of seed 112: `d1b` (a 122pt float holding two words, after the text of an outer column) and `d2` (also fixed by the
ordinal rule alone).

Evidence: net8.0 suite green; ordinary corpus (300) unchanged (3 lost, 0 doubled); tall-float corpus 14 lost and 7 doubled as
before except seed 17, one word fewer lost; the new test fails on main.

Not done: tall-float seeds 39, 204, 30 and 147 still draw a word twice (7 in all). They are not this case: the changes here do not move them.