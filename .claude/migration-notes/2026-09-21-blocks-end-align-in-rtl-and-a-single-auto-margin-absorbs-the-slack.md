# Blocks end-align in an rtl container; a single `auto` margin absorbs the slack

Two changes to where an in-flow block sits when it is narrower than its containing block. Both are new
relative to v0.9.19 (`CssBox.CommitBlockChildOffset` placed every block at the content *left* edge plus its
left margin, and `GetActualMarginLeft/Right` returned 0 whenever exactly one margin was `auto`).

## `direction: rtl`

A block-level box narrower than its `direction: rtl` containing block used to sit at the **left** edge; it now
sits at the **right** edge, and an over-constrained box (`width: 100%` plus a margin, or a `min-width` wider than
the container) overflows toward the *left* instead of the right. This is CSS 2.1 §10.3.3: the ignored margin
in an over-constrained box is `margin-left` under `rtl`, not `margin-right`.

A block that fills its container - which is every block with no declared or clamped width - is unchanged, so an
rtl page of ordinary full-width paragraphs renders as before. What moves is anything narrower: a `max-width`
column, a `width: 60%` box, a `margin: 0 auto` block's neighbours. Only in-flow block-level boxes in a
horizontal-tb container move; floats, tables, flex/grid items, absolutely-positioned boxes and vertical writing
modes are untouched.

## One `auto` margin

`margin-left: auto` against a fixed `margin-right` (and the mirror) used to resolve the auto margin to 0, so the
box stayed at the start edge. It now takes whatever the other margin, borders, padding and width leave over, as
§10.3.3 requires: `margin-left: auto; margin-right: 0` pushes a fixed-width block to the end edge. Both
margins `auto` still split the slack evenly, and an `auto` margin never resolves negative.
