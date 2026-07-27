# A list marker belongs beside its item's first line — whatever content that item has, and whatever fragmentainer that line is in

Closes the two residuals [#460](https://github.com/jhaygood86/PeachPDF/pull/460) left behind:
[#467](https://github.com/jhaygood86/PeachPDF/issues/467) (a `<li>` whose content is block-level got
no marker at all) and [#468](https://github.com/jhaygood86/PeachPDF/issues/468) (inside a
multi-column container the marker went with the column the item *ended* in).

## The load-bearing idea, for each half

**#467 — the marker has no business in the anonymous block.** `DomParser.CorrectInlineBoxesParent`
(not `CorrectTextBoxes`, which the issue and the gap file both named — worth knowing if you go
looking) wraps a list item's inline run in an anonymous block when the item also has block children,
and the marker was in that run. It became a *grand*child, and the one call that positions a marker
and the one that paints it both scan **direct** children. An outside marker is not part of the inline
flow the anonymous block exists for, so it is simply not gathered into it —
`DomParser.JoinsTheInlineRun`.

**#468 — the fragment tree already has the per-fragment channel, so the marker needs no second one.**
The accepted-gap note concluded this could not be fixed without making the marker's geometry a
per-fragment fact. It already is: `BoxGeometrySnapshot.WordOrigins` is captured per column, and
`FragmentEmitter`'s `FragmentRegion` tests the **inline** axis for a nested fragmentainer. So a marker
left at column 1's coordinates is claimed by column 1 and rejected by column 2, for free. What the
earlier, measured-worse attempt did differently was keep re-positioning the marker on the resumed
pass — which is what left two live origins for one word. Stop doing that and the whole special case
for columns disappears: `CssBox.MarkerBelongsToTheFragmentainerBeingFilled` is now `resume is null`,
one rule for both fragmentainer kinds, and `FragmentainerContext.HasOwnBand` is out of it entirely.

## Found by running it, not by reading it

- **A pass can place an item and then keep none of it.** The parent decides that — §3.1's
  break-before propagation, §5.4's orphans floor, a column's overflow arm — *after* the item's own
  layout has returned and its marker has been positioned. The fill then drops the item from that
  fragmentainer's captured geometry, and the marker is the only trace of it left there: claimed by
  nothing, painted nowhere, which is #444's own symptom from the other side. Only the sweep found it
  (9 markers in 660 documents, every one `column-fill: balance`).
  `CssBox.TakeBackTheMarkerOfAnItemThisPassKeptNothingOf` hands the marker back, and
  `OutsideMarkerAwaitsPlacement` lets the next pass take it — using the marker's own
  `AwaitsTheNextFragmentainer` flag as the whole bookkeeping channel, so there is nothing new to keep
  in step.
- **The suite passed while the block-content item had no height.** Keeping the marker a direct child
  makes it the box the item's first real child resolves its top against, and a marker's
  `ActualBottom` is 0 until it is positioned — which is after the children. The `<p>` went to
  document Y 0 and the item laid out flat. Every marker assertion still passed, because they all ask
  about the marker's relation to its item and that stayed correct. The **rasterized showcase** is
  what showed it. `DomUtils.GetPreviousSibling` now steps over an outside marker, alongside the
  `display: none`/absolute/fixed/float skips it already had, and
  `AnItemWhoseContentIsBlockLevel_LaysThatContentOutBelowTheItemsTop` pins it.

## Two more the review pass found, both real, both fixed

- **§3.1 propagation never fired for a list item.** `BreakPropagation.IsInFlow` documents that it must
  name the same set as `GetPreviousSibling`; it did not once the marker was excluded from the latter.
  Counting the marker made an item's first *real* child look like a second one, so an item whose
  content asked to start on the next page stayed behind as an empty stub with its bullet beside
  nothing and its content carried on unnumbered. This one is only reachable *because* #467 closed —
  before it there was no marker to strand.
- **"Kept nothing" was asked of words alone**, so an item keeping wordless block children read as
  keeping nothing and lost its marker to a later fragmentainer. `HasPlacedContent` counts a placed
  in-flow block child too. The obvious alternative (words only) is much worse and was measured: over
  162 wordless-content documents it turns 55 late markers into 52 claimed zero or twice.

## Deliberately not done

- **No new geometry channel for the marker**, which is what the gap file expected to be needed. See
  above — the columns engine's own snapshot is the channel.
- **An `inside` marker is still wrapped into the anonymous block.** It is an ordinary flowed inline,
  and browsers give it its own anonymous block above a block-level child rather than putting it on
  that child's first line. `AnInsideMarker_IsStillWrappedWithTheItemsInlineRun` pins the narrowing.
- **No attempt at CSS Lists 3 §3.1.1 marker-box layout** — a separate, still-accepted gap.
- **A block-content list item inside a multi-column container is not settled** — the page grid is
  clean, columns are not. Recorded as
  [its own gap](../accepted-gaps/marker-on-a-block-content-list-item-inside-a-multi-column-container.md)
  with tracking issue #483, because it is the columns engine's fragmentation of a block-level item
  rather than the marker rule: disabling the take-back entirely leaves the counts unchanged.

## Evidence

- 660-document multi-column sweep (`column-count: 2|3`, both `column-fill` values, 4–14 items, page
  heights 120–300pt), before → after: duplicate-claim documents **134 → 0**, markers in a later
  fragment than their item's first **2350 → 0**, unclaimed markers **0 → 0**, unclaimed words
  **0 → 0**.
- Full suite 6788 passed / 0 failed (net8.0). 100% diff coverage on the changed lines.
- Solution rebuild with 0 warnings.
- Showcases: 68 of 69 byte-identical to `main` once `/CreationDate`, `/ID`, `/M`, `/NM`, the font
  subset tag **and PDFsharp's own `% Creation date:`/`% Creation time:` header comments** are
  normalized — that last pair is not in the list
  [the timestamp invariant](../invariants/testing-a-pdf-carries-two-timestamps-not-one-when-showcases-are-compared.md)
  records, and without it every one of the 69 differs. The one that changed is `marker_styling`,
  which gained the two new sections, verified by reading its PDFium **and** MuPDF renders.
