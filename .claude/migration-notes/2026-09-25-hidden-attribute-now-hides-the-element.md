# The `hidden` attribute now hides an element, and the HTML Standard's other hidden elements are hidden too

**Verified against v0.9.19:** `git show v0.9.19:src/PeachPDF/Html/Core/CssDefaults.cs` has no `[hidden]` rule (only
`head`, `input[type=hidden]`, and a list of metadata elements), lists `noframes` under `display: block`, and
`git show v0.9.19:src/PeachPDF/CSS/Parser/SelectorConstructor.cs` rejects anything but `]` after an attribute
selector's value, so the `i` modifier the spec's own rules use could not be parsed. All three are genuine
changes for the next release.

**Before:** `<p>before <span hidden>SECRET</span> after</p><div hidden>BLOCKSECRET</div>` rendered every word
- the attribute was ignored. `<noframes>`, `<template>`, `<datalist>`, `<noembed>`, `<basefont>` and `<rp>`
content rendered as ordinary content. `[attr=value i]` / `[attr=value s]` made the whole rule invalid, so it
matched nothing.

**Now:** the default style sheet carries HTML Standard §15.3.1's rules:

- an element with a `hidden` attribute (valueless, empty, or any value other than `until-found`) is
  `display: none` - no box, no space, nothing painted. An author `display` declaration still overrides it,
  as in a browser; `<embed hidden>` stays a zero-sized inline box; `hidden=until-found` stays visible
  (PeachPDF has no `content-visibility`).
- `noframes`, `template`, `datalist`, `noembed`, `basefont` and `rp` are `display: none` (with the
  elements that already were: `area`, `base`, `head`, `link`, `meta`, `param`, `script`, `style`, `title`).
- `input[type=hidden]` matches in any letter case and is `display: none !important`, so an author
  `input { display: block }` no longer shows it.
- `[attr=value i]` and `[attr=value s]` parse and apply: `i` compares ASCII case-insensitively, `s`
  case-sensitively. Without a modifier the comparison is unchanged (case-insensitive in HTML).

**What a document author would notice:** a document that carries `hidden` on an element it also relied on
seeing (for example a template-like `<div hidden>` toggled by a script that never ran) now loses that
content in the PDF; add `style="display: block"` (or a stylesheet rule) to show it. `<noscript>` content is
unchanged: it still renders, since `(scripting)` is false for a PDF.
