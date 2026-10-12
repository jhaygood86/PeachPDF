# A box continuing into the next column or page no longer reopens the page it started on

## The load-bearing idea

`CssBox.ResumeInTheNextFragmentainer` moves a box that continues into the next fragmentainer (a nested one with its own
band: a multi-column column) with a `Location` write to where the remainder starts. `Location`'s setter treats every block-axis
change as a relocation (`OnBlockAxisRelocated` -> `NotifyGeometryChanged` -> `FragmentEmitter.InvalidateFrom`) and reopened the
fragmentainer the box had been in. But a continuation changes nothing about what the box already placed there: its lines
and words stay where they were, and the fragmentainer they were emitted into is still correct.

Every reopened slot is re-emitted by `CatchUpStaleSlotsBehind`, and in a multi-column document that is a walk of the whole
document (see below), once per page. The write now carries a flag (`_resumingIntoTheNextFragmentainer`) so the setter keeps
clearing the "emitted nothing here" observations (still conservative) but skips reopening the earlier slot. The `Location`
setter stays the single choke point; the flag is scoped to that one write with `try/finally`.

## What the profile of a long multi-column document showed

`multicol` prose, 4,800 paragraphs (~560 pages), CLI Release: 32s, quadratic from ~3,000 paragraphs (linear before that:
1,200 -> 5.9s, 2,400 -> 11.5s, 4,800 -> 32s, 9,600 -> 105s). `BuildDraft` was 17.8s of 24.4s layout, split between the forward
`EmitPass` (9.8s) and `CatchUpStaleSlotsBehind` (8.2s).

- **Catch-up** was reopening ~one slot per page: 114 catch-up emits at 127 pages, each ~6,400 box visits (the whole document).
  The `InvalidateFrom` callers, by stack, were almost all `ResumeInTheNextFragmentainer` -> `set_Location`.
  This change takes that to 11 emits (16 at 2,400 paragraphs, 41 at 4,800), 32s -> 24s at 4,800 paragraphs.
- **Forward emission is the other half and is not fixed here**: 11.5M box visits at 4,800 paragraphs, 4x per doubling.
  `ChildrenOf`'s capture path yields only children the page's column capture holds, but every other child falls through to
  "a child no fragmentainer holds ... read live" and is yielded to `BuildDraft`, which recurses into it: all ~1,190 other
  paragraphs, on every page (151k visits = 1,200 paragraphs x 126 pages). The ahead-of-slot skip cannot help because a
  non-final pass never takes it (`SKIP notFinal`: 556k of 556k).

## Tried and reverted: skipping unheld in-flow children at page level

Applying the existing nested-columns rule (`capture is not null && !IsExcludedFromFlow -> continue`) at page level too took
717k visits to 635 and 32s to 15s at 4,800 paragraphs, linear. It **drops content**: the PDF lost the heading "Section 140"
(and 7 words on another page) that the old build drew. No column capture in that document holds any `<h2>`: all 120 headings
were yielded live on every page, and the live read is what drew them at their one real page. The capture misses in-flow
children, so "unheld" does not mean "held on another page".

The route to the ~2x that remains is therefore one of: find out why the capture does not hold those headings and fix that,
or skip an unheld child only when some capture of this container holds it on a **different** slot (needs an
owner -> ever-held index kept correct across `RecordCapturedInstance`, both `ClearCapturedInstances` forms and the
"drops the geometry of boxes" path; a rebuild per query is itself quadratic). Do not skip on `IsExcludedFromFlow` alone.

## Evidence

- Output identical to the build without this change: same words and drawing operations on every page for 32-, 64- and
  127-page 2-column prose, and 3-column, `orphans/widows: 3`, `break-inside: avoid`, `column-fill: auto` with a height,
  `column-rule` and `column-span: all` variants (PyMuPDF `get_text("words")` + `get_drawings()`).
- `MulticolContinuationReopeningTests`: re-emitted catch-up slots over a ~30-page document (21 of 26 passes before, < 8
  after) and flat from 150 to 600 paragraphs. Both fail without the change. Backed by a permanent
  `HtmlContainerInt.CatchUpSlotEmits` counter next to `BuildDraftCalls`.
- Full suite green on net8.0 in Release; whole solution `-t:Rebuild` has 0 warnings. With `PEACHPDF_VERIFY_FRAGMENT_PRUNING=1`,
  `FragmentainerCursorIntegrationTests.AForcedBreakThatStepsOverAPage_StillLeavesTheSteppedOverPageBlank` fails, and fails
  identically without this change (pre-existing, not looked into).
