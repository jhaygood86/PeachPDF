# A stretched replaced grid item's ratio width is clamped to its grid area

An explicit `align-self: stretch` on a replaced grid item takes the item's width from the stretched height
through its aspect ratio. Browsers let that width overflow the grid area unless the page sets a `max-width`;
PeachPDF clamps it to the area. Measured against headless Chrome (pt, 100pt track, 80pt row): a 96x48 image
is 160 x 80 in Chrome and 100 x 80 here; with `max-width: 100%` both give 100 x 80.

**The clamp is deliberate.** It matches Chrome on a page with the very common `img { max-width: 100% }` reset
(which is where the maintainer's 99.8pt figure comes from) and matches `main`. It is not done by honouring
`max-width`: a percentage `max-width` or `width` on a grid item resolves against the grid **container** in
PeachPDF, not the item's grid **area** as the spec requires, so `max-width: 100%` cannot clamp it. The same
percentage-basis problem is why `width: 100%`/`50%` images in a grid were already wrong on `main`. An
unclamped version was tried in this change and gave 160pt (Chrome's plain result), but 160pt where a page
that has the reset expects 100pt, so it was reverted until the percentage basis is fixed.

Filed as [issue #1676](https://github.com/jhaygood86/PeachPDF/issues/1676).
