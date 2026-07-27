# What a nested fragmentainer states about "the next one" must be stated for every link of the record

_CSS Fragmentation Level 3. Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320)._

A break raised below a container's own child produces a **chain**, and every link of it was decided
against the page grid — its slot names a page, its target names a coordinate down the document.
An engine driving fragmentainers of its own restates that record in its own terms; restating only the
outermost link leaves the deeper ones saying what the page grid said, and the next fragmentainer is
then filled at a coordinate several bands away. Measured: the second column of a resumed page began
400pt past the page it was on, and the tail of the flow took a third page it did not need.

The same applies to every question asked *of* the record. "Which boxes did this fragmentainer hold"
is a prefix at **every** level of the chain, not only at the container's own children: the box the
break falls before is not here, the box it falls inside is here up to where it stopped, and every
sibling after the boundary is not here and still carries the measurement pass's geometry. Asking only
the outermost link claimed one box in two columns at once and left ghost fragments a whole virtual
column down the document — both of which the one-line invariant over the fragment tree, **every word
claimed exactly once**, is what catches.

And the one thing that must *not* be restated on a deeper link is the target itself. That box begins
its own containing block's content rather than the fragmentainer, and where that containing block
lands in the next fragmentainer is not known when the record is written — it has not been re-placed
there yet. Drop it, and re-derive it where the placement happens.
