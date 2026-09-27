# An auto-height scroll container fragments like any block

Closes #1321. This is the first part of #1334, split out so it can land on its own: floats laid out in one
piece (#1339, #1340) and absolutely positioned boxes (#1349) follow as separate changes. Until they land,
this change keeps a wrapper around a float or an atomic inline monolithic, as on `main`.

## What was wrong

`MonolithicContent.IsMonolithic` counted every scroll container (`overflow` other than `visible`/`clip`) as
monolithic.
- A tall `overflow: hidden` wrapper therefore fit no fragmentainer, and its content was laid out unbroken,
  with each page showing one slice.
- The line straddling each slice boundary was claimed only by the page its top fell on. That page drew it
  in its bottom margin, where the page clip hid it, so one line was lost per page.
- Whether a line happened to straddle depended on alignment, which is why the issue's `padding-top` was
  needed to make it appear.

## The load-bearing idea

The spec never asked for that set. css-break-3 §2 says "UAs **may** consider as monolithic any elements with
overflow set to auto or scroll and any elements with overflow: hidden and a non-auto logical height (and no
specified maximum logical height)".

An auto-height scroll container grows with its content, so on paper it has nothing to clip or scroll in the
block axis. `IsMonolithic` keeps a scroll container monolithic only when:
- **its block size is fixed** (`HasConstrainedBlockSize`), or
- **its break is not known to survive** (`!BreaksInBlockFlow`).

The second condition is an allow-list in three parts:
- **the box itself:** a block or list-item that is neither a float nor out of flow, not a flex/grid item,
  and not under `break-inside: avoid`;
- **every ancestor:** a kind that carries a break on (`EveryAncestorCarriesABreak`);
- **everything inside:** a kind that carries a break too (`EveryDescendantCarriesABreak`).

An unlisted kind keeps `main`'s monolithic behaviour, so it only leaves the fix out. The lists do not look
at the flow around the box, so a box that breaks still loses content wherever a plain block in its place
already does ([the gap](../accepted-gaps/an-auto-height-scroll-container-that-breaks-inherits-block-flow-losses.md)).

## How the allow-list was found (each round measured lines disappear)

The rounds below were measured while this was one PR (#1334).

- **Inline-block and flex/grid items.**
  - An `inline-block` straddling a boundary collapsed to a zero-height clip.
  - A flex or grid item's `Height` is set transiently by its engine while measuring, then pinned by
    `ItemContentCommit.CommitLayout`. A block-size test answered differently before and after the commit,
    and the line-relocation mover and the commit layout disagreed.
  - Both are excluded. `StraddlingAutoHeightScrollContainerInlineBlock_KeepsItsContent` and
    `…Item_MovesWholeWithItsLine` pin them.
- **Percentages resolve against layout's own base.** `CssLayoutEngine.PercentageHeightResolves` is
  extracted from `HasDefiniteHeight`.
  - `height` goes through it.
  - `max-height` goes through `IsHeightDefinite(box.ContainingBlock)`, since that is what `ApplyHeight`'s
    clamp applies, for abspos boxes too.
  - A percentage inside a flex/grid item is treated as a cap, because the item's height changes within the
    engine's pass.
  - A cap is a length (`IsValidLength`), never a keyword.
- **A size fixed without `height`.** A declared `aspect-ratio` (CSS Box Sizing 4 §5) and an abspos box with
  both block insets (CSS 2.1 §10.6.4) both cap the box. Each is asked of the declaration, not of the used
  size, which would answer differently before width layout.
- **Floats, page floats, vertical writing-mode ancestors, captions and `inline-table`.** Each lays its
  content out at an assigned position that drops the break token.
  - Measured: a float lost F4–F12 of 12. A clearfix block inside a `vertical-rl` parent placed no lines at
    all. The caption and `inline-table` rows lost F4–F5.
  - The deny-list kept missing the next case, so it became `EveryAncestorCarriesABreak`.
  - `AutoHeightScrollContainerUnderAnyAncestor_PlacesEveryLine` pins nine ancestor shapes.
- **Descendants.** A wrapper also needs its contents to carry the break:
  - A tall float in a clearfix wrapper drew 0 of 30 lines.
  - A `.row` of two floats drew 0 of 60.
  - An `overflow: hidden` child of a multi-column container lost 29 words on a single page, at paint time:
    its first-column fragment got a zero-height clip. So `WordsPlaced` paints each page through
    `RecordingGraphics` and counts only strings inside every clip in force.
  - An inline-block holding block content lost all 14 of its lines.
  - An absolutely positioned badge vanished (#1349).
  - `EveryDescendantCarriesABreak` rejects all of these. Its answer is cached per layout generation
    (`CssBox.DescendantsCarryABreak`).
- **`break-inside: avoid`.** A scroll container under it stays monolithic: fuzz seeds 238 and 273 lost lines
  there, and the same documents with plain `div`s lose the same lines on `main`
  ([#1369's gap](../accepted-gaps/a-break-inside-avoid-block-taller-than-a-page-can-lose-a-line.md)).

## Floats and atomic inlines stay out, for now

#1334's later rounds let floats and atomic inlines into a fragmenting wrapper, once a block-level float was
laid out in one piece and moved whole when it fits. That change is split out. Without it, a block-level
float breaks between its lines, its break ends the pass, and the text beside it is placed back on an emitted
page. That is the review's repro A, where `side` was drawn on no page.

The review's fuzz also found a tall inline-block inside a wrapper losing the tail of its slice. So both are
rejected in `EveryDescendantCarriesABreak`, and so is a scroll container that is itself absolutely, fixed or
running positioned. Its break ends the pass the same way while the content after it goes back on the emitted
page (#1349).

## A box capped by max-height stays whole

`HasConstrainedBlockSize` counts any cap: a `height`, a `max-height`, an `aspect-ratio` or both insets, for
every overflow value. Chrome prints a `max-height` box whose content fits under its cap across two pages,
but when the content overflows the cap, the clipped lines lie past the box's end, and a break among them
ends the pass with the content after the box placed back on an emitted page (all ten lines after a
60pt-capped 30-line box were lost). Whether it overflows is only known after layout.

A first version broke every capped box, noted one that clipped after `ApplyHeight`, and laid the whole
document out again with it kept whole. The second review removed it: the retry reset only the root's size
and position, so a table row broken across the page on the first attempt came out 38pt taller on the retry
(an anonymous inline box kept its first-attempt position, and `GetMaximumBottom` read it), and 400 clipping
cards rendered 7–9x slower for byte-identical output. Both samples are in #1479; this is an
[accepted gap](../accepted-gaps/a-scroll-container-with-a-max-height-is-kept-whole.md) (#1375, #1479).

## What was found by running it

- **Existing fixtures that used a bare `overflow: hidden` card** as the stock monolithic box now use
  `overflow: auto` with a `height` equal to the card's measured natural height (60pt, 462pt, 660pt), so no
  geometry changes.
- **The issue's repro.** `TallAutoHeightScrollContainer_DrawsEveryLineInsideAPageBand` reproduces it
  exactly against the old classifier (`L9`/`L19`/`L28` end 2–11pt past the 180pt band) and passes with the
  fix. Given a fixed tall height, the same fixture still loses those lines. That is the capped case,
  [#1328's gap](../accepted-gaps/a-capped-scroll-container-taller-than-a-page-loses-its-boundary-lines.md).
- **The review's card.** It splits now instead of moving whole, and that exposed the heading whose ink rises
  above its line. That is fixed by the change this one builds on
  ([its entry](2026-09-26-a-line-is-claimed-by-the-page-its-line-box-is-on.md)).
  `AnOverflowHiddenCardSplitAtAPageFoot_DrawsItsHeadingOnce` pins it here.

## Review of the split PR

- **An out-of-flow ancestor.** The ancestor allow-list tested display, float, multicol, vertical flow and
  `break-inside`, but not position, and an absolutely positioned box computes to `display: block`.
  - A wrapper inside a straddling absolute box fragmented, its break ended the pass inside the absolute
    box, and every paragraph after that box was lost.
  - `EveryAncestorCarriesABreak` now rejects `IsExcludedFromFlow`.
  - `AutoHeightScrollContainerInsideAnAbsoluteBox_LosesNothingAfterIt` fails without it.

## Second review: a card whose first child's margin reaches past the page foot

An auto-height card starting within its first child's `margin-top` of the page foot drew only its last line
and what followed it. The cause was in the fragment emitter, not the classifier:
- The margin cannot collapse through a scroll container, so the card's piece on the first page holds
  nothing, and a mover relocates the card whole. The pass then ends with a break token resuming in the slot
  it has just filled, having placed nothing there.
- `FragmentEmitter.EmitPass`'s final `CommitRemainingObservations(commit: true)` marked `html` and `body`
  "emitted nothing from this slot on", although both were on that outgoing break chain.
- The re-run pass placed the card at the same position, which discards no mark, so every later emission of
  those slots pruned the whole root away. `PEACHPDF_VERIFY_FRAGMENT_PRUNING=1` reported it as "the root
  draft is 'null' with pruning and 'a draft' without it".

A plain block never reached it: the child's margin collapses through it and the whole block moves instead.
`CommitRemainingObservations` now skips a box on the outgoing chain (`_continuesInto`), as
`CommitGeometricallySettledObservations` already did. `CardWhoseFirstChildsMarginReachesPastThePageFoot_DrawsEveryLineOnce`
fails without it. On the review's heading sweep (82 documents, a 0.5pt spacer sweep) the PR head lost 302
words that the merge-base draws, and none with the fix.

## User-visible side effect

An auto-height `overflow: hidden|auto|scroll` card that straddles a page boundary is now split across it
instead of moving whole to the next page (see the migration note). That is what browsers do when printing.
Authors who want the old result can add `break-inside: avoid`, or a fixed `height`.
