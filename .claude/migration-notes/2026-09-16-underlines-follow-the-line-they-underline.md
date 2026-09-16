# Underlines follow the line they underline, on a line that mixes font sizes

## What changed

An automatically positioned underline is now placed from the alphabetic baseline of the line it
decorates. It used to be placed from the top edge of that line's content plus an offset measured in
the decorating element's font — the same thing whenever the line is set in one font, and a different
thing as soon as it is not.

So a decorated block, cell or inline box whose line carries text in **more than one size** — a small
label beside a large figure, a heading with a smaller note after it, a total in a table cell — had its
underline drawn a whole ascent-difference away from the text: through the middle of the glyphs where
the decorating element's font was the smaller one, and floating below the line where it was the
larger. Both now sit under the text, one CSS pixel below the baseline, like the single-size case
always did.

A line set entirely in the decorating element's own font is **unchanged**, down to the last
coordinate. If your documents do not mix font sizes inside a `text-decoration: underline`, nothing
about their output moves.

## Also worth knowing

- A superscript or subscript on a decorated line no longer influences where the underline goes. It is
  raised or lowered off the baseline by `vertical-align`, so the underline follows the rest of the
  line instead of splitting the difference.
- An underline inside a table cell is unaffected by the cell's own `vertical-align`, including the
  `middle` the UA stylesheet gives every cell.

`line-through` and `overline` are not part of this change and still key off the decorated line's
content box.
