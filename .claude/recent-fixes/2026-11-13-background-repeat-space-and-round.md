# `background-repeat: space` and `round` painted as `repeat`

`BackgroundImageDrawHandler` switched on the normalized repeat keyword (`no-repeat`, `repeat-x`, `repeat-y`, else `repeat`), so `space` and `round`, alone or in a pair, drew plain tiles with no gaps and no resizing (css-backgrounds `background-repeat-space-*`, `-round-*`).

Now the repeat is read as a horizontal and a vertical value. `round` resizes the tile so a whole number (at least one) fills the positioning area, and rescales the other axis to match when its size is `auto` and it does not round itself (`RoundTiles`). `space` places as many whole tiles as fit and shares the leftover between them, from the positioning area's start; with room for one it is drawn where `background-position` puts it (`AxisPositions`). Every other combination keeps its old paint path.

css-backgrounds 330 -> 344; `BackgroundRepeatTests`.

## Not done
The remaining `background-repeat-round-1*` differ by at most 4 levels (resampling of the rescaled tile); `space-6`/`-7`, the `.xht` pair and `gradient-repeat-spaced-with-borders` (borders and a `background-clip` interplay) were not looked at. `border-image-repeat: space` is separate code and untouched.
