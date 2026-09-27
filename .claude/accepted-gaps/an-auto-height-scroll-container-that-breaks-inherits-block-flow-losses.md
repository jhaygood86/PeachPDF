# An auto-height scroll container that breaks inherits the block-flow losses a plain block has

_CSS Fragmentation Level 3. Trackers: [#1480](https://github.com/jhaygood86/PeachPDF/issues/1480),
[#1481](https://github.com/jhaygood86/PeachPDF/issues/1481), [#1482](https://github.com/jhaygood86/PeachPDF/issues/1482),
[#1483](https://github.com/jhaygood86/PeachPDF/issues/1483), [#1484](https://github.com/jhaygood86/PeachPDF/issues/1484),
[#1485](https://github.com/jhaygood86/PeachPDF/issues/1485); related [#1339](https://github.com/jhaygood86/PeachPDF/issues/1339),
[#1328](https://github.com/jhaygood86/PeachPDF/issues/1328)._

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
- **A table inside the box** (#1484, #1485): a row's second cell stays at the page foot while its first cell
  moves on, and a heading at the end of a cell is drawn across the page foot.
- **A monolithic box inside it taller than the page band** (#1328): an inner scroll container kept whole by
  `break-inside: avoid` is moved to the next page (leaving the first blank, as a plain `div` does too) and
  sliced there, losing the line on the slice boundary.

Keeping the box whole in these cases would need a guard that asks where the box is laid out relative to
floats and tables, an answer that changes between passes; the float and table work is where they get fixed.
On the review's fuzz corpora the change draws more words than it loses (on 1,000 documents, 7,281 recovered
against 5,893 lost relative to the merge-base). Not every remaining loss has been minimized and classified:
a third review still found 4,341 lost words in 258 documents that a `flow-root` control did not explain, and
its six worst were the shapes above plus the emitter bug the same change fixes
([the invariant](../invariants/fragmentation-a-pass-that-resumes-inside-its-own-range-commits-no-empty-marks.md)).
