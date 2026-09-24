# A float that does not fit below the content in its column overflows it (#1341)

**Gap:** a float in a multi-column container is placed in the column its anchor falls in (css-multicol-1
§2; see [the #1203 fix](../recent-fixes/2026-09-24-multicol-floats-are-positioned-in-their-column.md)). When
no room is left at the foot of that column it stays there and extends below the balanced column height,
rather than moving to the top of the next column (CSS 2.1 §9.5.1 rules 1 and 3, applied per column box).
The container's auto height grows to contain it, but `EstimateBalancedColumnHeight` and the packing
simulation do not count floats, so balancing ignores the space one takes.

**Why out of scope:** moving a float to a later column is float fragmentation - a float must be able to
be carried to, or continue in, a later fragmentainer - which is the substance of #317 and #1201 and needs
the resumption machinery those describe. Counting a float in the balance would be wrong without it: the
estimate would reserve room for a box the fill then relocates.
