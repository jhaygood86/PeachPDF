# Some atomic inline paths do not move the whole box onto the next line

An atomic inline-level box that does not fit in what is left of the line must make the browser close
that line and start a new one for it. CSS 2.1
[§9.4.2](https://www.w3.org/TR/CSS21/visuren.html#inline-formatting) makes an atomic inline-level box
one unbreakable unit on the line, which is exactly the case a line break exists for.

Tracked as **#1105**.

## What is fixed

The independent-formatting-context `inline-block` path now preflights its used margin-box width,
shares `OpenNextLine` with ordinary word wrapping, and moves the entire box before laying out its
contents. That covers an inline-block with a block-level descendant and one whose own inline content
must wrap. Its declared content-box `width` now includes its padding and border too. The
`cascade_layers` showcase is the concrete regression: its five cards paint at the browser's 182px
border-box width, wrap as three cards plus two cards rather than overhanging the page, and preserve
their bottom margin between those rows.

## What remains

The approximated `inline-block` path whose content fits on one line is still flowed into the parent's
line word-by-word. It can therefore discover that the box's declared width does not fit only after
placing some of its content, instead of preflighting and moving the opaque box as a unit. The engines
behind `inline-flex`, `inline-grid`, and `inline-table` also settle their final used width while laying
out their own contents, after the parent line would need the answer. Closing the remaining gap needs a
side-effect-free used-width preflight for those paths (or a safe re-layout at the new line), not a
post-layout translation that would preserve line breaking and fragmentation decisions made at the old
position.
