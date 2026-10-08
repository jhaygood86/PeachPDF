# `<iframe>` and `<video>` grid items are stretched instead of keeping their natural size

css-grid-2 §6.2: `justify-self`/`align-self: normal` stretches an item only if it has no natural size and
no aspect ratio. Browsers give `<iframe>` and `<video>` a natural size (300x150px) and keep it. PeachPDF
stretches them across the grid area. Measured against headless Chrome (pt, 100pt tracks, 80pt row):
`<iframe>` is 228 x 115.5 in Chrome and 100 x 80 here, `<video>` with no poster 225 x 112.5 and 100 x 80.

Images and inline svgs do keep their natural size in a grid
([recent fix](../recent-fixes/2026-10-08-inline-level-flex-and-grid-items-are-blockified.md)), so a grid of
mixed replaced items now treats `<img>` and `<iframe>` differently. Not new: both were stretched on
`main`. `<object>` resolving to an image and `<canvas>` were not measured here (`<canvas>` is unsupported).

**Why it was left.** Out of scope for the image/svg work; the likely fix is the same
`GetReplacedNaturalSizeAsync` treatment for these box kinds.

Filed as [issue #1675](https://github.com/jhaygood86/PeachPDF/issues/1675).
