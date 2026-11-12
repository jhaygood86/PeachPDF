# A text-only multicol scroll container of bounded height is columnized (#1849)

The accepted gap's stated reason was already gone: since #1818 the columns engine fills columns of the definite height and
clips the tail, so wrapping the text no longer skips words. Only the second pin remained - a wrapped scroll container taller
than a page is fragmented rather than laid out whole. That matches the same container holding blocks, so the guard
(`!IsHeightBoundedScrollContainer`) in `DomParser.CorrectInlineBoxesParent` is removed.

**Behavior change:** a text-only `overflow: scroll|auto|hidden` multicol container taller than a page now fragments across
pages (several passes) instead of being one unbroken pass. `ScrollContainerEstablishingColumnsButHoldingOnlyInlineContent`
was rewritten to pin that and that every line is drawn exactly once. Evidence: Engine.Tests and PeachPDF.Tests pass.
