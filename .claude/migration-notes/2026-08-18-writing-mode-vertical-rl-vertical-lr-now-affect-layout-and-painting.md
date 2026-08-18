# `writing-mode: vertical-rl`/`vertical-lr` now affect layout and painting, not just logical properties

**Landed:** 2026-08-18 — Implement writing-mode correctly (real vertical-rl/vertical-lr layout)
**Doc sections:** docs/html-css-support.md § [writing-mode/text-orientation rows](../../docs/html-css-support.md),
docs/supported-svg-features.md § [Text](../../docs/supported-svg-features.md)

As of the last release (v0.9.12), `writing-mode` (all five values) and `text-orientation` parsed,
cascaded, and inherited correctly, and `writing-mode` correctly drove
[logical box-model property](../../docs/html-css-support.md#logical-box-model-properties) resolution
(e.g. `margin-block-start` under `vertical-rl` already resolved to the physical right edge) — but
**layout and painting were not affected at all**: line-box flow, glyph rotation/orientation, and
table/flex axis interpretation stayed `horizontal-tb`-oriented regardless of the value. A document
that set `writing-mode: vertical-rl` expecting real vertical text got physically identical output to
one that never set it.

`vertical-rl`/`vertical-lr` now get real vertical layout and painting, for both HTML and SVG `<text>`:

- **HTML**: a block box holding plain inline content (text and simple nested inline elements, no block
  children) gets real vertical line flow — lines ("columns") stack along the block axis, text runs
  top-to-bottom within each column, and `text-orientation: mixed` (the default) classifies each
  character by Unicode's Vertical_Orientation property, painting CJK/kana upright and Latin/digits
  rotated 90°. Flexbox and simple `display: table` are writing-mode-aware too (row/column axis
  selection, cell placement). A vertical-writing-mode multi-column container falls back to ordinary
  single-column flow rather than arranging columns along the wrong axis.
- **SVG**: `<text>`/`<tspan>` get the same real vertical pen model and per-character orientation,
  composing with existing per-character `rotate=""`.
- **`sideways-rl`/`sideways-lr`** are unaffected by this change and continue to render as
  `horizontal-tb` in both pipelines.

A document that previously set `writing-mode: vertical-rl`/`vertical-lr` on inline-only content,
Flexbox, or a simple table — whether intentionally (expecting it to eventually work) or incidentally
(e.g. copied from a stylesheet written for a browser, where it silently had no layout effect in
PeachPDF) — will now see its content actually laid out vertically instead of horizontally. This is a
visible rendering change for any such document, not a bug fix to output that was already vertical.

Several real-world document shapes remain out of scope and still render as `horizontal-tb` internally
even under a vertical `writing-mode` — most notably **a vertical box containing block children**
(e.g. multiple `<p>`s, not just inline content), which is the shape a real multi-paragraph vertical
document is most likely to use. See
[`.claude/accepted-gaps/no-vertical-writing-mode-layout.md`](../accepted-gaps/no-vertical-writing-mode-layout.md)
for the full, itemized list of what's still unsupported (floats, hyphenation, bidi reordering inside a
vertical box, collapsed table borders/`<thead>`/`<tfoot>`/`<caption>`/`rowspan`/`colspan`, real
multi-column arrangement, and per-content pagination of a vertical box's own content, which is
monolithic instead) and the tracking issue for each.
