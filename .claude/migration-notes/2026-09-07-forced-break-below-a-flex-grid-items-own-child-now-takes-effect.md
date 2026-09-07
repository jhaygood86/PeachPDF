# A forced break below a flex or grid item's own child now takes effect

Previously, `break-before`/`break-after: page` (or `column`) declared on a descendant of a flex or grid
item — not the item itself, but something inside it — could be silently lost. Every earlier layout of
that item is a *measurement* (sizing the item before its final position is known), and a measurement pass
lays the item's content out for real, which could mark the forced break as already taken before the
item's own, final commit layout ever ran; the commit pass never got a second chance to see it.

A flex or grid item's own commit layout now clears that "already taken" state on its whole subtree before
laying it out for real, the same way the multi-column engine's own equivalent transition already did for
its own refill — so a forced break below a flex or grid item's own child now takes effect on the page
where it was declared, the same way it already did for a break declared directly on the item.
