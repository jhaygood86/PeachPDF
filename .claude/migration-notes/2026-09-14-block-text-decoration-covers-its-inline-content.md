# A block's text decoration now covers its inline content, not its full width

A `text-decoration` on a block-level box — a `display: block` link, a `<div>` with
`text-decoration: underline`, an `<h1>` under a UA rule — used to be drawn across the box's own
decoration area, so the line ran the full content width of the block whatever the text on it was. A
table-of-contents entry styled `a { display: block; }` underlined the whole line to the right page
margin.

The line is now drawn over the inline content it decorates, one span per line box, per
[css-text-decor-3 §2.4](https://www.w3.org/TR/css-text-decor-3/#line-decoration): a block container's
decoration propagates to the anonymous inline box wrapping its in-flow inline-level content, and
through its in-flow block-level descendants to theirs. Consequences a document author can see:

- A block wider than its text is underlined only under the text, and a wrapped block draws one line
  per line box, each as wide as that line, instead of one line across the whole block.
- The span follows `text-align`, so centered and right-aligned blocks are decorated where their text
  actually sits.
- Floated and absolutely positioned descendants are not decorated, and no line is drawn for a block
  with no inline content at all (previously a full-width line was).
- The block's own padding no longer displaces the line: an underlined block with horizontal padding
  starts its line at its text, not one padding's width further in.

Inline boxes are unaffected — they were already decorated per line box.
