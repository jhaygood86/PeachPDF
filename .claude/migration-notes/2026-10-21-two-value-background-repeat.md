# A two-value `background-repeat` of `repeat no-repeat` / `no-repeat repeat` now tiles one way

`background-repeat: repeat no-repeat` and `no-repeat repeat` were drawn as plain `repeat`: the image tiled in both directions. They now match CSS Backgrounds 3 §3.4 (and `repeat-x` / `repeat-y`): `repeat no-repeat` is a single horizontal strip, `no-repeat repeat` a single vertical one. A pair of equal values (`repeat repeat`, `no-repeat no-repeat`) is that single keyword. Documents that used the two-value form and relied on the (wrong) full tiling now show a strip.
