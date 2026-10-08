# An svg with only a width or only a height is stretched in a grid

An svg (inline, or an svg `<img>`) with only a `width` or only a `height` and no `viewBox` has a size in one
axis and no aspect ratio. As a grid item PeachPDF stretches the other axis to its grid area; browsers use
the default object size (300x150px) there. Measured against headless Chrome (pt, 100pt tracks, 80pt row):
width-only is 72 x 112.5 in Chrome and 72 x 80 here; height-only is 225 x 36 in Chrome (overflowing the
track) and 100 x 38.8 here. `justify-self`/`align-self: stretch` on these, `viewBox`-only svgs, width +
`viewBox`, and svgs with both attributes agree with Chrome.

**Why it was left.** Following Chrome by treating such an axis as "has the default size" was tried and made
the height-only case worse (15pt wide), because PeachPDF's generic replaced-element sizing for a missing
dimension does not produce Chrome's 300x150px default. That sizing has to be made right first, and the input
is rare. Not caused by blockifying flex/grid items: a one-sided svg was stretched on `main` as well.

Filed as [issue #1674](https://github.com/jhaygood86/PeachPDF/issues/1674). The user-facing note is in
`docs/html-css-support.md` under Flexbox.
