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
- Full `net8.0` suite: 8744 passed, 0 failed, 9 skipped.
- Diff coverage: 26/26 changed executable lines (100%).
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings.
