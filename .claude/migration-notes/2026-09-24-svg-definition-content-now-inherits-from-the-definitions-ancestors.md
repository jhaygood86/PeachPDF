# SVG pattern/marker/mask/clipPath content now inherits from the definition's ancestors

**Before:** unstyled content inside `<pattern>`, `<marker>`, `<mask>` and `<clipPath>` always started from
the initial values, whatever surrounded the definition. `<svg fill="#fff">` (or a `<g fill>`/`<defs fill>`
around the definition) did not reach it: a pattern's unstyled `<rect>` painted black, and a `<mask>` whose
content relied on an inherited `fill="white"` painted black and hid the masked element. Font-relative
lengths (`em`/`ex`/`ch`/…/`rem`) in that content, and in `userSpaceOnUse` gradient coordinates, resolved
against the initial 16px font whatever `font-size` the definition sat under.

**Now:** that content inherits from the definition element and its ancestors, like any other element —
`fill`, `stroke`, `stroke-width`, opacities, `font-*` — never from the element that references it.
Documents that put paint on an ancestor and relied on definition content *not* seeing it will now render
differently (usually the way a browser does). Documents that set paint on the content itself are unchanged.

Also: a `%` `userSpaceOnUse` gradient coordinate inside a nested `<svg>` resolves against that `<svg>`'s
viewport rather than the root's.

Confirmed against `v0.9.19`: the same code built definition content from initial values there (the
limitation was not yet documented in `docs/supported-svg-features.md` at that tag).
