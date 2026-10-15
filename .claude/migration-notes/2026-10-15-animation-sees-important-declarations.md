# A CSS animation sees `!important` custom properties

With `PdfGenerateConfig.AnimationProgress` set, a `var()` in an animated property or in a keyframe now sees a custom property declared
`!important`. `#a { --w: 100px; width: var(--w) }` plus `#a { --w: 300px !important }` with `@keyframes { from { width: 50px } }` gave a
width of 75px at the midpoint (the `100px` the normal declaration says); it gives 175px, as a browser does. This holds for author and inline
declarations, for the custom property on the element or an ancestor, for a custom property that refers to another, and for `@layer`ed
`!important` declarations (the earliest layer wins, as in the cascade).

Before this change (`git show v0.9.22:docs/html-css-support.md`) the docs listed `var()` against an `!important` custom property as not seen.
The related `currentcolor` behaviour (an `!important` `color`, a `color` set by a later animation) is part of the unreleased `currentcolor`
mixing and is described in `2026-10-14-currentcolor-mixes-in-animations.md`.
