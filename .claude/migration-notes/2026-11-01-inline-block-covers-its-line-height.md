# An inline-block with a declared line-height covers the whole line

Before: a one-line `inline-block` inside text with `line-height` larger than its font painted its
background/border only around the glyphs' content area, vertically centred in the line.

Now: its box is as tall as its line box (the inherited `line-height`), as in browsers, with the glyphs
unmoved. Boxes with `line-height: normal`, or with a declared `height`, are unchanged.
