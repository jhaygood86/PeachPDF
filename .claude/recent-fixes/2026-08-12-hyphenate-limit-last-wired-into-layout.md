# `hyphenate-limit-last` wired into layout, with two real defects found and closed along the way

Closes [#723](https://github.com/jhaygood86/PeachPDF/issues/723).

## The load-bearing idea

The accepted-gap note this closes framed the problem as needing "orphans/widows-caliber
fragmentainer-boundary lookahead" — a full pass rewind like `HtmlContainerInt.RequestWidowsRewind`/
`TryRewindForWidows`. That turned out to be more machinery than the property actually needs. Unlike
`widows` (which depends on how many lines land *after* a break — not knowable until a later pass
completes), whether a hyphenated split's prefix ends up the last line before a fragmentainer break is
knowable **synchronously**, at the exact point `CssLayoutEngine.CreateLineBoxes` discovers the break
(`coordinates.Break is { } stopped`) and removes the doomed in-progress line from `blockBox.LineBoxes`:
whatever line is now `blockBox.LineBoxes[^1]` is, by construction, the line the break falls after — with
one caveat the review pass below found. A split's suffix always opens a fresh line of its own
(`TryHyphenateWord`'s whole design), so the discarded line's own first word carries the tell: a new
`CssRectWord.HyphenationPrefix` back-link (the suffix's mirror of the existing `HyphenationSuffix`/
`PreSplitWord` pair) identifies it. No pass rewind, no `HtmlContainerInt` involvement at all —
`CssLayoutEngine.EnforceHyphenateLimitLastBeforeBreak` does the whole thing inline in `CreateLineBoxes`,
merging the split back into the original word and adjusting the returned
`InlineBreakToken.ResumeWordIndex` down by one (the owner list is one entry shorter after the merge, so
the restored word is counted where the prefix used to be, not the suffix).

This also explains why `always`'s "last full line of the element" clause needed no separate code path:
a split's suffix always claims the position that would otherwise be a forbidden hyphenated last line —
either it fits right after the prefix (not a hyphenated line end at all) or it opens a new line, which
becomes the *new* last line. The only real case left is "last line before a break", which is what this
rewind targets.

## Defect 1: the livelock, found by running it

The first working version livelocked: laying out a 40px-wide, `hyphens: auto` paragraph with
`hyphenate-limit-last: always` against a 20pt-tall page (one line per page) burned through 100,000+ empty
pages before the fragmentainer-pass cap forced a monolithic fallback. Cause: when the hyphenated word is
its line's *only* word, that line was already a fresh, full-width one — the exact same width the next
fragmentainer's opening line offers. Undoing the split moves the whole word forward, where it hyphenates
again for the identical reason, whose prefix is again its line's only word — forever. The fix,
`EnforceHyphenateLimitLastBeforeBreak`'s `keptLine.Words.Count <= 1` guard: decline the undo whenever the
prefix is the line's sole word, since moving forward cannot gain the word any more room. This mirrors the
`orphans`/`widows` "nothing above it in the fragmentainer" give-up (`CssBox.TryKeepFewerLinesForWidows`) —
a constraint this engine cannot honor at all is better left unenforced than looped on. Left in place, the
hyphen stays, which is the only thing that lets the line ever close. The guard is a word-*count* proxy for
"already full-width," not an actual measurement — documented as such in `docs/html-css-support.md` rather
than overclaiming precision it doesn't have (a zero-width leading word or a line narrowed by something
other than ordinary content could in principle defeat the proxy in either direction; not pursued further,
since doing so properly needs the `RGraphics` width-measurement `TryHyphenateWord` itself uses).

Only reachable when the whole (unhyphenated) word cannot fit on any line the fragmentainer would offer —
a narrow-column-plus-long-word combination unusual enough that the direct unit test suite
(`HyphenateLimitLastEnforcementTests`) and the earlier manual page-height sweeps used to build the real
end-to-end fixture (`HyphenateLimitLastPaginationIntegrationTests`) both missed it until the sweep
happened to land on a one-line-per-page geometry.

## Defect 2: mutating a line an earlier pass already froze, found by an adversarial review pass

A post-change review (this repo's own convention: 8-angle multi-agent pass over the diff) independently
converged, from two different angles, on a second real gap: `keptLine = blockBox.LineBoxes[^1]` assumes
the kept line was built by the *current* pass. That's false whenever the current pass's own seed line —
the one that resumes a split's suffix from an earlier pass — is itself what gets discarded before
completing any line of its own. In that shape, `blockBox.LineBoxes[^1]` after the discard names a line an
**earlier** pass already ran through `FinalizeLineBoxes` (alignment, bidi reordering, `AssignRectanglesToBoxes`)
for, and that earlier pass's fragment may already be frozen and emitted. `CssBox.TakeEarlyBreak` and
`HtmlContainerInt.TryRewindForWidows` both call `HtmlContainerInt.InvalidateEmittedFragmentsFor` before
touching geometry that old, precisely to un-freeze it first; this synchronous, same-pass-only fixup had no
such machinery, so mutating `keptLine` in that shape would either operate on stale already-committed
alignment data or have no visible effect at all on a page paint had already rendered.

Reachable with `hyphenate-limit-last: page` inside a multi-column context: a hyphen right before a column
break is allowed (the value only forbids page breaks), so one pass leaves it in place and its fragment
gets frozen; if the very next column the flow resumes into is short enough that its first (seed) line
can't even hold the carried-over suffix, that pass discards its own seed line immediately, and
`blockBox.LineBoxes[^1]` is still the *previous* pass's already-frozen last line.

The fix: `EnforceHyphenateLimitLastBeforeBreak` now takes `completedLines` (the same count
`CreateLineBoxes` already tracks — how many lines existed when the current pass began, per
`CssBox.DiscardLineBoxesFrom`) and declines whenever `blockBox.LineBoxes.Count - 1 < completedLines`,
i.e. whenever the candidate "kept line" predates this pass. This single check also subsumes the old
separate "no kept line exists at all" null case (an empty `LineBoxes` gives index `-1`, always less than
a non-negative `completedLines`), so that branch was removed rather than kept redundantly.

## Cleanup: a real duplication, also found by the review pass

Three of the review's independent angles flagged the same thing: `EnforceHyphenateLimitLastBeforeBreak`'s
merge step (look up the prefix's index in its owner's word list, remove the suffix, splice the original
word back into the prefix's slot, mark it `AwaitsTheNextFragmentainer`) was a line-for-line copy of the
pre-existing `UndoAbandonedHyphenationSplits`'s own merge step. Extracted into a shared
`TryRestoreHyphenationSplit(prefix, original, suffix)` both now call, so a future fix to the merge
mechanics (e.g. something about how the dangling `PreSplitWord`/`HyphenationSuffix`/`HyphenationPrefix`
links on the now-unreferenced `prefix`/`suffix` objects are handled) can't be applied to only one copy.

## What didn't pan out

A precise page-height sweep to land a hyphenated prefix exactly on the last line of a page turned out to
be sensitive to more than just line-height arithmetic, echoing issue #344's own fix notes: pagination
itself (via `orphans`/`widows`'s default-active corrections) can shift *which* word ends up hyphenated at
a given page height, not just where the break falls. The working end-to-end fixture needed a short filler
word ("x") ahead of the long word so the hyphenation point sits mid-line rather than always opening a
fresh one — otherwise every candidate geometry either hit the sole-word-on-line decline (defect 1's guard,
not the general case) or never crossed a page boundary at all where a hyphen was present. The same
sensitivity showed up again building the TestHarness showcase: the filler height that worked in isolation
needed re-tuning once the showcase's own heading and intro paragraph were added ahead of it, since they
shift the exact line the content starts on.

## Evidence

- New `HyphenateLimitLastEnforcementTests` (15 cases): all five `HyphenateLimitLast` values × page/column
  break kind, the sole-word-on-line livelock guard, the cross-pass frozen-line guard (both the general
  case and the degenerate no-kept-line-exists case it subsumes), and three independent structural-invariant
  defensive branches.
- New `HyphenateLimitLastPaginationIntegrationTests` (4 cases): real `hyphens: auto` content laid out
  across a real page break — `none` allows a hyphenated last line, `always` forbids it, content is
  preserved (no lost/duplicated characters) either way, and the livelock fixture stays pass-bounded.
- New TestHarness showcase (`hyphenate_limit_last`) — visually confirmed via PDFium rasterization that the
  hyphen actually moves off the page boundary under `always` versus staying under the default `none`.
- 8-angle multi-agent post-change review (this repo's CLAUDE.md convention): found and fixed the two
  defects above and the duplication; other candidates (a doc line overclaiming the livelock guard's
  precision, an ordering dependency between this method and `UndoAbandonedHyphenationSplits` worth noting
  in comments, a showcase string duplicated across two columns) were addressed or judged non-actionable.
- Full `net8.0` suite: 8739 passed, 0 failed, 9 skipped (a `BoxDecorationBreakPaintIntegrationTests`
  failure seen on one earlier run reproduced on neither an isolated re-run nor two subsequent full-suite
  runs — pre-existing flakiness, not a regression).
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings.
- Diff coverage: 40/41 changed executable lines (98%) — the one uncovered line is the switch expression's
  compiler-mandated `_` arm, unreachable for any value the cascade can actually produce.
