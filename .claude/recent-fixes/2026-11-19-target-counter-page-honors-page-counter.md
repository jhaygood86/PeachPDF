# `target-counter(..., page)` follows the `@page` page counter (#2433)

`AppendTargetCounter` already read `HtmlContainerInt.PageCounterFor`, but `PageCounterValues` was only built in `LayoutMarginBoxes`, after the target-page
convergence loop, so during the loop it was null and the value fell back to page index + 1 + offset. The fix rebuilds `PageCounterValues` from each pass's
provisional fragment tree (`BuildPageCounterValues`) right after `TargetPageMap` is set, before `ResolveTargetPageContent`. The existing signature check
already makes the loop converge on the resulting text; `LayoutMarginBoxes` still rebuilds the values from the final tree.

Trap: `BuildPageCounterValues` also assigns `PageLabelRanges`; the provisional value is overwritten by the final build, so it is harmless.

Evidence: `TargetCounterPage_FollowsThePageCounterRulesOfAtPage` (reset/set/increment on `@page :first`) fails without the change; full engine suite passes.
