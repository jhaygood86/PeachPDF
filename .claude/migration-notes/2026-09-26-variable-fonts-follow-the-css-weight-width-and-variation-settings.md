# Variable fonts follow `font-weight`, `font-stretch` and `font-variation-settings`

**Before:** a variable font (one with an `fvar` table) was read as its default design. Text set in it at `font-weight: 700` was the
default weight with a synthetic emboldening stroke, `font-stretch` did nothing to it, and `font-variation-settings` and
`font-optical-sizing` were unknown properties.

**Now:** the weight, width and style of the box select a location in the font's design space, so the glyph outlines, advance widths
(and therefore line breaks and text widths) and font-wide metrics change with it, and no faux bold or italic is added where the axis did
the work. `font-variation-settings: "wght" 650, "wdth" 80` sets axes directly and wins over `font-weight`/`font-stretch`;
`font-optical-sizing: auto` (the initial value) sets an `opsz` axis to the font size in CSS pixels, so a font with that axis now looks
different at different sizes. Both new properties are reset by the `font` shorthand. The PDF embeds each distinct location as its own
static font instance.

A document that uses a variable font (a common case for web fonts downloaded from a font service) will lay out and look different:
text is usually narrower or wider than the synthetic-bold rendering was, and regular text in a font with an `opsz` axis changes shape.
`font-optical-sizing: none` restores the default optical size.

Static fonts are unaffected: nothing in the pipeline changes for a font without an `fvar` table.
