# A multi-column container no longer visits, on every page, the children that lie past it

Follows [the continuation-reopening fix](2026-10-12-a-column-continuation-does-not-reopen-the-earlier-page.md), which left
forward emission as the remaining quadratic cost of a long multi-column document, and records why its first attempt at this
had to be reverted.

## The load-bearing idea

`FragmentEmitter.ChildrenOf` yields, for a page's multi-column container, the children that page's column captures hold,
against the captured geometry - and then every *other* child live, on the assumption it might be out of flow. That is all
the rest of the document, on every page, each recursed into: 151k visits for 1,200 paragraphs over 126 pages, 11.5M at 4,800.

A child that begins strictly past the top of the next page cannot show anything on this one, so it is no longer yielded.
The same guards the ordinary ahead-of-slot skip uses apply (no out-of-flow boxes, not a captured-instance owner or ancestor,
not on an oversized line, not never-touched), minus its `_currentPassIsFinal`; and it is limited to
`EstablishesMultiColumnContext` containers.

## Why "strictly", and why not "any child no capture holds"

Two things that look like the same optimisation and are not:

- **Skipping every unheld in-flow child drops content.** The first attempt (the nested-columns rule applied at page level)
  took 4,800 paragraphs from 24s to 15s and lost the heading "Section 140" plus 7 words. Cause, found by logging when
  captures hold the heading and when the live walk sees it: the heading was recorded in page 118's column capture, then a
  `break-after: avoid` move carried it to the top of page 119 *after* that capture was taken, and page 119's capture
  starts after it (the resume index points past it). So it is held only by a stale record, and the live read is what draws
  it. "Not held here" does not mean "held elsewhere"; what to trust is the live position.
- **A block's `Location` is not its first fragment's top inside a column.** `ResumeInTheNextFragmentainer` moves a
  continuing box's `Location` to where the remainder resumes, so a paragraph split across this page and the next reports the
  next page's content top while its first lines are on this one. A first version of the ahead test used `>=` and skipped
  those: 7 words missing from the foot of the right column on page 63, logged as 26 `WRONGSKIP` hits whose `Location.Y`
  equalled the next page's top exactly. `>` (plus a point) cannot skip a continuation, which always resumes exactly at the
  top. A genuine child ahead whose top equals the next page's top is merely not skipped; the direction of the error is safe.

## What was found by running it

- The parity oracle (`PEACHPDF_VERIFY_FRAGMENT_PRUNING=1`) caught a third mistake before it shipped: without the
  `EstablishesMultiColumnContext` limit the same skip dropped two child fragments of a table with a repeating header (also a
  captured-instance owner), `TableSpannedBandRepetitionTests.ARowspanTallerThanABand_DoesNotSliceTheRowThatEndsIt`.
  The oracle already fails 9 other tests on `main` with the variable set, so the check that matters is the *set*: it is
  identical with and without this change.
- Box-visit counts are not a clean complexity measure here. Children *behind* the page are still visited once each at the top
  level (they return immediately and do not recurse), so visits still grow ~13x for 4x the content (16x before), while time
  is close to linear. The tests count what the change does (skips, visits per page) rather than assert a ratio.

## Evidence

- Output identical to the build without it: same words and drawing operations on every page for 2-column prose of 32, 64
  and 127 pages and 3-column, `break-inside: avoid` and `column-span: all` variants (PyMuPDF `get_text("words")` + `get_drawings()`).
- Release CLI, matched two-column prose: 4,800 paragraphs 24s -> 15s; the 12,000-paragraph / 1,272-page document ~179s -> 58s
  (with the continuation fix underneath it).
- `MulticolUnheldChildrenTests`: every word of a 240-paragraph, headings-with-`break-after: avoid` document claimed by exactly
  one fragment; skips counted (0 -> thousands); box visits per page well under the whole document. The two counting tests
  fail without the change.
- Full suite green on net8.0 in Release; whole solution `-t:Rebuild` has 0 warnings.

## What this did not fix

Children behind the page are still visited each page, and a split paragraph's own page still walks its subtree. A cursor like
the one the ordinary path keeps (`LiveChildStartFor`) would make the walk over children behind the page O(1), but the capture
path does not use one and the captures' stale entries are exactly why its marks cannot be trusted there. The captures'
resume index should also be looked at on its own: a child pulled to the next page after the previous page was recorded
is missing from the next page's capture.
