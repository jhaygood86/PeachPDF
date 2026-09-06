# Named-page reflow convergence detection now accounts for which name is active, not just which page

Previously, the per-page horizontal reflow loop's fixpoint check (`HtmlContainerInt.PageAssignmentSignature`)
compared only each box's numeric page index across re-passes. A document where a width change between
passes shifted a named-page transition onto a different physical page — while a box's numeric page index
happened to stay the same — could have been silently accepted as "converged" one pass early, before the
new page's own (different) named-page margins had actually been applied to that content.

The signature now pairs each box's page index with that page's own active named page, so such a case is
correctly detected as not yet settled and the loop takes the additional pass it needs (still capped at 3
iterations, same as before). No known document in this project's own test suite is affected by this in
practice — every fixture already converges on the loop's first iteration — so this is a robustness fix
for a case not previously known to be exercised, not an expected behavior change for existing documents.
