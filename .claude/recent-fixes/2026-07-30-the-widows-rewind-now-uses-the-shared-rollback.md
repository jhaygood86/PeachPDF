# The widows rewind now uses the shared rollback

_Landed 2026-07-30._

**`HtmlContainerInt.TryRewindForWidows` now widens to the shared `PassRewind.RollBackTo`**
([issue #440](https://github.com/jhaygood86/PeachPDF/issues/440)), closing the one pass re-entry that
still did its own narrower thing: it used to discard only the widowed box's own line boxes past its
budget, leaving whatever the pass placed after that box exactly where it was rather than resetting and
re-laying it out. The other two pass re-entries (the columns engine's abandoned fill attempt, the
driver's keep-with-next run pull) already used the shared rollback; widows was the holdout, and its own
doc comment said why: widening it once measured a **16-word loss** from the `paged_media_horizontal_reflow`
showcase.

**The loss was never widows' own bug.** `git bisect` (fully clean rebuilds at every step — an
incremental-build artifact produced a false transition on the first attempt, caught by re-verifying both
endpoints manually before trusting the automated run) landed the fix at
[3a20fca](https://github.com/jhaygood86/PeachPDF/commit/3a20fca47468f6e6d05ae56780210544a75d668b), the
commit immediately after the one that introduced `TryRewindForWidows`'s narrow-only behavior — fixing
[issue #433](https://github.com/jhaygood86/PeachPDF/issues/433), not #440. #433's bug: a word a stopped
flow never reached kept a stale position (document Y 0, or whatever an earlier discarded attempt left on
it), which lies inside the *first* page's own band, so an earlier fragment wrongly claimed it. The fix
makes a block's inline flow say `AwaitPlacement()` of itself at the start of every fresh flow. Once that
landed, the specific loss `TryRewindForWidows`'s widening depended on stopped reproducing — it was #433's
stale-position bug surfacing through the shared rollback's later-sibling reset, not a defect in the
rollback itself.

**Verified on the platform the loss was originally measured on.** The issue's own investigation
(recorded separately, [2026-07-30](2026-07-30-the-no-progress-recoverys-two-remaining-holes-closed-the-third-stays-open.md))
already tried three escalating reproductions on Linux and could not reproduce the loss there — Georgia
isn't installed in that sandbox, so font substitution changes the exact line-wrap shape the defect
depended on. This fix was verified on Windows with Georgia actually installed: a standalone repro against
`PdfGenerator.GeneratePdf` confirmed the widows rewind still fires (3 times, budget 2, against the
showcase's `<p>` boxes) and produces byte-for-byte identical per-page text with the narrow rewind and the
naive shared rollback both — no loss, on the exact machine/font combination the original report used.

**What running it bought over reading it.** Reading the code alone would only show that `RollBackTo`
resets later siblings outright (`ResetForRefill`) rather than leaving them be — indistinguishable, by
inspection, from the version that lost content. Running the full showcase corpus (regenerated and
text-compared with PyMuPDF, both against the narrow rewind at the same commit and against `origin/main`)
is what shows the loss doesn't recur anywhere, not just in the one showcase the issue named.

**No new regression test.** Three attempts at a small, deterministic fixture that would fail under the
narrow rewind and pass under the shared one — a single widowed paragraph with one sibling; 40 paragraphs
at `orphans:4`/`widows:4` under per-page reflow; 20 four-line paragraphs with mirrored per-page margins on
a small page — each produced *identical* output under both old and new code. The actual defect needed 70
real paragraphs, real Georgia metrics, and mirrored per-page margins to manifest; it does not reduce to a
small fixture, the same shape #438's own dev note already recorded for a sibling defect
(["What running it bought, and what reading it would not have"](2026-07-27-the-driver-rolls-the-box-tree-back-when-it-re-enters-a-pass.md)).
Relying instead on the unmodified, still-passing `OrphansWidowsIntegrationTests.cs` suite plus the
full-corpus text comparison as evidence.

Tests: full net8.0 suite green (7355 passed, 0 failed), 100% diff coverage against `origin/main`,
zero-warning solution rebuild. All 78 showcases text-identical (PyMuPDF-extracted) against both the
narrow-rewind build at the same commit and against `origin/main`.
