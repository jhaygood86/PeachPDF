# An avoided break is answered *after* the later side has landed, and it moves the earlier side

_CSS Fragmentation Level 3 §3.1/§4.3. Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320)._

A **forced** break at a break point is a statement about where the *later* box goes, and it can be answered
before anything is placed. An **avoided** break is a statement about the *earlier* one, and it cannot: whether
a boundary really falls at a break point is a question about where the box below it ended up. A box the
boundary cuts *through* has taken no break at the point above it at all, and there is nothing there to avoid —
so asking before placement forbids breaks that never happen and misses the ones that do.

That is why a forward walk over break points (`LineRelocation.Walk` over flex lines and grid rows;
`CssBox.PlaceBlockChild` over siblings) has to keep enough state to reach **back** over content it has
already placed, and why avoidance is never merely another argument to the "how far does this move?" function.

Two consequences that have each been got wrong once:

- **The earlier side's move supersedes the later side's, it does not add to it.** The later box moved to open
  the destination fragmentainer; once a keep-with-next run travels, the run's *head* opens it instead. Adding
  the two displacements puts the group one box-height too low.
- **§4.3's ladder is arithmetic over coordinates and says nothing about what a run member is**
  (`EarlyBreak.TravellingRunHead`). A chain of siblings and a chain of flex lines have no structure in
  common, and both relax identically: trim from the *front*, reject a head at or above the content top of the
  fragmentainer being left, reject a run that will not fit the destination band alongside the subject. Write
  a second copy of those two guards and the two chains will relax differently.
