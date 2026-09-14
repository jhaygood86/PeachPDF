# line-clamp: vertical writing mode, cross-formatting-context line counting, block-ellipsis value, first-line font, no -webkit-box alias

Tracked as **#1051** (re-scoped from its original filing - see "What changed" at the bottom). Deliberate
v1 scope decisions for `line-clamp` (CSS Overflow Module Level 4 §line-clamp - a shorthand there for
`max-lines` + `continue: discard` + `block-ellipsis`, modeled here as one property; CSS Overflow Level 3
explicitly deferred the feature to Level 4).

## No clamping at all in vertical writing modes

`CssLayoutEngine.TryApplyLineClamp` is only ever called from `FlowBox`, the horizontal-line-flow engine.
`CreateVerticalLineBoxes` (real `vertical-rl`/`vertical-lr` line flow, issue #768) is a separate
implementation that never calls it, so `writing-mode: vertical-rl` combined with `line-clamp` produces
**no truncation whatsoever** - every line lays out, the block never shrinks, and no ellipsis appears.
Porting the cutoff (and the per-word popping/ellipsis-generation it drives) to `CreateVerticalLineBoxes`'s
own column-stacking loop is real, separate work from the horizontal implementation, not a small follow-up.

## Bidi/RTL: line counting and height work; ellipsis placement does not

Counting lines and stopping layout early are not direction-dependent for *horizontal* flow -
`line-clamp` correctly limits a block's visible line count and shrinks its height under bidi/RTL text
exactly as it does for plain LTR content, since `blockBox.LineBoxes.Count` and the early-return from
`CssLayoutEngine.FlowBox` don't care which direction the content flows. What isn't yet handled is the
generated ellipsis word's own placement *within* a mixed-direction line - `text-overflow`'s paint-time
mechanism already carries real bidi-aware anchor/boundary math
(`ResolveInlineEndBoundary`/`LeadingEdge`/`Forward` in `FragmentPainter.TextOverflow.cs`) that
`TryApplyLineClamp`'s much simpler "measure from the line's own left/right edge" placement does not yet
replicate, so the ellipsis can land at the visually wrong end of an RTL line even though the line count
and height are both correct.

## `max-lines` counts one block container's own lines, not the whole block formatting context

CSS Overflow 4 §max-lines: "a region break is forced after its Nth descendant in-flow line box... Only
line boxes in the same Block Formatting Context are counted; contents of descendants establishing
independent formatting contexts are skipped." `TryApplyLineClamp` reads `blockBox.LineBoxes.Count` -
the line boxes `CreateLineBoxes` produced directly for that one box - which is correct as long as
`line-clamp` is declared on a box whose entire inline content stays within its own formatting context
(the common case: a `<p>`/`<div>` of plain inline content). It undercounts if a descendant establishes
its own new formatting context that itself produces line boxes with a nested inline layout (e.g. a
`display: flow-root`/`overflow: hidden` descendant, or a floated/inline-block child holding its own
multi-line text) - those descendant line boxes are invisible to the ancestor's own `LineBoxes.Count`
today, whereas the spec only means to *exclude* them, not to make the ancestor's own count wrong by
their absence in a case where it matters. In practice this only diverges from spec behavior for the
(uncommon) case of `line-clamp` on a block whose direct inline content is itself interrupted by a
nested formatting context; plain-inline-content blocks (everything this feature's own tests and
showcase exercise) are unaffected.

## The two-value `<integer> <block-ellipsis>` grammar is not accepted

Full CSS Overflow 4 grammar: `none | <integer [1,∞]> <'block-ellipsis'>?`, where `<'block-ellipsis'>` is
`none | auto | <string>` (the marker text; `auto` is the UA default, typically "…"). `LineClampConverter`
(`Converters.cs`) only accepts the bare `none | <integer>` form - `line-clamp: 3 "--"`/`line-clamp: 3 auto`
fail to parse entirely, and (like any invalid declaration) the property falls back to its inherited/
initial value rather than clamping with the default ellipsis. `line-clamp`'s own value shape
(`CssKeywordOrValue<NoneKeyword, int>`, mirroring `z-index`) has no room for a second component without
either a real `block-ellipsis` longhand of its own or a compound value type - both larger changes than
fit alongside the line-count cutoff this v1 implements. A document author who writes the single-value
`line-clamp: <integer>` form (by far the common usage, and what every browser's own `-webkit-line-clamp`
heritage popularized) is unaffected.

## The ellipsis is always measured in the block's own font, not `::first-line`'s

`TryApplyLineClamp` always uses `blockBox.ActualFont`/`ActualTextShapingFeatures`. When line 1 (and,
for a one-line clamp, the *only* visible line) is styled by a `::first-line` rule with its own
`font-size`/`font-family`, the words on that line are correctly measured and painted with the
first-line style (`FirstLineStyle`/`RemeasureWordsTail`), but the generated ellipsis is not - it can
come out a visibly different size/face than the real text it sits beside on a `line-clamp: 1` block
with a `::first-line` override.

## No `-webkit-box`/`-webkit-line-clamp`/`-webkit-box-orient` legacy alias

Many real-world documents still use the pre-standardization idiom
(`display: -webkit-box; -webkit-line-clamp: N; -webkit-box-orient: vertical`) instead of the standard
`line-clamp: N`. This isn't recognized at all - `display: -webkit-box` is invalid-and-dropped at parse
time like any unrecognized `display` value (`CssSheetIgnoreVendorPrefixes`), so a document using only
the legacy form gets no clamping whatsoever. Supporting it means teaching `display` computation to
recognize a value with no corresponding real internal `DisplayMode` (WebKit's own fake
flexbox-that-isn't-flexbox), which is its own design decision, separate from - and larger than -
implementing the standard property.

## What changed from the original filing

The original version of this note (and of issue #1051) described word-granularity truncation itself as
a v1 simplification of `text-overflow`'s character-level truncation. On closer reading of CSS Overflow 4
§block-ellipsis, word/soft-wrap-opportunity granularity is not a simplification - it's the **specified**
placement rule ("after the last soft wrap opportunity that would still allow the entire block overflow
ellipsis to fit"). That section has been removed rather than carried forward here, and the five gaps
above (three genuine spec deviations not previously tracked, plus the pre-existing RTL and `-webkit-box`
notes) replace it.
