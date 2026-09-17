# Absolute `top`/`left` now anchor at the containing block's padding edge

## Before

An absolutely positioned box's `top` and `left` were measured from its containing block's **content**
edge. A positioned ancestor with padding therefore pushed every `top`/`left`-anchored descendant down
and across by that ancestor's `padding-top`/`padding-left`, on top of the offsets the author asked
for. `right` and `bottom` were already measured from the padding edge, so the same box landed in two
different places depending on which pair of offsets placed it.

## Now

All four offsets are measured from the padding edge, which is the containing block
([CSS 2.1 §10.1](https://www.w3.org/TR/CSS21/visudet.html#containing-block-details)), and match
Chrome.

## What a document author will notice

Only documents with **padding on a positioned ancestor** change; with `padding: 0` the padding edge
and the content edge are the same point and nothing moves.

Where they do change, a `top`/`left`-anchored box moves **up and left** by that ancestor's padding —
to where a browser has always drawn it. A layout that was hand-tuned against the old behaviour (for
example by subtracting the ancestor's padding from the box's own `top`) will now be off by that
padding in the other direction; remove the compensation.

The most likely shape to notice is an overlay pinned inside a padded container — a badge, a
watermark, a chart's goal-line or axis label — which previously sat one padding inside its intended
corner.
