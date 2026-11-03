# `writing-mode: sideways-rl` and `sideways-lr` now lay out vertically

Before, both laid out as `horizontal-tb`. Now they are vertical modes: lines stack from the right (`sideways-rl`) or from the left (`sideways-lr`), every glyph is rotated sideways whatever `text-orientation` says, and `sideways-lr` reads bottom to top (clockwise for `-rl`, counter-clockwise for `-lr`), starting at the bottom edge. SVG `<text>` still treats them as horizontal.
