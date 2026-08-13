# `hyphenate-limit-lines`'s consecutive-hyphenated-line count now carries across a page/column break

Closes [#724](https://github.com/jhaygood86/PeachPDF/issues/724).

## The load-bearing idea

`CssLineBoxCoordinates.ConsecutiveHyphenatedLines` lives on the per-pass coordinates record that
`CssLayoutEngine.CreateLineBoxes` constructs fresh at the top of every fragmentainer pass, so a paragraph
resuming on the next page silently began that page's run count at 0 regardless of how many consecutive
hyphenated lines it had already produced right before the break — the exact mechanism the accepted-gap
note (deleted by this change) and the sibling fix for `hyphenate-limit-last`
([`.claude/recent-fixes/2026-08-12-hyphenate-limit-last-wired-into-layout.md`](2026-08-12-hyphenate-limit-last-wired-into-layout.md))
both already described. The fix mirrors `InlineBreakToken.FollowsForcedBreak` exactly: a new
`InlineBreakToken.ConsecutiveHyphenatedLines` field is populated with `coordinates.ConsecutiveHyphenatedLines`
at the one site that constructs a break token (`CssLayoutEngine.cs`, the `WouldStraddleFragmentainer` arm),
and the resumed pass's `CssLineBoxCoordinates` seeds from it the same way it seeds `FollowsForcedBreak`.

The discarded line-in-progress (the one that straddles and gets rebuilt whole by the resumed pass, per
css-break-3 §4.1) is deliberately excluded from the carried value: it never closed, so it has not folded
into the running count either way, and the resumed pass evaluates it fresh from scratch.

## The one real interaction, found by reasoning through the existing `hyphenate-limit-last` code, not by a failing test

`CssLayoutEngine.EnforceHyphenateLimitLastBeforeBreak` can *un*-hyphenate the exact line whose
hyphenation state the carried count already reflects: `keptLine` there is always
`blockBox.LineBoxes[^1]`, the same line `ConsecutiveHyphenatedLines` was folded from when it closed. If
`hyphenate-limit-last` forbids a hyphen there and undoes the split, the immediately-preceding line no
longer ends in a hyphen — a trailing run whose last member stops being hyphenated isn't a shorter run,
it's no run at all, so the resumed pass must start back at 0, not inherit the now-stale carried count.
Fixed by adding `ConsecutiveHyphenatedLines = 0` to the same `with` expression that already adjusts
`ResumeWordIndex` on the undo's success path. Left alone, `hyphenate-limit-last: always` combined with
`hyphenate-limit-lines` at a page boundary could have under-counted the resumed page's very first line as
still part of a run that the `hyphenate-limit-last` undo had just broken.

## What didn't need to change

`CssLayoutEngine.EnforceHyphenateLimitLastBeforeBreak`'s two *decline* paths (the sole-word-on-line guard
and the "prefix not found" defensive branches) return `stopped` untouched, which is correct as-is — the
hyphen stays in place either way, so the carried count it already holds is still accurate.

## Second real interaction, found by a post-change review pass

`HtmlContainerInt.TryRebuildForBudget` — the widows rewind (`TryRewindForWidows`, css-break-3 §5.4) that
gives back some of a box's own kept lines so the fragment after a break satisfies `widows` — rebuilds the
same `InlineBreakToken` this fix's carry rides on, adjusting `ResumeWordIndex`/`CompletedLineCount`/
`LinesKeptHere` for the smaller budget, but its `with` expression never touched
`ConsecutiveHyphenatedLines`. The rewind can change *which* line is last-kept without changing the
count, which is the same "stale unless recomputed" hazard `EnforceHyphenateLimitLastBeforeBreak`'s own
reset exists for, just reached by giving lines back instead of undoing a hyphen. Before this diff added a
consumer, the field simply didn't exist there to go stale — a genuinely new gap this fix's own carry
opened, caught by an 8-angle review pass (two independent angles converged on it) rather than a failing
test. Fixed with a new `HtmlContainerInt.TrailingHyphenatedLineCount(box, lastKeptIndex)` helper that
walks back from the new last-kept line counting hyphenated lines via `CssRectWord.PreSplitWord` (the same
structural tell `TryHyphenateWord`'s own split sets, not a hyphen-glyph text comparison, which a custom
`hyphenate-character` would defeat). `TryRebuildForBudget` was widened from `private` to `internal` to let
`HtmlContainerIntWidowsRewindHyphenationTests` exercise it directly against hand-built line boxes, the
same reason `EnforceHyphenateLimitLastBeforeBreak` is `internal` rather than `private` — an end-to-end
widows-plus-hyphenation-limit fixture would fight the same page-height sensitivity the `hyphenate-limit-last`
fix notes already found unusually fragile for this class of scenario.

`FollowsForcedBreak` has the identical staleness gap in the same rewind path, confirmed pre-existing (not
introduced by this diff) and left alone — this fix's scope is `ConsecutiveHyphenatedLines`, and
`FollowsForcedBreak` going stale here was already possible before this change (it just had no way to
matter until a page/column boundary's `text-indent: each-line` state ran into the same rewind).

## Evidence

- `HyphenateLimitLastEnforcementTests`: `BuildSplitAtABreak`'s token now carries a nonzero
  `ConsecutiveHyphenatedLines` so `AssertForbiddingValueUndoesTheSplit` can assert the undo resets it to 0
  — every other case in the file already round-trips the token unchanged via full record equality, so no
  further per-case changes were needed.
- New `HyphenateLimitLinesPaginationIntegrationTests` (2 cases): a one-line-per-page fixture where an
  unconstrained baseline confirms the second page's first line genuinely has a fresh hyphenation
  opportunity (not merely a residual with no candidates left), then `hyphenate-limit-lines: 1` forbids
  that same line from hyphenating given the first page's only line already spent the budget. Verified this
  test actually regresses (fails) with the seeding temporarily reverted to `0`, and with
  `EnforceHyphenateLimitLastBeforeBreak`'s new reset removed, before restoring both.
- New `HtmlContainerIntWidowsRewindHyphenationTests` (3 cases): `TryRebuildForBudget` giving back a
  non-hyphenated line raises the recomputed count, giving back a hyphenated line lowers it, and giving
  back every line recomputes to 0 — all verified to fail (with the exact stale-vs-recomputed values the
  bug would produce) when `TrailingHyphenatedLineCount` is temporarily bypassed in favor of the original
  stale carry, before restoring the fix.
- `BreakTokenTests`: two new facts (`InlineToken_DiffersInFollowsForcedBreak`,
  `InlineToken_DiffersInConsecutiveHyphenatedLines`) close a pre-existing gap the review pass found in
  `InlineToken_DiffersInAnyOfItsOwnFields` — its own doc comment claimed to cover "the rest of its own
  fields" but never varied the two non-`int` optional fields, so a future `Equals`/`GetHashCode` edit that
  dropped either would have passed silently.
- 8-angle post-change review (this repo's CLAUDE.md convention): found and fixed the widows-rewind
  staleness gap (independently confirmed by two angles) and the equality-theory coverage gap; a test-file
  helper duplication between this PR's new pagination test and the sibling `hyphenate-limit-last`
  pagination test was noted but left as-is (existing precedent already has each property's pagination
  fixture in its own file).
- Full `net8.0` suite: 8749 passed, 0 failed, 9 skipped.
- Diff coverage: 33/33 changed executable lines (100%).
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings.
