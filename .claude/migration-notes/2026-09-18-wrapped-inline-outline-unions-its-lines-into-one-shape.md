# Patterned and bevelled outline styles follow a wrapped inline's merged shape

Previously, an `outline` that wrapped across several lines drew its merged shape
only for the styles decided by filled area alone (`solid`, `double`, `auto`);
the patterned pair (`dotted`, `dashed`) and the bevelled four (`inset`,
`outset`, `groove`, `ridge`) fell back to one closed ring per line.

Every `outline-style` now follows the merged shape. `dotted` and `dashed` fit
their pattern along each straight edge of the merged contour, so a dash lands
on every corner it turns, including the concave ones a merge introduces;
`groove`, `ridge`, `inset` and `outset` take each edge's light or dark face from
the direction that edge runs in rather than from which side of a box it is —
the only rule still well defined once the lines have merged and a "side" no
longer is. Both match Chromium, which paints all of these styles over the same
merged region.
