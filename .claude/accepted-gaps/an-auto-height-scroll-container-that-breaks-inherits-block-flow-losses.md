# An auto-height scroll container that breaks inherits the block-flow losses a plain block has

_CSS Fragmentation Level 3. Trackers: [#1480](https://github.com/jhaygood86/PeachPDF/issues/1480),
[#1481](https://github.com/jhaygood86/PeachPDF/issues/1481), [#1482](https://github.com/jhaygood86/PeachPDF/issues/1482),
[#1483](https://github.com/jhaygood86/PeachPDF/issues/1483); related [#1339](https://github.com/jhaygood86/PeachPDF/issues/1339)._

Once an auto-height `overflow: hidden|auto|scroll` box breaks like a block (#1321), it loses content wherever
a plain `div` in the same place already does. Before, it was kept whole, which happened to protect those
documents. `MonolithicContent`'s allow-lists look at the box, its ancestors and its own contents, not at
the rest of the block flow around it, so they do not catch these. Each shape was minimized from the #1413
review's fuzz and measured against baseline with the scroll container swapped for a plain `div` and for
`display: flow-root`: all three lose the same words there, and Chrome loses none.

- **A child reaching past the box's end** (#1480): a `position: relative` offset, a negative
  `margin-bottom`, or content overflowing a fixed-height child. The break inside that child ends the pass
  past the box's end, and the content after the box is drawn on no page.
- **A float outside the box** (#1481, #1482, #1483): a `break-inside: avoid` float earlier in the same table
  cell loses its last line, a paragraph after a table following an overhanging float loses words, and a
  block after an overhanging float loses its first lines.

Keeping the box whole in these cases would need a guard that asks where the box is laid out relative to
floats, an answer that changes between passes; the floats work (#1339, #1340) is where they get fixed. On
the review's 1,000-document fuzz corpus the change still draws more words than it loses (7,298 recovered
against 5,885 lost relative to the merge-base), and every document with a loss already lost words there.
