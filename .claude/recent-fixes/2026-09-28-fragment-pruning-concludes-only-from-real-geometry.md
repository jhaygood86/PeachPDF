# Fragment pruning concludes a box is finished only from geometry it really has

Part of #1487.

## What was wrong

`FragmentEmitter` skips a subtree it has concluded has nothing left to show (an "emitted nothing" mark),
so a page does not re-walk the whole document; without it the 280-page ordinary document took 47.5s instead
of 11.8s (the #917 cost). A wrong conclusion drops every word under the box from the page that holds them.

The shape that shows it most plainly is a card: an auto-height `overflow: hidden` or `auto` panel whose first
child, usually a title, has a top margin. Started within that margin of the page foot, its first page holds
nothing, and on `main` the whole card is drawn on no page:

```html
<!-- 300x200pt page, 20pt margins, Arial 10pt/12pt -->
<div style="height:145pt">Intro</div>
<div style="overflow:hidden;background:#eef"><h2 style="margin:30pt 0 4pt">Title</h2><p>Line1</p> … <p>Line20</p></div>
<p>After</p>
```

`main` draws 3 of the 23 words. Sweeping the spacer in 0.5pt steps (61 heights, `hidden` and `auto`), 120 of the
122 documents lose the whole card, 2,400 words in total; with this change none do. Before scroll containers broke
across pages the same documents lost 48 words in all.

Three ways the conclusion went wrong, each found and minimized from generated documents:

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
  now skips the outgoing break chain, as `CommitGeometricallySettledObservations` already did. This is the
  exclusion that fixes the card above: its pass ends with the card's own boxes on the break chain, and the
  empty first page made `html` and `body` look finished. On its own, without the range check, this exclusion
  is not safe: the first attempt at the card's fix skipped the chain alone and dropped a flex item's line in a
  plain document.

## What was found by running it

Against `main`, on generated documents with unique words extracted per page, and with each of the three
changes removed in turn (a build with one change removed loses what that change recovers):

| Sample | `main` loses | This change loses | Without the inline-box change | Without the range check | Without the chain exclusion |
|---|---|---|---|---|---|
| 250 mixed-feature documents (floats, columns, flex, grid, scroll containers) | 37,625 | 34,714 | 37,643 | 34,926 | 34,765 |
| the card sweep above (305 documents) | 2,400 | 0 | 0 | 0 | 2,400 |
| 250 ordinary documents | 482 | 482 | | | |

- On the mixed sample 69 documents lose fewer words and one draws 33 more duplicates; the 14 documents `main`
  renders completely and the 203 ordinary ones it renders completely are drawn in the same places.
- The inline-box change is the one that recovers most of the mixed-sample words (2,699 of the 2,911), the
  range check adds 212 and the chain exclusion 51; the chain exclusion is also what fixes the card. Each has a
  test that fails without it.
- A sweep over the three fixture shapes with the page and spacer heights varied (331 documents) does not
  separate the builds: the changes only matter in the specific documents the fixtures reduce, which is why
  the fixtures are reductions and not sweeps. One of those shapes (a box inside a capped scroll container)
  loses 12 words on `main` and on this change alike; it is not touched here.
- `FragmentPruningKeepsContentTests` holds one reduced document per cause, in Liberation Sans, and
  `CardAtThePageFootIntegrationTests` the card at ten offsets. The inline-box fixture is a fuzz reduction that
  still loses 11 of its 136 words to other causes, so it asserts the 68 words this change recovers.
- Of the 192 existing showcases, 190 rasterize identically to `main`'s. `svg` gains the 461 words its pages 3
  and 4 had lost, and `canvas_background` has identical text and positions but differs in 633 of 348,894
  pixels, by at most 5 of 255.

## Not done

Words are still lost in documents that mix these features (34,714 across the 250 mixed-feature documents
above). Those losses were not attributed to pruning on this baseline: the comparison that does it, the same
build with pruning's two reads turned off (see the invariant), was not repeated. #1487 stays open for them
and for the harmless tree divergences `PEACHPDF_VERIFY_FRAGMENT_PRUNING=1` reports.
