# A word the flow never reached is not the first page's

_Landed 2026-07-27._

**A word the flow never reached is not the first page's** ([issue #433](https://github.com/jhaygood86/PeachPDF/issues/433),
[CSS Fragmentation 3 §4.1](https://www.w3.org/TR/css-break-3/#possible-breaks)). An ordinary
paragraph that breaks at the **first** page boundary had every word below the break claimed by page
0's fragment as well as by the page it really lands on. Measured on the issue's own fixture — a
3,000-word `<p>` on A4 at the production default 10pt margins — page 0's text layer held **2,999**
words where the page shows **1,153**.

**The load-bearing idea is that an unpositioned word is not a word at the origin, and only the flow
can tell the difference.** `CssRect`'s position starts at 0, document Y 0 lies inside the first
slot's own band, and `FragmentEmitter.BuildDraft` asks nothing but `region.Contains(word.Rectangle)`
and `AwaitsTheNextFragmentainer` — a flag §4.1's *discarded line* sets and nothing else did. After
the pass there is nothing left to read: a word that was never placed and a word placed at the top of
page 1 carry the same coordinates. So the statement has to be made **before** the attempt, which is
what `CssBox.AwaitPlacement` already exists to do for a discarded multicol fill. The block's
own inline flow now says the same thing about itself on the pass that opens it, and
`CssRect.Top`'s setter clears it per word — so what survives the flow is exactly what the flow did
not reach. One call; the mechanism was already there and self-healing, as the issue predicted.

**The guard is the whole of the care needed, and it is load-bearing.** Only a pass with
`resume is null` may mark: on a resumed pass the words below the resume point belong to a
fragmentainer already filled and frozen, and marking them takes back content another page
legitimately holds. Removing the guard fails **9** tests across the widows, keep-with-next and
multicol suites.

**What running it bought.** The bug does *not* reproduce in `LayoutHarness`'s default fixture, and
the reason is worth knowing before writing a fixture for anything in this family: the harness
defaults to a 20pt margin, so slot 0's band begins at document Y 20 and a word's zero rectangle
(height ≈ 13pt) does not overlap it at all. The defect is real at
`PdfGenerateConfig`'s **own default of 10pt** and at 0, and invisible above roughly one line height.
A fixture that picks its margin for tidiness rather than from production silently tests nothing here.

**The showcase found a second form of it that the issue did not describe, and it is the more
alarming one.** 68 of 69 showcases are byte-identical; `paged_media_horizontal_reflow` is the one
that differs, and it differs by **losing a whole line of the following page's text painted at the
foot of pages 2 and 4** — an interior page, not page 0. That document's per-page widths are settled
by a bounded reflow loop, so its boxes are laid out more than once, and a word the *second* layout
does not reach keeps the *first* one's coordinates: ordinary-looking coordinates in the middle of
the document rather than the origin. The fix covers both because it says "this layout has placed
none of these yet" rather than testing for a magic position. Page count unchanged (7), verified in
both PDFium and MuPDF.

**Deliberately not chased**, because it is pre-existing and identical on `main` (verified by running
the same fixture on both): a `<li>` whose own text straddles a page boundary can leave its
`::marker` in no fragment at all — 39 of 40 markers claimed in a 40-item list. An outside marker is
laid out by `CssBox.PerformLayoutEpilogue`, which the pass that breaks never reaches, so its word is
positioned only on the completing pass — after the slot it belongs to was frozen. Filed as
[#444](https://github.com/jhaygood86/PeachPDF/issues/444).

**`windows-latest` found a second defect, and it is not this one.** The claimed-exactly-once
invariant, asked of the whole document, failed there on a plain paragraph with **16 words claimed by
slots [0,1], living in 0** — one whole line, drawn again in the next page's top margin. That is a
tolerance mismatch, not an unplaced word: layout keeps a line overhanging the band bottom by up to
`PageBoundaryEpsilon` (0.5pt) while the emitter counts an overlap of `BandOverlapEpsilon` (1e-6) as
membership of the next band, and whether a page's last line lands in that window is a function of
the platform's font metrics. Filed as [#446](https://github.com/jhaygood86/PeachPDF/issues/446); the
fixtures here now pin their line geometry (a 20pt line against an 830pt band, so every page's last
line is 10pt clear) and turn `orphans`/`widows` off, so what they measure is which fragment claims a
word the flow never reached. **Making the diagnostic name the slots, the word and where it lives is
what turned one red check into a filed issue in a single CI cycle** — the assertion message is part
of the test.

**One residual the review chased and the numbers closed.** `AwaitPlacement` marks a subtree
strictly larger than `FlowBox` visits — an outside `::marker` is skipped by the flow — so the
question is whether anything is now newly *un*painted. Measured over seven shapes at 2,500 words
each, with and without the fix: duplicates go 1,330–1,397 → **0** in every shape that had any, and
the two shapes with an unclaimed word (a list's own marker, an absolutely-positioned inline's words)
report the **same** count either way. So the marking loses nothing; both residuals are pre-existing,
and the second one belongs to [#318](https://github.com/jhaygood86/PeachPDF/issues/318). The
regression guard for the class is `AListWhoseItemsDoNotBreak_StillClaimsEveryMarker`, which passes on
both builds by design — an item that does not itself break reaches its own epilogue on the same
pass, so its marker is positioned before the slot freezes.

Tests: `UnreachedWordClaimTests` (7 — #374's claimed-exactly-once invariant over the *whole*
document for five shapes, since what stops is the fill rather than the paragraph; the symptom stated
from the page grid rather than from the flag the fix sets; and the marker guard), plus
`EarlyBreakLayoutIntegrationTests.PulledRun_FromAPassThatResumedIntoAParagraph_ClaimsEachBlockWordExactlyOnce`
**promoted** from the two blocks the pull moves to the whole document. Removing the one call fails 6
of those 9. Full net8.0
suite green (6,640 passed / 6,649 total), CLI green (96); **100% diff coverage**; 0 warnings on
`dotnet build PeachPDF.slnx -t:Rebuild`.

The durable half is in
[.claude/invariants/fragmentation-an-unpositioned-word-is-not-a-word-at-the-origin.md](../invariants/fragmentation-an-unpositioned-word-is-not-a-word-at-the-origin.md).
