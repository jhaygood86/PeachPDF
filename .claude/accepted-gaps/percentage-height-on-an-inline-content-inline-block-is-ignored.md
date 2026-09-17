# A percentage height/min-height on an inline-content inline-block is ignored

Tracked as **#1167**. Left in place by #1101, which made an *absolute-length* declared
`height`/`min-height` size an inlines-only `inline-block`'s painted box; the exclusion itself is
deliberate, matching the width property's own original scope before #1097.

`CssLayoutEngine.ResolveAtomicInlineDeclaredHeight` declines anything ending in `%`, so such a box is
sized as though `height: auto` were declared.

## Why it stays out

Resolving a percentage height correctly needs to know whether the box's containing block itself has
a definite height — `CssBox.IsHeightCalculated`. That flag is only ever set in the *ancestor's own
layout epilogue* (`CssLayoutEngine.ApplyHeight`), which runs strictly after the ancestor's own
content — including this box, flowed into one of the ancestor's own lines — has already been laid
out. So by the time this box's height would need resolving, its containing block's definiteness
isn't known yet.

This is the height-axis counterpart of the percentage-width gap #1091 originally shipped with,
closed by #1097 by threading the current line's document Y and the child's real containing block
into the resolver — viable there because width is normally stretch-fit and known top-down before
children are placed. Percentage height has no equivalent top-down availability; it is normally
resolved bottom-up, from content, so the same fix shape doesn't carry over directly.

Only the inlines-only case is affected: an inline-block holding block-level content goes through
`FlowAtomicBlockContentChild`, whose `GetBoxHeight`-driven layout does resolve a percentage height
against its real containing block correctly.
