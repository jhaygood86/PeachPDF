# A forced break at the end of a block opened a line

A forced line break ([css-text-3 §5.5](https://www.w3.org/TR/css-text-3/#forced-line-break)) *ends*
the line it falls on. `CssLayoutEngine` instead closes that line and starts the next one with the
break's own word, so a break was always followed by a line — including where the break is the last
thing in the block and there is nothing for that line to hold. CSS 2.1
[§9.4.2](https://www.w3.org/TR/CSS21/visuren.html#inline-formatting) requires such a line to "be
treated as not existing for any other purpose", and a `<br>` is not the preserved newline that
sentence carves out. Every such block came out one line taller than a browser makes it.

`DropATrailingForcedBreaksOwnLine` takes that line back at the point the block finishes.

## What measuring it turned up

- **The line-box shapes say it plainly.** Printing every finished block's lines:
  `<div>x<br></div>` is `['x'] | [<BR>]`, `<div><br></div>` is `[] | [<BR>]`, and
  `<div><br><br></div>` is `[] | [<BR>] | [<BR>]`. The break's word never shares a line with the
  content it follows, and the trailing one always has a line to itself.
- **Only the last break, and only when its line holds nothing else.** `x<br><br>` really is two
  lines in a browser and `x<br><br>c` three — measured against Chrome 152, along with `x<br>`,
  a bare `<br>`, and `x<br>` in front of a `display: block` sibling. Every one of the five now
  matches Chrome to the point.
- **A block with no inline content is not this case** and is deliberately excluded: its single line
  box has no words, and dropping it would collapse a block rather than shorten it.
- **The common shape is a table cell holding a label and an image.**
  `label<br><img style="display:block">` gained a whole line in every row of such a table. On a
  seven-row routing table that is 70pt, enough to push the table after it onto a second page.

## Evidence

`TrailingForcedBreakLineTests`: seven content shapes asserted as a multiple of the block's own line
height, taken from a single-line control in the same document, plus the block-sibling case and the
empty-block exclusion. `line-height` is pinned in the fixture — a break-only line and a text line
differ by a quarter of a point under `normal`, which is the font's metrics and not this test's
subject. Five of the nine fail against `main`; the four that do not are the controls. Full suite
green on net8.0 (10,387 before the new tests), 0 new build warnings.

## Correction

The first version of this cited CSS 2.1 §16.6.1 for "a forced break ends a line". That is wrong and
the maintainer caught it: §16.6.1 is "The 'white-space' processing model", entirely about collapsing
tabs, spaces and linefeeds, and it never mentions `<br>` or a forced break. Verified by fetching the
section. The term is defined in css-text-3 §5.5, and the rule this fix actually implements — an empty
line box "must be treated as not existing" — is CSS 2.1 §9.4.2. Both verified the same way before
being written here.
