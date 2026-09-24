# A float's own content, if taller than one page, overflows rather than continuing

Was **#1038**, "a float that follows inline content is placed on the next line, not beside it" -
that shape is fixed: a float sharing a box with surrounding inline content (whether it precedes or
follows the content around it) now stays in the same inline formatting context, and browsers-match
placement (CSS 2.1 §9.5 / §9.5.1 rule 6) applies uniformly regardless of source order. What remains,
narrower than the original gap, is pagination-only.

## What remains

`CssLayoutEngine.FlowBox`'s dispatch branch for a floated child (`FlowFloatChild`) positions the float
directly from the current line and then lays its own content out through
`CssBox.LayoutContentAtItsAssignedPosition` - the same entry point `FlowAtomicBlockContentChild`
already uses for an inline-block holding real block-level content. Neither call site threads a nested
`PendingBreakToken` any further: if the float's own content is taller than the remaining
fragmentainer, the content that does not fit is never laid out at all - it silently ends, rather than
continuing onto a later page.

```html
<div style="width: 300pt; font: 16px monospace">
  Before <span style="float:left; width: 50pt;">…many lines of text, taller than one page…</span> after
</div>
```

**It is not only a float taller than a page.** Measured while fixing #1321: a 100pt float (five 20pt
lines) whose top sits 85pt above a page's band end keeps F1–F3 and loses F4–F5, with `overflow: visible`,
among inline content or alone in its parent. Nothing moves a straddling float to the next page, so the
same loss hits any float that crosses a boundary. `MonolithicContent.BreaksInBlockFlow` keeps an
auto-height scroll-container float monolithic for exactly this reason. Monolithic, its content lays out
unbroken, so every line is placed, but the line on the boundary is drawn past the band (the
`StraddlingAutoHeightScrollContainerFloat_PlacesEveryLine` fixtures).

The float itself, "Before", and "after" are all placed correctly and safely - nothing crashes or
duplicates content - but the float's own overflowing lines are simply absent rather than resuming on
the next page. A float that sits wholly on one page is unaffected. A float that crosses a
page boundary is affected, whether or not it is taller than a page. The
*surrounding* document's own pagination is unaffected either way - a float's presence does not stop
the container it sits in from pausing and resuming across a page boundary the ordinary way (see
`PeachPDF.Tests.Integration.FloatLayoutRegressionTests.FloatAmidInlineContent_SurroundingContentResumesAcrossAPageBoundary_WithNoWordLostOrDuplicated`).

## Why it is not a small addition

Fixing this needs `InlineBreakToken` (or an equivalent) to carry a nested per-child continuation
record - comparable to `BlockBreakToken.ChildToken`'s existing "parallel flow" shape - so a later
fragmentainer pass can resume laying out exactly the float (or inline-block) that stopped, without
re-deriving its position or re-placing content already committed. `FlowBox`'s own per-word,
ordinal-based resumption model has no such slot today for "resume this one non-word child's content
in place." Filed as [issue #1201](https://github.com/jhaygood86/PeachPDF/issues/1201).
