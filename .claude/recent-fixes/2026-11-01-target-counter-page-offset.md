# `target-counter(..., page)` follows `HtmlDocumentOptions.PageNumberOffset`

`CssContentEngine.AppendTargetCounter` resolved a target's page as the page index + 1, so in a `DocumentMerge` with continuous
numbering `counter(page)` was shifted and a table of contents built from `target-counter` was not (both read part-local numbers).
Both the resolved value and the pre-map placeholder now add `container.PageNumberOffset`. The section counters
(`PageNumberWithinSection`, `TotalPagesWithinSection`) subtract two local page numbers, so they need no offset.

Evidence: `CssContentEngineTests.ApplyContent_TargetCounterPage_CountsOnFromThePageNumberOffset` (placeholder) and
`FootnoteTargetCounterReflowIntegrationTests.TargetCounterPage_AfterTheLayoutConverges_IsShiftedByThePageNumberOffset` (full layout:
the resolved page is the plain one + 10). The offset is applied only to the number that is printed, never to the page map or the
convergence loop's signature, so non-merged documents are untouched.
