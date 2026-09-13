# `text-align: justify` no longer stretches the line before a `<br>`, and `text-align-last` is now honoured

Three changes a document author can see, all under `text-align: justify`
([css-text-3 §6.1](https://www.w3.org/TR/css-text-3/#text-align-property), §6.3, §6.4.3). Issue #1021.

## The line before a forced break is no longer justified

**Before:** only the block's own last line was exempt. A line ended by a `<br>` — or by a preserved
newline under `white-space: pre`/`pre-wrap`/`pre-line` — was stretched to the full measure like any
soft-wrapped line, so

```html
<div style="text-align: justify; width: 200pt">AA BB CC DD EE FF GG<br>next paragraph</div>
```

put `GG` flush against the right margin with the gaps blown open to reach it. A `<br>`-separated
address block or a stanza of verse came out with every line differently and wrongly spaced.

**Now:** such a line ends a paragraph and is aligned by `text-align-last` (below) instead — start, by
default — exactly as a browser renders it. A line that merely ends at a *page or column* boundary is
unaffected: it is not the end of anything, so it still justifies and the block resumes justified on the
next page.

## `text-align-last` is implemented

**Before:** the declaration parsed and was then ignored — there was no way to ask for anything but the
default treatment of a paragraph's closing line.

**Now:** `text-align-last: auto | start | end | left | right | center | justify` is supported and
inherited. It governs both kinds of paragraph-ending line (the block's last, and the last before a
forced break) and also §6.4.3's *unexpandable* line — a justified line with no justification
opportunity to expand at, such as one holding a single word. The initial `auto` defers to `text-align`,
except under `justify` where it is start; `text-align-last: justify` opts back into stretching those
lines.

`match-parent` is not recognized, and `text-align` remains an independent longhand rather than a
shorthand over `text-align-all`/`text-align-last` — `text-align-all` is not implemented, matching every
shipping browser.

## A justified RTL block's closing lines now flush right

**Before:** under `direction: rtl`, `text-align: justify` left every paragraph-ending line where the
flow had put it, which is the physical *left* edge — reading as a stray outdent at the wrong side of
the column.

**Now:** `auto` under `justify` resolves to *start*, which for RTL is the right edge, so those lines
flush right. `text-align-last`'s own `start`/`end` resolve against `direction` the same way, and the
vertical-writing-mode counterpart resolves against the column's inline-start edge.

The same correction reaches two neighbouring shapes in an RTL justified block, both of which used to be
left wherever the flow put them (the physical left edge):

- a line with no justification opportunity to expand at (css-text-3 §6.4.3), including one that
  `text-align-last: justify` asked to stretch and that cannot be stretched, and
- a line whose content is already too long for its measure (§6.1), which now flushes right and spills
  past the *left* edge, the way a non-justified RTL overflowing line already did.
