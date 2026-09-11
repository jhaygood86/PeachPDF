# A raster `<image>` inside a `display:none` SVG's `<clipPath>` (referenced via `clip-path: url()`) doesn't resolve

`clip-path: url(#id)` on an HTML element (`SvgClipPathRegistry`) discovers a `<clipPath>` defined
anywhere in the document, including inside an `<svg style="display:none">` used purely as a
defs-only resource - which is never otherwise visited by layout. To read such a hidden `<svg>`'s
definitions, the registry force-calls `CssBoxSvg.EnsureDocument()` directly, bypassing the async
`<image>`-prefetch step (`SvgTreeBuilder.PrefetchImageResourcesAsync`) that normally precedes it
during `CssBoxSvg.MeasureWordsSize` - which a `display:none` box never runs, since `display:none`
boxes are skipped by layout entirely.

Consequence: a raster `<image>` element nested inside such a hidden `<clipPath>` silently
contributes no geometry to the clip (its network/file data was never prefetched). Basic shapes
(`circle`, `rect`, `path`, `polygon`, etc. - the overwhelmingly common `<clipPath>` content) are
unaffected. Filed as [issue #999](https://github.com/jhaygood86/PeachPDF/issues/999).
