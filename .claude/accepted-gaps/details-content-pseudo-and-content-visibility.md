# `::details-content` cannot be styled, and a closed `<details>` does not go through `content-visibility`

The HTML Standard hides a closed `<details>`'s content by giving the second slot of its shadow tree (`::details-content`) `display: block; content-visibility: hidden`. The engine builds that box (`DomParser.WrapDetailsContents`) and hides the contents of a closed element, but:

- `::details-content` is still an unmatchable selector, so an author cannot style the box (border, background, padding, transitions).
- `content-visibility` is a property (`visible`, `hidden`, `auto`), and `hidden` skips its contents (`auto` is in [content-visibility-auto-skipping-what-is-left.md](content-visibility-auto-skipping-what-is-left.md)). The closed state is reached by forcing the contents `display: none` after the cascade, not through `content-visibility: hidden`, so the box has no `contain-intrinsic-size` and layout queries cannot tell the two apart (the slot could simply carry `content-visibility: hidden` now that it is supported).
- `hidden=until-found` is not hidden at all in the engine, so it has no interplay with find or fragment navigation here.

Why left: the visible behaviour (no author `display` can reveal closed content) is right, and the styling of the slot and its find/anchor interplay need selector and query work that no page seen so far needs.
