# A `double` text decoration now draws two lines

## What changed

`text-decoration-style: double` used to paint a single line, identical to `solid`. It now draws two
strokes of the resolved `text-decoration-thickness`, separated by a gap of the same thickness.

Anything already declaring `double` will change appearance — that is the point, but it is a change:
an accounting-style double rule under a grand total, a `<u style="text-decoration-style: double">`, or
any `text-decoration: underline double` shorthand now occupies about three times the vertical space a
single line did, and takes it from the side away from the text. The first stroke stays exactly where the
single line was; the second is added below it for an underline and a line-through, and above it for an
overline, which is what browsers do.

Because the pair grows away from the text rather than around it, a double underline does not intrude on
the glyphs, but it does sit lower overall. Where a decorated element was tightly spaced against what
follows it, the second stroke is the thing most likely to want a little more room.

A double **overline** grows upward, so it needs roughly two extra line-widths above the text. Flush
against the top of a page there is none, and the upper stroke falls outside the page and is not drawn —
give such a heading a little top margin. (A browser is less forgiving here, not more: Chrome drops the
overline entirely on the same markup.)

`solid`, `dotted` and `dashed` are unchanged.

## Still to come

`wavy` continues to paint as `solid`. It is now documented as such rather than listed as supported
without qualification.
