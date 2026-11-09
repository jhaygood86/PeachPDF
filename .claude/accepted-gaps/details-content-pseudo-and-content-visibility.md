# `::details-content` cannot be styled, and `content-visibility` does not skip its contents

The HTML Standard hides a closed `<details>`'s content by giving the second slot of its shadow tree (`::details-content`) `display: block; content-visibility: hidden`. The engine builds that box (`DomParser.WrapDetailsContents`) and hides the contents of a closed element, but:

- `::details-content` is still an unmatchable selector, so an author cannot style the box (border, background, padding, transitions).
- `content-visibility` is a property (`visible`, `hidden`, `auto`), but it only applies its containment: see [content-visibility-never-skips-its-contents.md](content-visibility-never-skips-its-contents.md). The closed state is reached by forcing the contents `display: none` after the cascade, not through `content-visibility: hidden`, so the box has no `contain-intrinsic-size` and layout queries cannot tell the two apart.
- `hidden=until-found` is not hidden at all in the engine, so it has no interplay with find or fragment navigation here.

Why left: the visible behaviour (no author `display` can reveal closed content) is right, and a general `content-visibility` needs layout, fragment-emission and query changes that no page seen so far needs. Tracking issue for `content-visibility`: [#2309](https://github.com/jhaygood86/PeachPDF/issues/2309).
