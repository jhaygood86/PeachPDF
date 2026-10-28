# Resuming layout from the changed page (text edits)

`HtmlDocument`'s text edits now lay out again from the pass before the one that placed the edited paragraph instead of from page one.
`HtmlContainerInt.TryResumeLayoutAt` does what the in-run keep-with-next rewind does (`InvalidateFrom`, `PassRewind.RollBackTo`, `TruncatePassEntries`) between
two layouts, over the retained emitter; `LayoutDocument` takes `(resumeEmitter, resumeSlot, resumeToken)` and skips its once-per-layout resets when resuming, and
the tail of `PerformLayoutOnePass` is `FinishLayout`, shared by both. `TextEditResult.ResumedFromPage` reports it.

**Found by running it** (all three are in `.claude/invariants/fragmentation-a-resumed-layout-keeps-the-generation-so-it-must-rearm-what-a-new-one-would-reset.md`):
spent widows/orphans latches, ancestors' stale `ActualBottom`, and `ActualSize`. Each showed up only as a difference from a fresh load - the first as one line
at a page top after a *second* resumed edit, the others as a content extent too big by one paragraph - which is why every resume test compares with a fresh load.

**Measured** (Release, net8.0, 1,500 paragraphs, 84 pages): edit on the first pages ~700 ms (whole layout, includes JIT), paragraph 200 175 ms, 700 226 ms,
1,000 51 ms, 1,400 77 ms. The spike's 2.5x "re-running layout on the same tree is slower than the first" is why the saving comes only from skipping earlier pages.

**Not done**: resuming for documents the guard declines (see the accepted-gap file), and keeping the pages before the resume point's *painted output* (the result
reports `ResumedFromPage`, and an embedder can keep its bitmaps for the earlier pages).

**Evidence.** `TextEditTests` 36 incl. late-page insert, four resumed edits in a row, split/join/delete on late pages, each equal to a fresh load; engine suite 16,523.
