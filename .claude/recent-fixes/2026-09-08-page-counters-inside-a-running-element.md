# `counter(page)` inside a running element, and what re-resolving `content` drags with it

`CssContentEngine.ApplyContent` runs once, at DOM-construction time, and bakes its result into the
box's `Text`. For a running element that is right for everything except the `page`/`pages` counters,
which are UA-maintained per-page values `CssCounterEngine` knows nothing about — it answers 1 for
both — so a `position: running()` footer read "Page 1 of 1" on every page.

`RunningElementLayout` now re-resolves a subtree's `content` per page, against a
`RunningElementPageContext` the container sets while laying a running element out for one page.
`CssContentEngine` reads that context first for `page`/`pages` and falls through to the ordinary
document-counter lookup for everything else and whenever the context is null.

## What running it turned up

- **The suite passing proved nothing.** 10,207 tests stayed green because nothing exercised the new
  branch: no fixture put a `page` counter inside a running element. Coverage on the diff showed 0
  hits on `CssContentEngine.cs`'s new arm and on `RefreshPageCounterContent`'s body. `RunningElementPageCounterTests`
  now covers it, and four of its five fixtures fail against the merge base.
- **Re-resolving `content` re-runs more than the counter.** `ApplyContent` also resolves
  `open-quote`/`close-quote`, and `GetQuoteDepthAtStart` walks `ParentBox` and previous siblings
  **live** every call — there is no `FinalizedCounterNames`-style memoization the way there is for
  counters, which is what makes a repeat counter lookup safe. Run after the running box is
  reparented onto its throwaway `syntheticContainer`, that walk sees a box with no ancestors and no
  siblings. The refresh therefore runs *before* the reparent. Being precise about the evidence: I
  could not build a document where the ordering changes the output, because a declaration that opens
  and closes its own quote starts at depth 0 whatever it is parented to, and a quote opened outside
  a running element has no well-defined depth inside one anyway. The reorder costs nothing and
  removes a live dependency on a scratch parent; it is not backed by a failing fixture.
- **`pages` is the real final count, not an estimate.** `RunningElementPageContext` is populated from
  `HtmlContainerInt.LayoutMarginBoxes`, which runs once after `_emitter.Finish()` has materialized
  the fragment tree — not inside the per-page reflow/named-page convergence loop. So `totalPages` is
  `tree.Fragmentainers.Count` at its final value, and the refresh cannot perturb that loop's
  fixpoint detection.
- **The textual pre-check is looser than its first doc comment claimed.** `MentionsPageCounter` is
  "mentions `counter` and the substring `page`", so `counter(page-count)` matches too. Harmless, but
  the comment said a false positive "only costs a re-resolution that produces the same string",
  which is only true because of the counter memoization above — worth stating rather than asserting.

## Evidence

`RunningElementPageCounterTests`: `counter(page)` reading 1..N across N pages, `counter(pages)`
reading N on every page, the combined "N of M", an ordinary document counter as the contrast case
(it must keep its DOM-time value, or the assertions above would also pass if every counter were
captured), and quotes around a page counter. Four fail against the merge base. Full suite green on
net8.0, 0 build warnings.
