# `::details-content` cannot be styled, and `content-visibility` is not supported

The HTML Standard hides a closed `<details>`'s content by giving the second slot of its shadow tree (`::details-content`) `display: block; content-visibility: hidden`. The engine builds that box (`DomParser.WrapDetailsContents`) and hides the contents of a closed element, but:

- `::details-content` is still an unmatchable selector, so an author cannot style the box (border, background, padding, transitions).
- `content-visibility` is not a property here at all (`visible`, `hidden`, `auto`). The closed state is reached by forcing the contents `display: none` after the cascade, so the box has no `contain-intrinsic-size` and layout queries cannot tell the two apart.
- `hidden=until-found` is not hidden at all in the engine, so it has no interplay with find or fragment navigation here.

Why left: the visible behaviour (no author `display` can reveal closed content) is right, and a general `content-visibility` needs layout, fragment-emission and query changes that no page seen so far needs. Tracking issue: to be filed with the next batch (a spec deviation per CLAUDE.md).
