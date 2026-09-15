# Declarative API: no Placeholder prototyping element

Tracked as **#1072**. A deliberate scope decision for the declarative document-building API
(`PdfGenerator.CreateDocument`/`PeachPDF.Layout`), made during design review of the #1060 follow-up
series and dropped from it entirely rather than deferred as an oversight. Not a spec deviation - the
feature has no CSS/HTML spec to deviate from, being a pure prototyping aid with no document-semantic
meaning at all.

`IContainer` has no `Placeholder(string? label = null)` terminal method. Unlike every other terminal
method in this API, a placeholder has no CSS property behind it at all (it is a pure prototyping aid,
not a document-semantic element), so it needs a genuinely new `IFragmentContentPainter`
(`Html/Core/Paint/Content/`, per `CLAUDE.md`'s "Paint lives in `Html/Core/Paint/`" convention) drawing
a fixed gray rectangle plus a centered label - real new paint code, not a wrapper over an existing
property, which is why it was dropped from the #1060 series rather than bundled alongside the other
five (now-shipped) follow-ups: standalone SVG, direct image/SVG injection and dynamic content, shared
images, dashed `LineHorizontal`/`LineVertical`, a custom `ClampLines` ellipsis, `PdfTextDirection.Auto`,
and sectioned page numbers.
