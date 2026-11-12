# A float or inline-block multicol container balances its columns (#2341)

**Cause (probed, not guessed):** a float or inline-block is laid out with the fragmentainer detached
(`LayoutContentUnbroken`, `FlowAtomicBlockContentChild`), so `CurrentFragmentainer` is null when the columns
engine is reached. Its column contexts were built with `inheritsSuppression: true`, i.e. `_fragmenting =
container.IsFragmenting` = false, and every column-break arm in `LayoutBlockChildren` (gated on
`IsFragmenting && HasOwnBand`) was skipped. The balance target was right (40pt); nothing could break at it.

**Fix:** `CssLayoutEngineColumns.RootsItsOwnFormattingContext` - a float/inline-block columns box reached
with no fragmentainer owns the scope, so its column contexts do not inherit suppression and its page budget is
`HtmlContainerInt.ContinuousBandPoints`. The budget is what keeps the invariant that a break recorded in a
suppressed scope the enclosing engine never reads drops content: with a continuous band the fill never leaves
a carry, so no `PendingBreakToken` can escape.

Not widened to flex/grid items or table cells: their measurement passes are detached at provisional positions.
Evidence: `AnAutoHeightContainer_BalancesItsColumns_WhateverFormattingContextItIs` (float, inline-block failed
before; block and overflow:hidden were already right); full Engine.Tests and PeachPDF.Tests pass.
