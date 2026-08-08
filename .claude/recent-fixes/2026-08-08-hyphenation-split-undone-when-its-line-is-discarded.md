# A hyphenation split made for one fragmentainer pass no longer survives into the resumed pass

Closes [#344](https://github.com/jhaygood86/PeachPDF/issues/344).

## The load-bearing idea

`CssLayoutEngine.FlowBox`'s hyphenation branch (`TryHyphenateWord`) mutates the owning box's
`CssBox.Words` list **in place**: `b.Words[wordIndex] = prefixWord; b.Words.Insert(wordIndex + 1,
suffixWord);`. A resumed fragmentainer pass re-walks that same list from the top, so a split made
against one pass's remaining line width was still there, unchanged, on the next pass - even when the
line it was made for is the exact line `CreateLineBoxes` discards at the break (css-break-3 §4.1: a
line box never straddles a fragmentainer, so the whole in-progress line is thrown away and rebuilt by
the resumed pass). The discard already re-flows every other word in that line fresh; the split
prefix/suffix pair was the one piece of state that didn't reset, so it could show up as two words
(with a needless hyphen) on the resumed page where the whole original word would have fit against the
fresh, undivided width.

The fix: `CssRectWord` gained two internal links (`PreSplitWord`, `HyphenationSuffix`) that
`TryHyphenateWord` now sets on the prefix it creates, pointing back at the original whole word and at
its own suffix half. `CreateLineBoxes`'s discard path calls a new
`CssLayoutEngine.UndoAbandonedHyphenationSplits(discardedLine)` before marking the discarded line's
words as belonging to the next fragmentainer: for any word in that line that is a hyphenation prefix,
it removes the suffix from the owner box's `Words` list and replaces the prefix's slot with the
original word, so the resumed pass encounters the whole word again and decides fresh whether to
hyphenate at all.

## The other half that wasn't obvious from the issue text

The restored original word has never been positioned this pass, so `Top`/`Left` are whatever it last
carried - nothing, since the split moved it out of the flow before it was ever reached. Left alone,
that "nothing" is indistinguishable from a genuinely-placed word to
`FragmentEmitter`'s `if (box.Words[i].AwaitsTheNextFragmentainer) continue;` guard (`TryGetWordRect`
falls back to `word.Rectangle`, i.e. whatever stale/default rect the object carries, when there is no
snapshot). The loop that sets `AwaitsTheNextFragmentainer = true` for the rest of the discarded line's
words runs against the line's own word list, which still names the discarded *prefix* object, not the
word that replaced it - so the restored word needs the flag set explicitly, in the undo method itself,
or it could be wrongly claimed into the fragment that is about to close.

## Which shape was actually reachable

Only a prefix ending up as a member of the discarded line's own word list triggers the undo - which
happens when `word.WouldStraddleFragmentainer()` (checked immediately after the prefix is placed,
before the loop ever reaches the suffix) trips on the prefix itself. The other shape - a prefix already
committed to an earlier, kept line, with only its suffix alone starting the fragmentainer that gets
thrown away - is an ordinary hyphen straddling the break and is *not* touched: the suffix carries no
`PreSplitWord`/`HyphenationSuffix` of its own (only the prefix TryHyphenateWord creates does), so
`UndoAbandonedHyphenationSplits`'s pattern match never matches it, by construction rather than by an
extra check.

## What didn't pan out

A pixel-perfect end-to-end fragmentation repro turned out to be far more sensitive than expected:
plain horizontal line-wrapping geometry is entirely page-position-independent in this engine (the same
preceding same-line content reflows to the same width on every page), so a hyphenation decision made
in one pass and one made fresh on resume are provably identical unless *something* narrows the line
differently between the two passes - a float is the natural real-world cause, but getting a float's
absolute bottom edge to land inside the exact word's line while also clearing the next page's absolute
content top turned out to need a very narrow, fragile window of page-height values, not something
worth pinning a regression test to. The mechanism itself doesn't depend on floats at all - it depends
only on the prefix's own placement tripping the straddle check - so the regression coverage instead
exercises `CssLayoutEngine.UndoAbandonedHyphenationSplits` directly against hand-built words carrying
the same linkage `TryHyphenateWord` sets, which is deterministic and doesn't fight page geometry.

## Evidence

- New `HyphenationSplitUndoTests.cs` (5 cases): a prefix alone in the discarded line restores the
  original word and sets `AwaitsTheNextFragmentainer`; the same with other words surrounding the split
  in the box's word list; an ordinary (non-hyphenated) word is left untouched; the legitimate
  suffix-alone-starts-the-next-fragmentainer shape is left untouched; an empty discarded line is a
  no-op.
- Full `net8.0` suite (excluding an unrelated file with in-progress, uncommitted changes from a
  concurrent investigation into issue #395 that was already present in the working tree before this
  change and is unaffected by it): 8207 passed, 0 failed, 9 skipped.
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings.
- Coverage: all new/changed lines in `CssLayoutEngine.cs`/`CssRectWord.cs` hit at least once
  (`dotnet test --collect:"XPlat Code Coverage"`), including the real (non-test-only) call site inside
  `TryHyphenateWord` and the merge path inside `UndoAbandonedHyphenationSplits`.
