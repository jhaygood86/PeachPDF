# `SynchronousOnlyViolationException` must pass through every layer that wraps or swallows a resource failure

`HtmlDocumentOptions.SynchronousOnly` promises that a document needs nothing asynchronous; `AsyncGuard.Require` throws `SynchronousOnlyViolationException` (an `InvalidOperationException`) the moment a `ValueTask` on the way is not already complete.

**The trap.** The pipeline turns resource failures into something else in many places: `ImageLoadHandler.LoadImage` and `StylesheetLoadHandler.LoadStylesheet` wrap into `HtmlRenderException`, `SvgTreeBuilder` swallows a failed image fetch, and `CssBox.LayoutBlockChild`, the flex/grid/columns engines, `FragmentPainter` and `DomParser` wrap into `HtmlRenderException("Exception in box layout")`. Without a filter the host sees `HtmlRenderException` (not an `InvalidOperationException`) or, for SVG, a silently blank image: the contract the host relies on is gone. Each of those catches is `catch (Exception ex) when (ex is not SynchronousOnlyViolationException)`.

**Rule.** A new `catch (Exception)` that wraps or swallows a failure on the path from `GetResourceStream`/font loading up to `HtmlDocument.Load`/`Layout`/`PaintPage` carries the same filter, and a new place that awaits something that can suspend goes through `AsyncGuard.Require(task, container.SynchronousOnly, "what", resource)` first. `tests/PeachBrackets.Engine.Tests/Integration/SynchronousOnlyTests.cs` pins stylesheet, `@import`, web font and image.

**Related.** A `ValueTask` is awaited once and `.GetResult()` is legal only on a completed one: `HtmlDocument.Load/Layout/PaintPage` check `IsCompleted` first and throw rather than block. `HtmlDocument.ContentExtent.Height` is the whole page; use `GetContentHeight(page)` for the height of the content.
