# SVG filter `BackgroundImage`: isolation groups inside the SVG are not modelled

`in="BackgroundImage"`/`"BackgroundAlpha"` is painted as the page behind an inline `<svg>` (over white paper, up to the nearest
HTML backdrop root) plus the SVG's own content painted before the filtered element. Three parts of the isolation-group rule are not
modelled (tracked as #1323):

1. **An isolating ancestor inside the SVG** (`opacity < 1`, `mask` or `filter` on a `<g>`/ancestor). The group's children are painted
   into a tile that does not carry the repaint state, so a filtered element inside it sees an *empty* backdrop instead of the group's
   own earlier children. Measured behaviour, pinned by `BackgroundImage_InsideAnOpacityGroup_StartsEmpty`.
2. **`clip-path` on an ancestor** creates an isolation group per spec but is only a clip here, so the backdrop also holds content
   painted outside that group.
3. **A shared element instantiated more than once (`<use>`).** The repaint stops at the first painted *object*, so a second instance
   sees only what preceded the first.

Why it was not done: the repaint stops at a scene-graph element, not at a position in paint order, and a group's offscreen tile is a
separate graphics. Fixing (1)/(2) means replaying the group's children up to the filtered element inside its own tile; (3) needs the stop
to name a painted instance.

What was tried and is fine: page content behind an inline SVG (transformed ancestors included: the page is repainted in device space and
the bitmap covers the SVG's user-space region), `<svg style="background:...">`, viewBox scaling, an isolating HTML ancestor (the repaint
then starts at it, over transparency), and a chain of filters each reading the backdrop (nesting capped at 3).
