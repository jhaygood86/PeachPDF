# A wrapping column flex container has no break points between the items within a line

`CssLayoutEngineFlex.BuildLineGroups` hands `LineRelocation` **one** group for a container whose cross axis
is the inline axis — `flex-direction: column`/`column-reverse`. That is right for the break points *between
lines*: those lines sit side by side sharing one block-axis range, so no fragmentainer boundary falls
between two of them and a value declared there names nothing (this is half of what
[issue #448](https://github.com/jhaygood86/PeachPDF/issues/448) asked for). It also keeps the geometric
half: a line holding something that may not be cut still moves, and every line moves with it, so they stay
level rather than one sliding down alone.

What it leaves undone is the break points such a container *does* have. Its items are stacked along the
block axis **within** a line, so §3.1's class-A break points there are between item and item — which
css-flexbox-1 §11 states directly ("flex containers can break across pages between items"). So a
`break-before: page` on the second item of a line is inert, a `break-after` between two items is inert, and
`break-inside: avoid` on one item moves the whole container's content rather than that item and the ones
after it.

Inert is the deliberate answer, and it took a correction to get there. The arm first handed the predicate
the whole of `flowLines[0]` as the later-in-flow side of the boundary above the container, which reads
`break-before` off *every* item of the first line — so a `break-before: page` on the second item forced the
container's entire content onto the next page, carrying the first item, which sits **before** that break
point, along with it. §3.1 requires content before a forced break to stay in the earlier fragmentainer, so
that was a worse deviation than the gap it was covering. Only the container's first in-flow child speaks
for that boundary now.

Left out because it is a different unit of work, not a refinement: the thing that moves stops being "a
line" and becomes "a run of items inside a line", and two side-by-side lines each have their own sequence
of block-axis break points that must not disturb one another — while `LineRelocation`'s running
displacement is one number for one chain. Grid is unaffected: even under `grid-auto-flow: column` the rows
are still the block-axis unit.

Filed as [issue #455](https://github.com/jhaygood86/PeachPDF/issues/455).
