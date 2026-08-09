# `inline-block` lines near a page or column boundary now move whole

Previously: an `inline-block` element (e.g. a padded `<button>`) whose vertical padding/border pushed its
text past a page or column boundary could have that boundary decided using a position that hadn't yet had
the padding applied, then relocated word-by-word after the fact. In a fragmented layout (multi-column
content, or content nested inside a table cell) this could leave part of a line's words on one page/column
and the rest effectively orphaned relative to the line-level break the rest of the document already
honors, rather than moving the whole line together.

Now: the padding/border inset is applied before the boundary decision is made, so an `inline-block`'s line
that doesn't fit moves to the next page or column as a whole - consistent with how every other line in the
document already breaks (CSS Fragmentation Module Level 3 §4.1: a line box is a monolithic break unit).
This only changes rendering for content within a padding/border's width of a fragmentation boundary;
ordinary `inline-block` layout away from a boundary is unaffected.
