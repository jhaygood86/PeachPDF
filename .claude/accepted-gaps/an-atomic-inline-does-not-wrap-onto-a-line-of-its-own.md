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

`inline-table`, `inline-grid`, and `inline-flex` shared this fix's own `FlowAtomicBlockContentChild`/
`FlowInlineFlexChild` placement code but never ran ANY fit check before it, so they stayed on an
overflowing line unconditionally even after the `inline-block` case above was fixed. They now share
the same `FitAtomicInlineOnLine` preflight-and-`OpenNextLine` helper `inline-block` uses, closing the
"engines... also settle their final used width" half of this gap's original "what remains" note: a
declared, non-percentage `width` is used as the fit-check estimate as-is, and an auto width uses the
box's own (issue #1032-corrected) max-content width bounded by the containing block - CSS Flexbox 1
§9.2's shrink-to-fit main size for `inline-flex`, the same shrink-to-fit idea CSS 2.1 §10.3.9 already
gives `inline-block`'s own auto-width case. The estimate is side-effect-free and discarded either way:
each engine still settles its own real used width independently once its own layout (column/track
algorithm, or the flex algorithm) actually runs.

## What remains

The approximated `inline-block` path whose content fits on one line is still flowed into the parent's
line word-by-word. It can therefore discover that the box's declared width does not fit only after
placing some of its content, instead of preflighting and moving the opaque box as a unit. Closing this
needs the same side-effect-free used-width preflight the paths above now get, applied to this
approximated path too (or a safe re-layout at the new line), not a post-layout translation that would
preserve line breaking and fragmentation decisions made at the old position.
