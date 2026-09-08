# A page counter that gains a digit re-stales its bidi levels

`RunningElementLayout.RefreshPageCounterContent` re-resolves a running element's `content` per page
so `counter(page)`/`counter(pages)` read the page they are drawn on. It called `ApplyContent` then
`ParseToWords` — and skipped the step between them that the other two content-re-resolution paths
already take.

`BidiLevels`, `CharScripts` and `JoiningForms` are indexed against the text the LAST resolution saw.
A page counter changes its own length the moment it gains a digit — `"9"` becomes `"10"` — so
`AppendWordsFromText` walked past the end of a stale array and threw `IndexOutOfRangeException`.
Any document with a running footer showing a page counter and **ten or more pages** crashed.

The fix is the one line `HtmlContainerInt.ReapplyPseudoElementContent` and `ResolveTargetPageContent`
both already carry, with the comment on the first one saying exactly why:
`CssBidiParagraphResolver.ResolveOwnTextAsParagraph(box)`.

## What running it turned up

- **The bug shipped because every fixture stopped at seven pages.** All five of the original
  fixtures run a document to about seven pages, where the counter is one digit from first to last
  and the stale array is coincidentally the right length. The defect needs page ten. A "Page N of M"
  feature tested only on single-digit documents is not tested.
- **It was found by rendering the real corpus, not by reading.** One production document died with
  `IndexOutOfRangeException` where pristine upstream had rendered it; bisecting the two files the
  page-counter change touched isolated it in one step.
- **The public repro is twelve lines.** A running footer with `counter(page)` over enough paragraphs
  to reach page ten — no customer content needed, which is what made it reportable.

## Evidence

`RunningElementPageCounterTests.CounterPage_PastTheFirstTwoDigitPage_DoesNotThrow` runs to 23 pages
and asserts the full 1..N sequence; removing the one added line reproduces the
`IndexOutOfRangeException`. Full suite green on net8.0 (10,253), 0 new build warnings. The corpus
document that crashed now renders.
