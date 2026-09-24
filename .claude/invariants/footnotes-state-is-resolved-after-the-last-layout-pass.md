# Footnote and page-float state must be resolved after the last `LayoutDocument` call

`FootnoteAreaHeightsBySlot`, the per-slot call grouping, `TopFloatAreaHeightsBySlot`, `BottomFloatAreaHeightsBySlot`
and `PageFloatPlacements` describe one particular layout of `Root`. `AttachFootnoteAreas` reads them after
`PerformLayout` finishes, so **any code path that calls `LayoutDocument` again has to run
`ResolveFootnotesForThisAttempt`/`ResolvePageFloatsForThisAttempt` after it** - or the state describes a layout the
tree has since left, and an area is attached to a page its call is not on, or dropped.

The footnote loop in `PerformLayout` already ends on a resolve. The `target-counter(_, page)` loop is the second
place that relays out, and it did not (#757): 55 of 77 swept layouts of a toc-plus-footnote document lost a note
area. A new reflow loop (another convergence pass, a per-page reflow) needs the same resolve as its last step.

Do not resolve *before* the loop and rely on it having settled: the target-counter reflows are what change the
pagination. And do not "probe" with a resolve after the loop and then re-enter the footnote loop - the probe
consumes the change signal, so the re-entered loop's first resolve reports "unchanged" and exits without relaying
out. See [the fix](../recent-fixes/2026-09-24-footnote-state-is-resolved-after-the-last-reflow.md).
