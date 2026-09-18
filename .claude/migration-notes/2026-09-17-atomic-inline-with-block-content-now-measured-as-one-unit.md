# An inline-block/-table/-grid/-flex holding block-level content now sizes shrink-to-fit boxes correctly

`CssBox.GetMinMaxWidth`'s intrinsic (min-content/max-content) walk — what sizes a float, a `position:
absolute` box, and an auto table column — previously undercounted an atomic inline-level box
(`inline-block`, `inline-table`, `inline-grid`, or a column-direction `inline-flex`) whenever a
block-level descendant sat inside it. A block-level child reset the walk's single running total, and
the walk restored it afterward by taking the wider of the two instead of adding them back together —
discarding whatever inline content preceded the atomic box on its line.

A document with a badge/card built as `text <span class="badge"><div>…</div></span>` inside a float, an
auto table column, or an absolutely-positioned auto-width box previously had that shrink-to-fit
container sized too narrow — narrow enough to overlap the badge's own content — and, separately, an
atomic box whose block content declared its own `padding-left`/`padding-right` could have that padding
counted on top of an already-discarded (too-narrow) content measurement. Both cases now measure
correctly: the atomic box is measured in isolation (via its own top-level intrinsic-width call, the same
treatment a flex row already gives each of its items) and its result is added onto the line it sits on,
with its own decoration counted exactly once.

No markup or CSS property changed meaning — this only corrects the measured width fed into shrink-to-fit
sizing, which can make previously-too-narrow floats, table columns, and absolutely-positioned boxes wider
now that they correctly hold their content without overlapping it.
