# A running band is not part of the flow `ActualSize` measures

`CssBox`'s layout epilogue grows `HtmlContainerInt.ActualSize` from every box it finishes, gated only
on `IsFixed`. A `position: running()` element is painted into a page margin box, bounded by the
margin band rather than the content box, so a full-width header band reported the document as
overflowing its own page — and `ShrinkToFit`, which reads `ActualSize`, scaled everything down to fit
content that was never in the flow.

The gate is now `IsFixedOrInRunningElement`: one ancestor walk answering both, because the epilogue
runs for every box and a second walk would be a measurable cost on a deep document.

## What running it turned up

- **Testing `ActualSize` directly proves nothing here.** Two fixtures built on the lightweight
  `LayoutHarness` — a wide running box, and a wide box nested inside a running box — pass against the
  pre-fix code as well as the fixed one; the epilogue's update only runs on the path the real
  generator takes. The fixtures that discriminate go through
  `PdfGeneratorLayoutHarness.LayoutWithRescaleAsync` and assert the rescale, which is also the
  reported symptom rather than a proxy for it.
- **`pixelsPerPoint` moves the opposite way to intuition.** It is the device scale the fit is
  expressed as, so shrinking the document *raises* it: 900pt of content into a 540pt content box
  comes out at ~1.68, i.e. drawn at ~0.6×. An unscaled document is exactly 1.0. The first version of
  the contrast case asserted `< 1.0` and failed against correct behaviour.
- **The width has to be on a CHILD of the running box to reach the defect.** The running box itself
  was already skipped by an earlier flow-exclusion path; its wrapper children each ran the same
  `ActualSize` update in their own right. A fixture putting the width on the running box passes
  either way.
- **Why not `IsExcludedFromFlow`.** It sits twelve lines away and answers an adjacent-sounding
  question — whether a box contributes to its PARENT's in-flow content — and is the wider set,
  including `IsOutOfFlow`. Reusing it here would also stop absolutely-positioned and floated content
  growing `ActualSize`, which is a much larger behaviour change than the defect. Both doc comments
  now cross-reference the other so the next reader does not have to re-derive why there are two.
- **`IsFixed` is `virtual` with no override today**, and this walk re-implements its
  `Position.Value == PositionMode.Fixed` test rather than calling it per ancestor (nesting two
  ancestor walks is quadratic). Noted in the doc comment: if an override is ever added, the two have
  to change together.

## Evidence

Two fixtures in `RunningElementLayoutIntegrationTests`, bracketing the behaviour: a running band
wider than the content box leaves the document unscaled (fails against the merge base), and the
identical wide box in the flow still shrinks it. Full suite green on net8.0, 0 build warnings.
