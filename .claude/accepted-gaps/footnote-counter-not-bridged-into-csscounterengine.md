# `counter-reset`/`counter-increment: footnote` and explicit `content: counter(footnote)` are not supported

A footnote's number is assigned by `HtmlContainerInt.ResolveFootnotesForThisAttempt` - a live, per-layout-
attempt bookkeeping pass that groups each `CssBoxFootnoteCall` by the pagination slot its call landed on
and numbers them in document order, resetting per page. This is a deliberately separate channel from
`CssCounterEngine`, PeachPDF's general `counter-reset`/`counter-increment`/`counter()` machinery: that
engine resolves a counter's value purely from a box's position in the DOM tree (parent/sibling walks), with
no notion of "which page" a box ends up on - and a footnote's number is fundamentally a *pagination*
outcome, only known once layout has actually run. `CssBoxFootnoteCall.ApplyNumber`/
`CssBoxFootnoteMarker.ApplyNumber` write the resolved number directly into the box's own `Text`, bypassing
`CssCounterEngine` entirely (except for reusing its shared `FormatCounterValue` formatter).

Two consequences: an author's own `counter-reset: footnote`/`counter-increment: footnote` declaration is
parsed and stored like any other CSS declaration but has no effect on the number footnotes actually receive
(the UA-default per-page auto-numbering always wins), and an explicit `content: counter(footnote)` written
on `::footnote-call`/`::footnote-marker` (or anywhere else in the document) resolves through the ordinary
`CssContentEngine`/`CssCounterEngine` path, which has never heard of a footnote landing on any particular
page, and so does not resolve to the live number.

Bridging the two properly - so `CssCounterEngine` genuinely knows a footnote's page-scoped value - is its
own design pass, closer in shape to how `counter(page)` already sits alongside (not inside) the general
counter engine via `TargetPageMap`/`PageAnchorResolver`. Filed as
[issue #754](https://github.com/jhaygood86/PeachPDF/issues/754).
