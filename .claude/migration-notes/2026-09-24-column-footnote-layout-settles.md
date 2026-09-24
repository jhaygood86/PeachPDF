# Documents with many column-scoped footnotes now settle instead of stopping on the pass cap

**Before:** a multi-column container with a `float-reference: column` footnote on many of its lines could keep moving
its note areas between passes until the layout loop's cap ended it, so the note areas described whichever state it
stopped in.

**Now:** once the layout revisits a state, each column keeps the largest note reservation it needed in that cycle and
the layout settles. A column that lost a call to its neighbour can keep a blank strip at its foot. Documents that
already settled are laid out exactly as before.
