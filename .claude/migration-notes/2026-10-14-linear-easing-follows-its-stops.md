# `animation-timing-function: linear(...)` follows its stops

With `PdfGenerateConfig.AnimationProgress` set, an easing written as `linear(<stops>)` used to be treated as plain `linear`, so
`linear(0, 0.8 25%, 1)` sampled 0.5 of an interval at 0.5. It now follows the piecewise-linear curve through its stops
(CSS Easing 2 §2.1), as a browser does: that interval samples about 0.87 at 0.5. A curve whose stops lie on the straight line
renders as before; one with a plateau, a jump or an overshoot renders differently. The curve is continued past its first and last
stop, so a value can leave 0 to 1, within what the animated property allows.

Before this change (`git show v0.9.22:docs/html-css-support.md`) the docs said "`linear()` with stops is treated as `linear`".
