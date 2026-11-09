# A footnote body paints its stacking-context descendants (#1690)

An inline-block or `<img>` with `transform` or `opacity < 1` (or a z-indexed positioned box) inside a `float: footnote`
body was never painted. Two causes, both needed:

- `StackingOrder.Flatten` skips children that `NeedsStackingHoist` and leaves them to the nearest *local ordering scope*.
  A footnote body is painted by `FragmentPainter.PaintDetached`, outside any page tree, and is not a scope, so nothing
  claimed the child. `Flatten` now takes `isDetachedRoot` (passed as `ReferenceEquals(fragment, _detachedRoot)`), which makes
  the detached root a scope. `DomUtils.IsStackingContextBox` is untouched (layout reads it). This also covers a margin box's
  content (css-page-3 3.1: a margin box always establishes a stacking context).
- `HasStackingHoistCandidates` is computed by walking `Root`, and footnote bodies are detached from it, so a document whose only
  candidate sat in a footnote left the gate false. `ComputeFlowFlagsWithFootnoteBodies` also walks `FootnoteCalls` bodies.

Oracle: Prince 17 paints every stacking-context inline-block in a footnote body in tree order (CSS 2.1 App. E); Chromium,
WebKit and Gecko do not implement `float: footnote`.

`SvgForeignObjectContent` paints its own container's root (`IsRoot` scope, gate computed from that tree), so it does not have
the bug. Tests: `FootnoteStackingContextPaintTests` (each effect painted once; fail without the change).

Noticed, not fixed: a `display:block` span as the first content of a footnote-body span drew none of its text in the test
harness (even with no effect); unrelated to this fix.
