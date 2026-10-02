# Border widths are used as written, not snapped to whole device pixels

PeachPDF paints a border at the width the author gave: `0.5px` is 0.375pt wide, `1.5px` is 1.125pt wide.
CSS Values 4's "snap a length as a border width" rounds a width above 0 and below one device pixel **up** to one
device pixel, and a wider one **down** to a whole number of device pixels, so a browser draws `0.5px` as a
visible 1px line and `1.5px` as 1px. That rule changes the *used* border width, and so the size of the box, not
just where its edge is painted. Tracked in #1584.

**Why it is a gap and not part of
[`SnapBoxDecorationsToCssPixels`](../../docs/usage-examples.md#crisp-1px-borders-at-100-zoom):** that option
snaps the *positions* of a box's edges at paint time, which is what Chromium does (`ToPixelSnappedRect` rounds
the left and right edge of a fractional layout rect on their own). It never touches a width, and the border is
still drawn at its true width inside the snapped outer edge, which is also why the overflow clip is the snapped
border box inset by the border's own widths. Snapping widths would change the box size of every element with a
fractional border width, so it belongs in the layout engine, as its own change with a migration note.

**The open question** is what a "device pixel" is: a PDF has no fixed pixel grid, and it depends on the viewer's
zoom. The likely answers are the CSS pixel (1px = 0.75pt), or an opt-in like the position snapping.
