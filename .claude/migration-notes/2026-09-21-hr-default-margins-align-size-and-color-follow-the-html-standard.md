# `<hr>`: 0.5em margins and centring by default, `align` and `size` as the HTML Standard maps them, `color`/`noshade` fill the rule

Four changes to a horizontal rule that a document author could see. All are new relative to v0.9.19, where
`CssBox.CollapsedMarginBefore` still substituted `1.1em` for a near-zero margin before a rule, the UA sheet had
no `hr` margin, `DomParser` mapped `align` to `text-align` and `size` to `height: <size>`, and `color`/`noshade`
set only the border colour.

## Margins and centring

A rule now has `0.5em` above and below (the HTML Standard's `margin-block: 0.5em`) and `auto` on the left and
right. Before, a rule got 1.1em above it only when a preceding sibling collapsed to (near) zero, and nothing
below it. So:

- spacing around a rule is 0.5em on both sides - a rule after a paragraph collapses against the paragraph's
  margin as usual, but a rule before one now has space where it had none;
- a rule narrower than its container (`<hr width="50%">`, `hr { width: 200px }`) is **centred**, where it sat
  at the left edge;
- `hr { margin: 0 }`, `margin-block: 0` and any margin small enough to fall under 0.1pt are **honoured**. They
  used to be replaced with 1.1em after the cascade had decided, so an author's reset was silently discarded.
  A stylesheet that declared `margin: 0` on a rule and was drawn with a gap above it is now drawn flush.

## `align`

`<hr align=left|center|right>` moves the rule: `left` is `margin-left: 0`, `right` is `margin-left: auto`, `center`
is both `auto`. It used to map to `text-align`, which does nothing to a rule, so every value left it at the left.
The value must match exactly (ASCII case-insensitively, no padding), and an author `margin` still overrides it.

## `size`

`size` is the rule's **total** height, so `<hr size="3">` is now 3px tall (it was 5px: the `height: 3px` content
box plus the two 1px borders). `size="1"` is a single 1px line (it was a two-tone 2px rule), which is the "hairline"
that legacy and email markup means by it. A `size` that is empty, valueless, negative, zero or not a number
behaves like `size="1"`, as in Chrome.

## `color` and `noshade` fill the rule

`<hr size="10" color="red">` and `<hr size="10" noshade>` are now a solid bar. They used to be two coloured lines
with the page showing through the 8px between them, because only the border took the colour.
