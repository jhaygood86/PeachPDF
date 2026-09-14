# Declarative API v1: no standalone SVG, Placeholder, sectioned page numbers, or dashed lines

Tracked as **#1060**. Deliberate v1 scope decisions for the declarative document-building API
(`PdfGenerator.CreateDocument`/`PeachPDF.Layout`), made to keep the initial change reviewable and
well-tested rather than defects found afterward. None of these are spec deviations - the feature has
no CSS/HTML spec to deviate from, being a code-first API layered on top of PeachPDF's own existing,
already-compliant engine.

## No standalone SVG

`IContainer` has no `Svg(string)`/`Svg(Stream)`/`Svg(byte[])`/`Svg(Func<PdfSize,string>)` terminal
methods. PeachPDF already supports standalone SVG parsing/rendering outside an HTML host (see
`docs/supported-svg-features.md`), so this is pure wiring rather than new engine work - just not done
yet.

## No Placeholder

`IContainer` has no `Placeholder(string? label = null)` terminal method. Unlike every other terminal
method in this API, a placeholder has no CSS property behind it at all (it is a pure prototyping aid,
not a document-semantic element), so it needs a genuinely new `IFragmentContentPainter`
(`Html/Core/Paint/Content/`, per `CLAUDE.md`'s "Paint lives in `Html/Core/Paint/`" convention) drawing
a fixed gray rectangle plus a centered label - real new paint code, not a wrapper over an existing
property.

## No sectioned page numbers

There are no `BeginPageNumberOfSection`/`EndPageNumberOfSection`/`PageNumberWithinSection`/
`TotalPagesWithinSection` methods - page numbers scoped to (or counted from) a named document section.
`ITextSpanContainer.CurrentPageNumber()`/`TotalPages()` (unscoped, document-wide) already exist and are
fully tested (`DeclarativeApiIntegrationTests.Footer_CurrentPageNumberAndTotalPages_ResolvePerPage`),
backed by `RunningElementLayout.RefreshPageCounterContent`/`HtmlContainerInt.RunningElementPageContext`.
The sectioned variants would additionally need `target-counter()` (`CssContentEngine.AppendTargetCounter`,
already implemented and tested for the HTML path) and a "section marker with a stable id" concept the
declarative builder doesn't yet expose.

## No dashed-line pattern

`IContainer.LineHorizontal`/`LineVertical` only support a solid fill (optionally via
`BackgroundLinearGradient`-style gradients, since they're filled boxes, not stroked borders). There is
no CSS property that maps directly onto "a dashed line" for a filled box the way there is for a
bordered one; a `repeating-linear-gradient` along the line's own axis could approximate a dash pattern,
but this was never attempted.

## Two smaller, narrower gaps

- `PdfTextDirection` only has `Ltr`/`Rtl` - no `Auto`. Real HTML's `dir="auto"` first-strong-character
  detection already exists (`DomParser.ResolveAutoDirectionality`) but isn't wired up for a hand-built
  declarative tree, which has no `<html dir>` to read one from in the first place.
- `IContainer.ClampLines(int lines)` has no custom-ellipsis-string parameter, because the underlying
  engine feature (`line-clamp`, CSS Overflow Module Level 4) only implements the default `"…"` ellipsis
  - there is no separate `block-ellipsis` property in this codebase to set a custom one with (see
  `.claude/accepted-gaps/line-clamp-vertical-writing-mode-bfc-counting-block-ellipsis-value-and-webkit-alias.md`
  for the underlying gap this inherits from).

## Why out of scope for the initial declarative-API change

Table, Lists, Header/Footer with page numbers, and the full style-decorator vocabulary (padding,
border, background/border gradients, shadow, corner radius, alignment, hyperlinks, bookmarks, rich
text with decorations/spacing/subscript-superscript/font-features/direction/paragraph
indent-spacing/line-clamp/inline-element-injection, images including a reusable `PdfImage` wrapper) are
already a very large single change. Each item above is either genuinely new engine-adjacent work
(Placeholder's paint primitive, the section-marker concept) or a smaller wiring task not yet done
(SVG, dashed lines, `Direction.Auto`) - real, useful follow-ups rather than blockers for a first,
solid, thoroughly-tested core.
