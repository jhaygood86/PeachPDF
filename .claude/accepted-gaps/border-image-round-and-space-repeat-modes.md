# border-image-repeat: round degrades to repeat, and space is not a recognized keyword

Tracked as **#1057**. Deliberate v1 scope decisions made when `border-image` (CSS Backgrounds and
Borders Module Level 3 §13) painting was first implemented, not defects found afterward.

## `round` degrades to `repeat`

Per spec, `round` resizes each tile so an integer number of them fit exactly across the edge/center,
with no partial tile at the boundary. `BorderImageDrawHandler.DrawEdge`/`DrawMiddle` instead tile
edge-to-edge at the source slice's own natural aspect (scaled to the edge's fixed cross-axis
thickness) and rely on the pushed clip (`g.PushClip(dest)`) to cut off a partial final tile - which is
exactly what `repeat` does. This mirrors a pre-existing, already-shipped simplification in this
engine's own `background-repeat: round` handling (`BackgroundImageDrawHandler.DrawBackgroundImage`'s
`switch` has no `"round"` case of its own; it falls into the same `default` branch `repeat`/anything
else not `no-repeat`/`repeat-x`/`repeat-y` uses) - `round`'s own even-fit resizing was never
implemented there either, so border-image's identical shortcut keeps the two properties' behavior
consistent with each other rather than introducing a new inconsistency between them.

## `space` is not a recognized keyword

`BorderRepeat` (`src/PeachPDF/CSS/Enumerations/BorderRepeat.cs`) only has `Stretch`/`Repeat`/`Round`
members, and `Map.BorderRepeatModes` (`src/PeachPDF/CSS/Model/Map.cs`) has no `"space"` entry - a
pre-existing gap in the CSS-OM layer, not introduced by the painting work, but directly relevant to
it: `border-image-repeat: space` (or `space` as either component of the two-value form) fails to
parse as a valid value for the property, so the whole declaration is dropped (falling back to the
inherited/initial value, `stretch`) rather than reaching `BorderImageLayerResolver.ResolveRepeat` at
all. That resolver degrades an unrecognized keyword to `BorderRepeat.Stretch` defensively (see its own
remarks), but in practice never sees `"space"` in the first place - the CSS-OM rejection happens
first.

## Why out of scope for the initial border-image painting change

Real `round`/`space` tiling math - resizing tiles to fit an edge evenly, or inserting even gaps
between them - is a distinct, self-contained piece of work from the 9-slice painting algorithm itself
(corners, edges, center, slice/width/outset resolution, source-image resolution including gradients/
SVG), and the `round`-degrades-to-`repeat` half of this gap already has engine precedent to stay
consistent with. `stretch` and `repeat` - the two most commonly authored values - are both fully,
correctly implemented.
