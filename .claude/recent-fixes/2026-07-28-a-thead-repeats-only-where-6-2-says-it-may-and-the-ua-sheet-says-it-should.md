# A `<thead>` repeats only where §6.2 says it may, and the UA sheet is what says it should

Closes [#494](https://github.com/jhaygood86/PeachPDF/issues/494) and
[#520](https://github.com/jhaygood86/PeachPDF/issues/520) (PR #519). Both of §6.2's conditions, not the
one the issue and the tracker expected to be separable.

## What was true

[css-tables-3 §6.2](https://www.w3.org/TR/css-tables-3/#repeated-headers) repeats a `<thead>`/`<tfoot>`
on each page a table spans *"if the header/footer has avoid `break-inside` applied to it"* and *"if the
height required to do so is inferior to two quarters of the page height (up to one quarter for header
rows, and up to one quarter for footer rows)"*. `_shouldRepeatHeaders` was `_headerBox != null &&
_headerBox.Display == table-header-group` — and the second half is tautological, since `_headerBox` is
only ever assigned inside `case CssConstants.TableHeaderGroup:`. So it read "the table has a `<thead>`".

Free while the room was notional. Not free since PR #495 (#439) made the header's room real at the
band's head and PR #513 (#493) made the footer's real at its foot: a tall group is now charged its own
height out of **every** band the table spans, at both ends, with nothing capping the bill.

## The load-bearing idea

The plan of record — the issue's own "Work" section, the gap file, and #320's suggested ordering — was
to take the quarter cap alone and record `break-inside` as a deviation, because applying it changes
behaviour for every document with a `<thead>`. **That framing had the condition in the wrong layer.**
§6.2's condition is not "PeachPDF should repeat less"; it is "repetition is a property an author can
turn off". Put `thead, tfoot { break-inside: avoid }` in the UA print stylesheet and the strict reading
is *behaviour-preserving* — the default is what every print engine does, and `break-inside: auto` becomes
a real, spec-sanctioned opt-out the engine did not previously offer.

That works only because `break-inside` is **not inherited** here: `CssBoxProperties.InheritStyle` copies
it in the `everything: true` branch alone (a structural duplicate of the same element), so the rule
cannot reach the group's rows, cells, or cell content. `BreakInsideOnAThead_DoesNotReachItsRowsOrCells`
pins that, because the whole approach rests on it.

## Two questions that were one field

`_shouldRepeatHeaders` answered "is the group detached and proxied" **and** "does it repeat", and the
split is what most of the diff is. They are now `_headerIsDetached` and `_headerRepeats`, and three sites
are keyed to the first on purpose:

- the measurement steps — a group must be laid out to be measured before the cap can be evaluated, so a
  cap expressed inside the flag that gates the measurement would be circular;
- the orphan pre-check that moves a table whose header fits but whose first body row does not — a
  declined group is still drawn once, at the table's top, and can strand there just the same;
- the **headerless** whole-table pre-check, gated on the *absence* of both groups, which is the trap:
  reading repetition there sends a declined table down a relocation path a table with a `<thead>` never
  takes.

"Not repeated" means *not repeated*, not *not drawn*. The first header block still runs on the pass that
starts the table (`_headerRepeats || !_continuesAPreviousPass`), and step 5's closing footer is keyed to
detachment, because it closes the **table** rather than a page — the distinction PR #513 established
between step 5 and step 5a.

## The half a count cannot state

Gating the proxies alone would have passed every "drawn once" assertion and changed nothing that matters:
the band reservations would still charge each page the group's height, and content would simply start
below a blank strip. So `RepeatedFooterHeight` replaces four bare `- _footerHeight` subtractions, which
were only ever safe because `_footerHeight` was zero for exactly the tables that draw no repeated footer
— no longer the same set, since a declined group is still measured.
`ATallHeaderThatDoesNotRepeat_LeavesTheLaterBandsToTheRows` is the assertion that separates the two, and
it is the one whose baseline is loudest: **20.0 on the branch against 124.7 on `main`**.

The decision rides on `DetachedRowGroup.Repeats`, for the reason `Height` already did — a continuation
skips the measurement step entirely and re-seeds from `TableSetup`, so a decision not carried is a
decision silently retaken.

## Found by running it

- **Ordinary documents are untouched, and this is the evidence**: all **71 showcases byte-identical**,
  and on the production path an A4 table's header measures **22.7pt against an 802pt band** — 2.8%,
  against a 200.5pt cap. The five suite failures were all in ad-hoc harnesses that leave the adapter's
  `PixelsPerPoint` unpinned *and* use 200–300-unit pages, where a one-row header really is ~85 units,
  i.e. over a quarter. Their pages were raised rather than their expectations lowered: those tests are
  about proxies being made per page, not about repetition eligibility.
- **The quarter's denominator was wrong first time round.** It measured against the *content band*, on
  the reasoning that the band is what a repeated group actually costs its height out of. But §6.2 says
  "the page height" and it is a `must`-repeat rule, so the smaller denominator declines a group in a
  range the spec requires repeating — roughly 182pt–210pt on A4 with 20mm margins. Filed as #520 to be
  deferred, then pulled in-scope. `HtmlContainerInt.PageSheetHeight` is the quantity, and it needs no
  slot: only the margins vary per page, and the sheet they come out of does not — which incidentally
  removed the one place the settle step had to reason about *which* band the table began in.
- **The `<tfoot>` risk predicted before implementation reproduced, and was worth measuring rather than
  assuming.** Sweeping a declined footer's body cell in fours from 120 to 200 words: four consecutive
  documents put the footer 1.2pt past the band, and the 17 others did not. Bounded by about one row's
  height, self-correcting once the row itself moves on. Filed as
  [#518](https://github.com/jhaygood86/PeachPDF/issues/518) with a gap file rather than fixed, because
  both alternatives cost more — see it.

## Deliberately not done

- **No break-before-the-footer path.** It would move a *repeating* footer too, and that placement is
  #493's.
- **The band is the one the table begins in**, once per table, even under per-page `@page` geometry.
  Deciding per band would let a continuation disagree with the pass that detached the group.
- **Multicol is not re-asked.** Inside a column the table's fragmentainer is not a page, so §6.2's first
  condition ("if the page is the table's fragmentainer") arguably does not apply at all there; the cap
  uses `PageBandHeightOf` like the rest of the engine and nothing about column behaviour moved.
- The UA sheet's own `h1…h6 { page-break-after: avoid }` was respelt `break-after` alongside — no
  behaviour change (the two spellings share storage and initial value), and the byte-identical corpus is
  the proof. The `page-break-*` entries in `CssDefaults.InitialValues` stay: they are the alias registry
  that makes `initial`/`unset`/`revert` resolve on the legacy spelling.

## What the review pass found that 6,908 green tests did not

The habit #513 earned paid again, and this time against the *tests* rather than the code. Mutating the
source showed two gates that could be reverted with the **entire suite still green**:

- **`RepeatedFooterHeight` → the raw height.** The single change the repeated-group-cost invariant calls
  load-bearing, and four of the five gated subtractions are the footer's. Nothing failed. The header had
  `ATallHeaderThatDoesNotRepeat_…`; the footer had no counterpart, because the two are asked of opposite
  edges of the band and one test cannot cover both.
- **Both of `TakeBreakBeforeRow`'s gates.** Every fixture written first time round fragments *inside a
  cell*, so each later fragmentainer is opened by a continuation and `TakeBreakBeforeRow` never runs at
  all — while a break *between two rows* is the path an ordinary many-row table takes. A declined group
  would have been redrawn on every page of the commonest table shape there is, silently.

Both are now pinned, and both mutations were re-run to confirm they fail. The lesson worth keeping is
narrower than "test more": **a fixture that fragments mid-cell and a fixture that breaks between rows
exercise disjoint halves of this engine**, and the repeated-group work has now twice written only the
first kind.

Two smaller review findings were real too: `RepeatedFooterHeight` dropped the `> 0` clamp its
predecessor expression carried, so an empty `<tfoot></tfoot>` (which measures `-GetVerticalSpacing()`)
would have *added* room to a band; and the UA rule matches the **elements**, so a `<thead>` under the
common `table { display: block }` idiom now gets `break-inside: avoid` as an ordinary block — measured as
a 120pt group moving from page 0 to page 1. That is the property doing what it says, but it is a third
migration case and is now written down as one.

## Evidence

Full net8.0 suite **6917 passed / 0 failed / 9 skipped** (6895 before). CLI **96/96**. Zero-warning
`dotnet build PeachPDF.slnx -t:Rebuild`. Six of the ten behavioural assertions confirmed **failing on
`main`** with the change stashed, and the three `Repeats` assertions cannot compile there at all. 71 of
71 pre-existing showcases byte-identical, plus one new one — `paged_media_table_group_repetition`, two
tables of identical rows differing only in `break-inside`, which is both the demonstration of the new
author control and the two-renderer check this class of change wants. Tall-header, tall-footer,
transition and showcase documents rasterized with **both** PDFium and MuPDF and read.
