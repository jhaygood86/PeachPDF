# `transform` is ignored on a non-replaced inline box

Closes #1684.

## What was wrong

`DerivedStyle.HasTransform`/`ActualTransformMatrix` read only the computed `transform`, so every consumer (the paint
push in `FragmentPainter`, `DomUtils.IsStackingContextBox`, `FormsContainingBlockForFixed`, backdrop roots, flatten,
outlines) treated a plain `<span style="transform: ...">` as transformed. css-transforms-1 §2 makes only block-level,
atomic inline-level and table-internal boxes transformable. Headless Chrome measures the issue's example span as
66.9 x 15.8pt, unrotated; PeachPDF drew it rotated 45 degrees.

## The fix

One predicate, `DerivedStyle.IsTransformable` (not a non-replaced `display: inline` box, not a `table-column`/
`table-column-group`; replaced boxes always are), consulted where the value is *computed* rather than
in each consumer: `HasTransform` is false and `ActualTransformMatrix` is identity / `ActualTransform4` null for a box
that is not transformable. Paint, stacking context and containing block then agree without each asking.
`ActualPerspective` (0), `IsPreserve3dRequested` and `IsBackfaceHidden` take the same gate, because css-transforms-2
restricts `perspective`, `transform-style` and `backface-visibility` to transformable elements too.

## Traps

- `ActualDisplay` is already blockified for a `float` (`DerivedStyle.ActualDisplay`), and `position: absolute`/`fixed`
  are blockified in the cascade, which is what keeps such a span transformable. Do not read
  `Style.DisplayPositioning.Display` here: it is the raw cascaded keyword and would wrongly exclude them.
- `DomParser.CorrectBlockInsideInline` rewrites an inline box that holds a block child to `display: block` at parse
  time, before anything reads `HasTransform`. To the author it is still an inline box (a browser ignores the transform
  on `<a style="transform: ..."><div>..</div></a>`), so the parser marks it `CssBox.IsInlineBoxSplitByBlock` and
  `IsTransformable` honours that. Without the flag the original rotated while its split-off clones did not.
- `CssBoxObject.IsReplaced` is settled by `EnsureResolved` when the box is measured (`MeasureWordsSize`, from
  `PerformLayoutPrologue`, before the box's own content is laid out), and `HasTransform`, `ActualTransformMatrix` and
  `ActualPerspective` cache on first read, so the question is whether anything reads them first. The pre-layout
  `ComputeFlowFlags` call passes `includeStackingHoistCandidates: false`, which skips `NeedsStackingHoist`, the only
  read there; the post-layout calls run once `_isReplaced` is settled. So `<object>`/`<video>` ask `IsReplaced` like
  any replaced box (a resolved one transforms, one showing its fallback content does not, as in a browser). The one
  reader that can run during layout is `DomUtils.ClippingContainingBlockOf` (from `FragmentEmitter`); it only reads the
  ancestors of the box being clipped, which are already measured, so an `<object>`'s own cached value is final by then.
  No test covers that path for a `position: fixed`/`absolute` box inside an object's fallback content. A first attempt counted every
  `<object>` as transformable to dodge this; it was unnecessary and kept a spec deviation, so it was dropped. A
  `<video>` is the exception: a browser always treats it as replaced, but here it is a plain container until it has a
  `poster` (`CssBoxVideo`), so `IsTransformable` counts every `CssBoxVideo`.
- The existing `DomUtilsTests` stacking-context tests used a bare `<span>`; they now use a `<div>`.

## Evidence

The issue's example rasterized with PDFium: the span is 134 px wide at 2x (66.9pt, matching Chrome) and unrotated; an
`inline-block` with the same transform is still rotated. Unit tests pin the predicate for inline, `inline-block`,
`inline-flex`/`-grid`/`-table`, `float`, `position: absolute`, `block`, `<img>`, an `<object>` showing its fallback, an `<a>` wrapping a
block, table row/cell/table versus colgroup, `perspective`/`transform-style` on a span versus an `inline-block`, and a
`position: fixed` descendant's containing block; a paint test asserts no matrix is pushed for the plain span, and another that a resolved `<object>` (a data: PNG) is
transformed while an unresolved one is not. Removing
the `IsInlineBoxSplitByBlock` term makes the `<a>`-wrapping-a-block row fail. That row reaches only the second rewrite
site in `CorrectBlockInsideInlineImp` (an inline box normally arrives wrapped in an anonymous block, so the wrapper is
the `box` and the inline is its `Boxes[0]`); the first site, where the inline box is itself `box`, is set for
consistency and no test reaches it (deleting that line leaves the suite green). The `text <div>..</div> more text`
shape finds a split-off clone, which is inline anyway, so it does not exercise the flag either.
