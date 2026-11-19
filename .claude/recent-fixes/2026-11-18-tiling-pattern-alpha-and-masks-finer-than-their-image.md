# Tiling pattern alpha, and stencil masks finer than their image

Two PDFium corpus fixes (bug_1161, bug_1396266, bug_1976; corpus 146 -> 149 of 207, exact fetch).

**Tiling pattern painted at /ca below 1.** The cell was drawn opaque. It now starts at the fill's alpha (`TilingBrush(..., alpha)`, part of
the tile cache key), so a cell with no alpha of its own is as faint as the fill while a cell that sets its own `/ca` overrides it. This is
PDFium's behavior and both corpus cases need it: bug_1161 (cell paints black, fill ca 0.2, expected 20% gray) and bug_1693 (fill ca 0.5, but
the cell sets `/ca 1` and draws an image, expected fully opaque). **First attempt, discarded:** drawing the whole tile into a group at the fill
alpha fixed 1161 and broke 1693 (3.9% -> 75% differing), the clue that alpha is an initial state of the cell, not a composite of the tile.

**An image under a mask with more pixels than itself.** `ApplyAlpha` sampled the mask down onto the image's grid, so a 3x3 image under a 64x64
stencil became 3x3 coarse blocks (bug_1396266, bug_1976). The image is now repeated onto the mask's grid first (capped at 16M pixels) and the
mask applied there, so the mask decides the edges. A coarser mask is still sampled up onto the image.

**Tried and reverted: smoothing every image drawn smaller than its pixels.** It changed nothing for bug_1396266 (which magnifies, not shrinks) and
took bug_1693 from 3.9% to 75% differing, so no evidence supports it as a default.

Still failing nearby: bug_40721524 (a Type 3 glyph whose stencil is painted with a shading pattern draws black: a stencil image ignores a
pattern fill).
