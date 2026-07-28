# A repeated header sits above the continuation, not on top of it

_Landed 2026-07-28._

[#439](https://github.com/jhaygood86/PeachPDF/issues/439), the last of the three
[#464](https://github.com/jhaygood86/PeachPDF/issues/464) unblocked. A repeating `<thead>` was already
being drawn on every page a mid-cell continuation reached; it was drawn **over** the first lines of that
continuation rather than above them. `FragmentainerContext` gains a `ResumeContentInset` and
`CssLayoutEngineTable` sets it, for the resumed row only, to the room the header just took.

## The finding that reframed the issue: its own measurement was stale

#439's closing comment reports the showcase's header text extracted per page as `Note=1 Clause=1` on
page 0 and `0 0` on pages 1 and 2 — the header not repeating at all. **Re-measured on merged `main`,
that is not what happens**, and it had already stopped happening at
[PR #488](2026-07-28-a-table-fills-one-fragmentainer-per-pass-and-is-resumed-in-the-next.md) itself:

| tree | pages | header per page |
|---|---|---|
| `145a639` (PR #488, merged) | 3 | **1, 1, 1** |
| `662c2f6` (`main` before this change) | 3 | 1, 1, 1 |

So the comment's numbers were taken from an earlier state of that PR's branch, not from what landed.
Starting from the showcase — which is exactly what the comment told the next reader to do — is what
found this out; starting from the issue's table would have sent the work at a symptom that no longer
existed. **A measurement in an issue is evidence about the tree it was taken on, and merges move that
tree even while the issue sits still.**

## What was actually left, and why no count could see it

Every word claimed exactly once, header on every page, and still wrong. The per-page census of
`paged_media_table_row_continuation` on `main`:

| page | clause words | range |
|---|---|---|
| 0 | 75 | 1–75 |
| 1 | 93 | 76–168 |
| 2 | 92 | 169–260 |

No word missing, none duplicated. But rasterizing it shows page 1 beginning at `clause82` and page 2 at
`clause175` — **`clause76`–`81` and `clause169`–`174` are drawn underneath the header's opaque
`background: #e2e8f0`**. Six words on each of two pages, present in the fragment tree, invisible on the
page. Both PDFium and MuPDF agree.

That is the ordering constraint #439's own "Why" section had predicted and no test asserted: *"the
header has to sit above the continuation of the cell, which means the cell's remainder has to be placed
after the header."* The repository's standing check — every word claimed exactly once — is satisfied by
a document that draws two of its lines under an opaque box, which is worth knowing about the check
rather than about this defect.

## The cause: the row cursor cannot speak for the one flow that matters

The row cursor reserves the header's height by advancing `CurrentY`. That positions the rows a pass
places and the cells it enters fresh — and reaches neither of the things a mid-cell continuation
consists of, because a cell continuing an earlier fragmentainer deliberately **keeps the `Location` its
first fragment was built from** ([the invariant](../invariants/fragmentation-a-continuation-may-not-move-geometry-an-earlier-fragmentainer-emitted.md)),
and its content goes wherever the flow puts it: `CssLayoutEngine.CreateLineBoxes` starts a resumed flow
at `FragmentainerContext.ResumeContentTop`, which was `BandTop`.

`cursor.CurrentY` after the header and `ResumeContentTop` were therefore **two names for two different
answers to the same question**, one of which had not been told about the header. The fix does not move
the cell (which would retract emitted geometry) and does not move the header; it tells the
fragmentainer that, inside this table, its content begins below what the table repeats there.
Additive and restored in a `finally`, so a table repeating a header inside a cell of another one owes
both, and a sibling resuming elsewhere in the same fragmentainer owes neither.

Scoped to `i == ResumeRowIndex` because that is the only row that can hold a resumed cell — a
continuation re-enters exactly the row the record names, and every row after it is entered fresh.

## Measured, `main` vs this change, same tree

| | `main` | this change |
|---|---|---|
| pages | 3 | **4** |
| fragmentainer passes | 3 | 4 |
| `LastResortRelayouts` / `PassRewinds` | 0 / 0 | **0 / 0** |
| header drawn on | all 3 | all 4 |
| words hidden under the header | **12** (6 on each of pages 1, 2) | **0** |
| words claimed exactly once | 260/260 | 260/260 |

One pass per page either way, and the no-progress backstop is never reached: the reservation moves
where a continuation starts, not how many times the driver has to ask.

The extra page is the point rather than a regression: the header genuinely consumes a band's worth of
room on every page it repeats onto, and the document that fits in three pages is the one that was
hiding two lines per page.

**69 of 70 showcases byte-identical** after normalizing creation date, `/ID`, subset tags, the
annotation `/M` and `/NM`, and PDFsharp's plaintext `% Creation date:`/`% Creation time:` header lines.
The one that changes is the one this is about. That the corpus is otherwise untouched is what says the
inset stayed inside the resumed row: the reservation is reachable only from a continuation of a table
with a repeating header, and nothing else in the corpus is one.

## What the review caught, and it was not a style point

**A reservation has to name the fragmentainer it was made in.** The first draft stored a bare
`double ResumeContentInset`. `FragmentainerContext.SlotIndex` is a *cursor* — `StepOverTo` moves it on
when a forced break is realized by placement — so a pass can leave the fragmentainer the reservation
was made in without the reservation being restored, and the page a forced break opens gets **no**
repeated header (the per-row header block is guarded `i > ResumeRowIndex`). The review reasoned this
out and could not build the fixture; a two-cell resumed row with a `break-before: page` in the second
cell produces it in one shot. Measured, on the page the break opens:

| | `main` | bare inset | slot-aware reservation |
|---|---|---|---|
| content top of the header-less page | 20.0 | **33.2** | 20.0 |

A 13pt blank strip at the top of a page, held for a header that is not there. Now
[an invariant](../invariants/fragmentation-a-repeated-groups-room-is-owed-to-the-flow-the-row-cursor-cannot-position.md)
in its general form — any state scoped to "the fragmentainer being filled" must record which one — and
pinned by `FragmentainerContextTests.AReservation_StopsApplyingOnceThePassHasSteppedPastItsOwnFragmentainer`,
which fails if the slot comparison is removed.

The other findings that changed the diff: the new generator harness returned a **disposed** container
(`HtmlContainerInt.Dispose` nulls `Root` and disposes every `CssImage`), which worked only because the
three tests happen to read neither; the `Func<ValueTask>` closure was replaced by an ordinary
parameterized method, matching the `DetachFragmentainer`/`RestoreFragmentainer` save-and-restore shape
every other fragmentation-state scope in this codebase uses; the spec citation was wrong (§2.1 is
*Table Structure*; the rule is **§6.2, `#repeated-headers`**, whose *"user agents must leave room"* is
the exact sentence this implements and is now quoted where it belongs); and "below … rather than under
it" in the docs said nothing, since those are synonyms.

## What was checked and not done

- **[#435](https://github.com/jhaygood86/PeachPDF/issues/435) and
  [#333](https://github.com/jhaygood86/PeachPDF/issues/333) were in scope and are not needed.** The
  going-in hypothesis was that the showcase's cell never straddles a band, so no break token is ever
  recorded and no continuation opens — the failure mode PR #488's note warns about. Measurement says
  otherwise: the showcase paginates through the break-token path at one pass per page, and the
  continuation machinery works. Neither issue is touched, and neither is what #439 was waiting on.
- **The `<tfoot>` half of the same sentence in the docs was false, and is now filed.** While verifying
  `docs/html-css-support.md`'s claim that "a table repeating a `<thead>` or `<tfoot>` repeats it above
  such a continuation", the `<tfoot>` half was measured and does not: `0, 0, 0, 0, 0, 0, 1` across
  seven pages, against the header's `1, 1, 1, 1, 1, 1, 1`, while a between-rows break gives both
  `1, 1, 1`. The two gates that would have to change are each load-bearing for a different reason, so
  it is a third case rather than a relaxation —
  [#493](https://github.com/jhaygood86/PeachPDF/issues/493), which has since been fixed the same way
  from the other end of the band — see
  [that note](2026-07-28-a-repeating-tfoot-closes-every-page-the-table-covers.md), which is where its
  gap file went. Worth noting that fixing the header's half is what would otherwise have made that
  sentence *look* verified.
- **§6.2's two *conditions* on repeating at all are still not applied**, and honouring "leave room" is
  what makes that expensive. The same section says a header repeats only where it carries
  `break-inside: avoid` and only while it costs under a quarter of the page; `_shouldRepeatHeaders` is
  "there is a `<thead>`". That was nearly free while the room was not actually reserved, and now costs
  the header's height out of every band. [#494](https://github.com/jhaygood86/PeachPDF/issues/494) —
  deliberately not taken here, because the `break-inside` half changes behaviour for every document
  with a `<thead>` and is what all the existing repeating-header tests and two showcases assert.
  *(Since closed: putting `thead, tfoot { break-inside: avoid }` in the UA print stylesheet made the
  strict reading behaviour-preserving, so both conditions landed together and the gap file is gone.
  See [that entry](2026-07-28-a-thead-repeats-only-where-6-2-says-it-may-and-the-ua-sheet-says-it-should.md).)*
- **Nested repeating-header tables still overlap each other.** The inset composes; the inner table's
  proxy *placement* does not — it lands at `Math.Max(startY, PageTopOf(ResumeSlotIndex))`, the band top
  the outer header occupies. The doc comment claims only what is demonstrated.
- **[#478](https://github.com/jhaygood86/PeachPDF/issues/478) is untouched and still visible here** —
  the finished `note` cell's borders still stop at the page boundary instead of running the
  continuation's depth. Its gap file stands.
- **[#432](https://github.com/jhaygood86/PeachPDF/issues/432) is untouched.** The row loop's band is
  still a counter; [the stale-cursor invariant](../invariants/fragmentation-a-stale-cursor-can-be-load-bearing-compensation-for-a-bad-estimate.md)
  says why correcting it alone regresses four tests, and it produces this issue's old symptom by a
  second route.
- **The header proxy's own creation was not touched.** It already runs on a continuation (the pre-loop
  block is not gated on `_continuesAPreviousPass`), which is what #488 made true and what the
  re-measurement above confirms.

## Evidence

Full net8.0 suite green (6,852 tests, up 8), CLI suite green (96),
`dotnet build PeachPDF.slnx -t:Rebuild` with zero warnings, `diff-cover` **100% over 33 changed lines**.
69/70 showcases byte-identical; the changed one rasterized with PDFium and MuPDF on every page and read.
The suite was run four times on the branch and three times on a clean `origin/main` before any of that
was believed — an earlier apparently-failing run was `git stash push` leaving untracked files behind,
not flakiness, and both trees are stable.

Tests: `TableCellBreakTokenTests.ARepeatedHeader_SitsAboveTheContinuationItRepeatsOver` over the same
two shapes its sibling repetition theory uses; a new
`TableHeaderRepetitionThroughTheGeneratorTests` over the showcase's own markup — the first test in the
suite to lay a document out the way `PdfGenerator` does, through the new
`TestSupport/PdfGeneratorLayoutHarness`; and three `FragmentainerContextTests` facts over the
reservation itself. Both geometry tests fail on `main` — the harness one at `word0036` starting at
Y 20 against a header bottom of 33.1, the generator one at `clause79` at 34.0 against 48.8 — and the
slot test fails if the slot comparison is removed. The two "already correct" assertions in the
generator class (header once per page, every word claimed once) pass on `main` too, deliberately —
they record what #488 achieved and guard it.

**What a future change should take from this:** the defect was invisible to the fragment tree's own
correctness check and to five green tests, and visible immediately on a rasterized page. It is the
second #439 finding in a row that only rendering the document produced, and the first one whose
starting evidence had gone stale in the issue.
