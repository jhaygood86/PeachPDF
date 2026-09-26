# Sweep gradients in colour fonts start at the right angle, and variable colour paints follow the location

**Before:** a `COLR` version 1 colour glyph painted with a sweep (conic) gradient was drawn turned by 180 degrees: the gradient's angles were
read as stored, but the font format stores them with a half turn taken off, so the seam and the colour stops sat on the opposite side from
where the font's author, Chrome and other renderers put them. Separately, the variable paints of a variable colour font (opacities, gradient
geometry and colours, transforms) were drawn at the font's default location whatever `font-weight`, `font-stretch` or
`font-variation-settings` said.

**Now:** sweep gradients start where the font says (the seam of a full-turn gradient is on the positive x axis of the glyph), and a variable
colour font's paints follow the location of the box's font, so a colour glyph can change opacity, gradient and placement with the weight or the
width. A document with a colour font that has sweep gradients, or a variable colour font, renders differently; a non-variable colour font
without sweep gradients renders exactly as before.
