# A multi-column container clears the floats beside it

A `columns` container placed after a float laid its columns under the float, and the line flow pushed lines
inside a column past every column's extent, where no fragmentainer claimed them: the words were lost. Found by
a seeded 300-document corpus (words drawn vs words in the source), not by reading.

## What changed

- `CssLayoutEngineColumns.Layout` asks `CssLayoutEngine.ExtentBesideFloats` (CSS 2.1 §9.5: a formatting context
  root keeps clear of floats) for the room left beside the floats, and lays the columns out in that extent. Only
  when at least 20pt is left and the container still has several columns there (`HasSeveralColumnsAt`); a single
  column is the block flow, which avoids the float by itself.
- The narrowed extent is kept on the box (`ColumnsBesideFloats`) for the pages it continues onto. A
  continuation's top is the page's, where the float is no longer, and `Location.X` is already the shifted one,
  so asking again narrowed twice.
- `DomUtils`' float scans stop at a container that has such an extent: its floats were already accounted for.
- `ExtentBesideFloats` needs a probe `CssLineBox`; its constructor registers it on the owner, so it must be
  removed in a `finally` or every word is emitted twice.

## Emitter

- A word wider than its column spills into the next column's extent and is claimed by the column it starts in
  (`FragmentRegion.YieldsToEarlierColumn`). Only when the earlier column's snapshot also holds the box: a float
  wider than its column overflows *leftward* into the previous column's extent and belongs to the column that
  holds it. The first version of the rule lacked the "holds the box" test and lost those floats' words.
- An absolutely positioned box in a column snapshot is claimed by the page band and emitted by the last column
  that holds it (`HeldByALaterColumn`), so it is drawn once. It is positioned against the containing block's
  *first* fragment (`CssBox.FirstColumnFragmentOrigin`), not the last column's `Location`, which could be off the
  page. The origin is only kept within one page: a box on an earlier, already emitted page cannot be drawn
  into, so an origin from an earlier page is dropped (that case lost the box outright).

## Measured (300-document corpus, vs main)

Words lost 254 -> 163, words doubled 407 -> 161; 20 documents lose fewer words, 4 lose more (112, 119, 151,
235), 32 double fewer, 1 doubles more (61). Each of the five was reduced and compared against main:

- 112, 235: the same reduction loses the same words on main without any narrowing (inline float / float in a
  table-row wrapper wider than a column); the shifted layout only exposed it.
- 119: an absolutely positioned box now sits at the first fragment and runs past the page foot (it is not
  fragmented); before, it landed in the last column by luck.
- 151, 61: a multicol inside a multicol, already garbled on main.

## Not done

Nested multicol (#1525) and fragmenting an absolutely positioned box across pages are separate pieces of work.
