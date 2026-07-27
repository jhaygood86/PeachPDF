# A break point is identified in flow order and acted on in block-axis order

_CSS Fragmentation Level 3 §3.1. Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320)._

These are two different orders and one list must not be asked to be both.

**Identification is flow order.** §3.1 pairs the earlier sibling's `break-after` with the later
sibling's `break-before`; that pairing is a statement about the source, and it does not change when the
layout puts the two boxes somewhere else.

**Action is block-axis order.** Which box a fragmentainer boundary leaves above it, which boxes have to
follow one that moves, and how a running displacement accumulates are all questions about where the
boxes ended up.

Block flow is the case where the two coincide, which is why the distinction stayed invisible until a
flex container was walked. `flex-wrap: wrap-reverse` reverses the lines' cross offsets *after* they are
assigned (`CssLayoutEngineFlex.DistributeCrossSpace`), so the first line in the source is the last one
down the page — measured on a 200pt page with two full-width 40pt lines and no break values at all, the
first item lands at `y = 180` and the second at `y = 140`. Walking the source order there accumulates a
displacement onto lines physically *above* the one that moved and leaves the ones below untouched, so
the relocated line is drawn over the top of one that stayed put.

`LineGroup` therefore carries the break point above a group as an explicit flow-ordered pair
(`Earlier`/`Later`) while the list of groups is in block-axis order, and under `wrap-reverse` the two
sides swap: at the boundary above a group, the group's **own** items carry the governing `break-after`.
That is the only realizable reading — a boundary can only start the content *below* it on a new page, so
the pair's later-in-flow member cannot be the thing that moves when it is the thing sitting above.

Three corollaries that a future change can plausibly get wrong, each measured:

- **A container whose lines share one block-axis range has no break points between them.** A wrapping
  `flex-direction: column` container stacks its lines along the *inline* axis; a value declared at that
  point names no boundary. Acting on it moves one line down the page alone, out of line with the lines
  beside it, which is what the pass used to do.
- **The break point above the container's topmost content is §3.1's break point before the container's
  first in-flow child — and "first in flow" is not "first down the page".** Under `wrap-reverse` the
  first line in flow is the *last* one down the page, so the group that must read its `break-before` is
  the one at the top. Reading each group's own `break-before` there instead drops the declaration
  entirely: measured on two full-width lines with 40pt of filler, `break-before: page` on the first
  item moved nothing at all, where the same document without `wrap-reverse` moves it.
- **Only the first in-flow child speaks for that break point, not its whole line.** In a column
  container a line is a block-axis stack, so a `break-before` on its *second* item names a boundary
  inside the container. Handing the whole line to the predicate let that value move the container's
  entire content — including the item that sits *before* the break point, which §3.1 requires to stay
  in the earlier fragmentainer. Measured: three 30pt items, `break-before: page` on the second, and all
  three moved a page.
