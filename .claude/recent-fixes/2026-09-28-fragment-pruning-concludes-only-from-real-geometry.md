# Fragment pruning concludes a box is finished only from geometry it really has

Part of #1487.

## What was wrong

`FragmentEmitter` skips a subtree it has concluded has nothing left to show (an "emitted nothing" mark),
so a page does not re-walk the whole document; without it the 280-page ordinary document took 47.5s instead
of 11.8s (the #917 cost). A wrong conclusion drops every word under the box from the page that holds them.
On two fuzz corpora of plain and `flow-root` documents, `main` drew 12,579 and 8,074 fewer words with pruning
than the same build with pruning's two reads turned off, and pruning off never lost a word. The published
SVG showcase lost every section heading and caption on its fourth page.

Three ways the conclusion went wrong, each minimized from those corpora:

- **A plain inline box's `ActualBottom` is not geometry.** `CommitGeometricallySettledObservations` marks a
  box whose `ActualBottom` is above the slot being emitted. The inline flow never places an inline box
  itself, only its line rectangles and words, so its `Location` stays at 0 and its `ActualBottom` holds
  whatever an earlier sizing left. The text box inside a float read 57.5pt while its words were at 346pt,
  two pages on. `SettledBottomOf` now measures an inline box by its own line rectangles and words (and those
  of the inline boxes inside it). This cause alone removed about 90% of the losses.
- **A box empty throughout a pass's range can have content further down.** `CommitRemainingObservations`
  marked every box the pass's whole range found empty. A `flow-root` box inside a capped scroll container,
  laid out in one piece, was empty on page 2 with its only line at 445.6pt on page 3. It now needs the box's
  content to end within the range (`rangeBottom`, the range's last page bottom). A box with no geometry at
  all is still marked: it has nothing to lose, and skipping those made three fuzz documents 2–3× slower.
- **A box the pass stopped inside has no bottom yet.** Its height is only applied on the pass that completes
  it, so its `ActualBottom` is its top, which the range check above reads as "ended inside this range". A
  `flow-root` box around a flex container read 329.1pt on a page ending at 340pt. `CommitRemainingObservations`
  now skips the outgoing break chain, as `CommitGeometricallySettledObservations` already did. On its own,
  without the range check, this exclusion is not safe: the first attempt at #1413's B1 fix skipped the chain
  alone and dropped a flex item's line in a plain document.

## What was found by running it

Measured against the same build with pruning off (the unpruned walk is the reference: it only ever adds
words), words that pruning still drops:

| Corpus | `main` | This change |
|---|---|---|
| 1,000 `flow-root` control documents | 12,579 in 205 | 18 in 1 |
| 800 `flow-root` control documents | 8,074 in 162 | 0 |
| 1,000 scroll-container fuzz documents | 16,151 in 253 | 1 in 1 |
| 800 scroll-container fuzz documents | 12,160 in 229 | 87 in 2 |

- Timings, fastest of three against `main`: the 280-page ordinary document 0.89×, 3,000 cards 0.98×, 400
  clipping cards 0.93×, a 6,000-paragraph wrapper 0.97×, the three largest fuzz documents 0.87–1.08×.
- 184 of 185 showcases are byte-identical; `svg` gains the 461 words its pages 3 and 4 had lost.
- `PEACHPDF_VERIFY_FRAGMENT_PRUNING=1` failures on the two control corpora fall from 964 to 369 documents
  (with the first cause alone). Many of the rest do not lose words: a heading whose glyphs rise above its
  line box gets an empty sliver fragment on the previous page in the full walk that the "not started yet"
  skip in `ChildrenOf` omits. The check compares trees, not words, so it still reports them.
- `FragmentPruningKeepsContentTests` holds one reduced document per cause, in Liberation Sans. The float and
  capped-scroll-container tests fail on `main`; the flex test passes on `main` and fails with only the chain
  exclusion removed, which is what it guards.

## Not done

The remaining losses (106 words in 4 of about 3,600 documents) are not minimized, and #1487 stays open for
them and for the harmless tree divergences above.
