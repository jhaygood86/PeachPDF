# Media/container query absolute-length comparisons ignore `PixelsPerPoint`

`MediaQueryMatcher.CompareLength` (shared with `ContainerQueryMatcher`) resolves a media/container-query
feature's length via the box-less `Length.ToPixels(...)` overload and compares it directly against
`MediaQueryContext.ViewportWidthPt`/`ViewportHeightPt` (from `HtmlContainerInt.PageSize`) or a container's
own size basis. Despite the `Pt` naming, that basis is PeachPDF's internal, `PixelsPerPoint`-inflated
layout coordinate space whenever `PdfGenerateConfig.PixelsPerInch` is set to a non-default value — so an
absolute-unit feature value (`(min-width: 600px)`) is compared unscaled against an inflated actual, and
can evaluate incorrectly.

This is the same bug class as issue #814 (general absolute CSS length resolution under non-default
`PixelsPerInch`), just not reachable through that fix's mechanism: `CssValueParser`'s box-aware
`ParseLength` overloads multiply an absolute length's result by the ambient `PixelsPerPoint`, but
`MediaQueryMatcher.CompareLength` has no `CssBox`/adapter in scope to consult, and `MediaQueryContext`/
`ContainerQueryContext` don't currently carry a `PixelsPerPoint` value for it to use.

Left out of #814's fix to keep that change bounded to the reported bug plus the two pre-existing bugs it
directly exposed while implementing the fix (see [.claude/recent-fixes/](../recent-fixes/) for that PR).
Tracked as [issue #820](https://github.com/jhaygood86/PeachPDF/issues/820).
