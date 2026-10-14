# `currentcolor` mixes in a CSS animation instead of flipping half way

With `PdfGenerateConfig.AnimationProgress` set, a keyframe colour of `currentcolor` against another colour (`background-color`,
a `border-*-color`, `outline-color`, a shadow's colour, ...) used to show the first keyframe's value until half way through the
interval and the second's after. It now mixes as the box's own `color`: from red to `currentcolor` on a blue-text box is purple
half way. A `color` animated in the same keyframes is followed, so the mix tracks it. Two values that both say `currentcolor`
keep the keyword, as before.

Before this change (`git show v0.9.22:docs/html-css-support.md`) the docs listed `currentcolor` against a color as flipping half way.
