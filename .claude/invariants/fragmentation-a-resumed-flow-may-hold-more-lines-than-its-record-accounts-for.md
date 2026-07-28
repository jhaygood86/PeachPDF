# A resumed flow may hold more lines than its record accounts for

_CSS Fragmentation Level 3 §2. Tracker: [#464](https://github.com/jhaygood86/PeachPDF/issues/464)._

`InlineBreakToken.CompletedLineCount` says how many line boxes a block had produced when the break
was taken, and `CssLayoutEngine.CreateLineBoxes` finalizes from that index on resume. If the block
holds **more** lines than that, those extras are re-finalized, and
`CssLineBox.AssignRectanglesToBoxes` throws `An item with the same key has already been added` —
because the per-line rectangle it is about to add is already on the box.

**A record going stale is not a bug in the record; it is what a retraction leaves behind.** Five
mechanisms retract work a pass has done, and a box can be laid out again by an engine that abandoned
a fill attempt after the record was written. The multi-column engine is the reachable one: a table
inside `column-count: 2` is laid out again over cells that still hold the abandoned attempt's lines
while the record still names the earlier count.

So `CreateLineBoxes` calls `CssBox.DiscardLineBoxesFrom(completedLines)` on every resumed flow. It is
conservative in both directions and that is why it is safe as an unconditional statement rather than
a guarded one: a record naming line *n* was written by the pass that finalized lines 0..*n*−1, so
nothing an earlier fragmentainer emitted can be inside the range it drops.

**This is the guard [PR #481](https://github.com/jhaygood86/PeachPDF/pull/481) built and deliberately
dropped**, because at the time no test could tell it from its own absence — a stale record on a plain
`<div>` does not throw, and "no word is hosted on two lines" fails with the guard in place too. What
made it landable was not a better test but a reachable *case*: moving the monolithic gate off the
table arm made `<div style='column-count:2'><table><tr><td>…</td></tr></table></div>` throw. If this
is ever removed, that markup is the sweep that fails.
