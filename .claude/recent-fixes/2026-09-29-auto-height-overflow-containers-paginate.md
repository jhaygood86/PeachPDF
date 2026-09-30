# Auto-height overflow containers keep their page break points

A real document (a wrapper with `overflow: hidden` and no height around the whole page body) lost its last
line on page 1: the line stayed in the PDF text but the page edge sliced it, and it belonged on page 2.
`MonolithicContent.IsMonolithic` called every scroll container unbreakable, so layout suppressed the
container's internal breaks and the page clip cut through whatever line fell on the edge.

## What changed

A scroll container is now monolithic when it has a definite logical height or maximum logical height
(`HasConstrainedLogicalHeight`, which reads a percentage that cannot resolve as automatic and uses the width
in a vertical writing mode), when an engine other than block flow lays it out
(`IsLaidOutByAnEngineThatCannotContinueIt`: a flex or grid item, or a box inside a multi-column container), or
when it holds an absolutely positioned box that renders (`HoldsAnAbsolutelyPositionedBox`). Everything else,
an auto-height overflow box in ordinary block flow, breaks like any other block.

CSS Fragmentation 3 4.1 lets a user agent treat `overflow: auto`/`scroll` as monolithic, but `overflow: hidden`
only with a non-auto logical height and no specified maximum. The rule here is deliberately narrower than
"allow everything 4.1 does not forbid": a hidden box with a maximum stays unbreakable too, which goes one step
past 4.1 (see below for why). Chrome splits sized scroll containers across pages as well, so the sized case is
a place where PeachPDF still keeps a box whole that a browser does not.

The stale-record guard in `HtmlContainerInt.TryRebuildForBudget` is part of the same change. Once an overflow
box can break, a box's line boxes can be replaced by a later layout inside the pass that asked for a `widows`
rewind, so the record says it completed more lines than the box holds, and rebuilding it indexed past the end.
A record claiming more completed lines than the box holds is now declined. Note what declining means: the box
has already been marked as having taken its one rewind, so the `widows` requirement for that box is left
unmet, which css-break-3 4.3 allows as a last resort. A reduced form of the crashing document is in
`WidowsRewindStaleRecordTests`; it crashed the tree before this change as well.

## What was found by running it (and why the rule is not simpler)

- The reported document now puts the sentence on page 2, under the document's own fixed header. Chrome breaks
  page 1 at a different line for reasons unrelated to this change, so its page-2 y position is not comparable.
  The document's wrapper is itself positioned, which is why the absolute-box condition looks at descendants
  rather than at the wrapper's own `position`.
- A first version made every unsized scroll container breakable, including flex and grid items and boxes in
  columns. A review of it measured lines lost that upstream keeps:
  - a flex or grid item straddling a page edge lost a whole 8-word paragraph (a lead paragraph of 82 to 90
    words on a 300x200pt page; the commit pass pins the item's used size before its own layout, so the
    classifier answered differently at different times for the same box);
  - `columns: 2` around an auto-height `overflow: hidden` box drew 8 of 96 words for 12 paragraphs (the hidden
    clip rectangle is applied per fragment while the box now spans columns).
  Both now stay unbreakable, as before, and `OverflowBoxInLayoutEngineTests` covers them (six flex/grid
  combinations, three multi-column shapes, and the classification).
- The same first version let `overflow: hidden` with a `max-height` break, which dropped the paragraph after
  the box at all 10 break positions tried. That box stays whole again.
- A `position: relative; overflow: hidden` wrapper holding an absolute box lost that box's words when the
  wrapper straddled a page edge (6 and 3 words at lead paragraphs of 60 and 75 words; 30 and 45 were fine).
  Making every positioned wrapper monolithic fixed that but also put the reported document back to its old
  output, so the condition looks at descendants. A wrapper with no `position` of its own lost the same words
  when its absolute descendant was placed against an ancestor above it, so the wrapper's own `position` is not
  part of the condition either, and a `display: none` absolute box does not count (it renders nothing).
  Scanning the subtree on every layout pass cost 7 to 27% on a 6,000-block wrapper (a long document re-enters
  the wrapper on every page), so the answer is cached on the box (`CssBox.HoldsAbsolutelyPositionedBox`):
  7.59 s against 7.61 s on upstream for the static wrapper, 7.52 s against 7.52 s for the positioned one.
- Auto-height `overflow: hidden` and `scroll` boxes in block flow now split across pages the way Chrome does
  (5 | 13 | 12 words in the probe document, where before one word was lost at a page edge).
- The page content of every showcase that existed before this change was byte-identical to the previous tree,
  except the monolithic-content showcase, whose cards now use `break-inside: avoid` (the same relocation, and
  what Chrome does), so nothing without a tall auto-height overflow box in block flow changed.

## What was deliberately not done

Making auto-height scroll containers breakable inside flex/grid items, columns, or when they hold an absolute
box needs the item's size before pinning to be remembered, the clip rectangle to be resolved from the finished
fragment, and absolute boxes to be placed against a containing block that continues onto another page; that is
larger work and is recorded in `.claude/accepted-gaps/`.

Sized scroll containers (a definite height or maximum) also stay whole where Chrome splits them. Letting them
break dropped the content after a hidden box with a maximum on the documents measured, through defects already
in those paths (text past the end of a fixed-height box inside a table cell inside a grid item is dropped; a
box whose content overflows a fixed height across a page break can drop the content after it), so they are
not reachable through this change while the sized box stays whole.
