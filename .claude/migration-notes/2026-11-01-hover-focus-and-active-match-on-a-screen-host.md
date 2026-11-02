# `:hover`, `:active` and the `:focus` family can match

Before: these pseudo-classes parsed and never matched, so a rule using them never applied.

Now: they match when a host calls `HtmlDocument.SetPointerStateAsync` / `SetFocusAsync`. A document that
is only rendered (a PDF, a bitmap) never has any state, so nothing changes for it. The user agent style
sheet's `:focus { outline: thin dotted invert }` is what a focused element shows on a host that sets focus.
