# The `hidden` attribute has no effect

The HTML Standard's rendering section ([15.3.1 Hidden elements](https://html.spec.whatwg.org/multipage/rendering.html))
gives every element `[hidden]:not([hidden=until-found i]):not(embed) { display: none }`. The UA style sheet in
`src/PeachPDF/Html/Core/CssDefaults.cs` has only `input[type=hidden]`, so `<span hidden>SECRET</span>` and
`<div hidden>…</div>` render as though the attribute were not there.

**Why it was left.** Found while fixing bare text being dropped next to a `display: none` element, which is a layout
defect in its own right; the `hidden` rule is a separate gap in the UA sheet. It changes rendering for any document
that carries a `hidden` attribute (knowingly or not, and including one a script would have toggled), so it should
land with its own tests and a migration note rather than ride along.

**What happens instead.** The element and its content render normally.

Tracked as [issue #1380](https://github.com/jhaygood86/PeachPDF/issues/1380).
