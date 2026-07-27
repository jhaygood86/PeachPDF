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

Two corollaries that a future change can plausibly get wrong:

- **A container whose lines share one block-axis range has no break points between them.** A wrapping
  `flex-direction: column` container stacks its lines along the *inline* axis; a value declared at that
  point names no boundary. Acting on it moves one line down the page alone, out of line with the lines
  beside it, which is what the pass used to do.
- **A flow-order neighbour is not necessarily a geometric neighbour, but the reverse holds too**: the
  first group down the page is only entitled to read its own `break-before` as "the break point before
  the container's first child" when it really is the first child in flow.
