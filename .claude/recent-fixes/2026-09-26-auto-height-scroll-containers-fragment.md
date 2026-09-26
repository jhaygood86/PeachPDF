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

An unlisted kind keeps `main`'s monolithic behaviour. It can leave the fix out, but it can never lose
content.

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

## A box capped only by max-height breaks, as Chrome prints it

Chrome prints `overflow: hidden; max-height` and `overflow: auto; max-height` boxes across two pages. §2's
permission for `hidden` is a non-auto height *and no max-height*. So `HasConstrainedBlockSize` takes §2's
own case for every overflow value: a `height` with no `max-height`, plus `aspect-ratio` and both insets for
`auto`/`scroll`.

That exposed a trap: when the content overflows the cap, the clipped lines lie past the box's end, and a
break among them ends the pass with the content after the box placed back on an emitted page. Probes:
- all ten lines after a 60pt-capped 30-line box were lost;
- three lines after a straddling 96pt one were lost, and an empty page was added.

So the layout epilogue checks such a box after `ApplyHeight` (`CssBox.NoteIfAFragmentingScrollContainerClips`).
It looks for content reaching past the end of the page the box starts on, since any break inside a clipping
box loses content. `HtmlContainerInt.LayoutDocument` then lays the document out again with the box
monolithic:
- It runs at most three times, and the set is frozen on the last attempt.
- It runs inside every layout call: the per-page width reflow and the footnote/page-float and
  `target-counter` loops lay out at their own geometry.
- The set is cleared per layout (`PerformLayoutOnePass`), so a box widened since the last layout can break
  again.
- A box clipped within its own page costs no extra layout.

This is an [accepted gap](../accepted-gaps/a-scroll-container-that-clips-past-its-max-height-is-kept-whole.md)
(#1375). Vertical writing modes keep the old rule.

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

## User-visible side effect

An auto-height `overflow: hidden|auto|scroll` card that straddles a page boundary is now split across it
instead of moving whole to the next page (see the migration note). That is what browsers do when printing.
Authors who want the old result can add `break-inside: avoid`, or a fixed `height`.
