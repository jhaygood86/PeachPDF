# Nested and percentage-width blocks now reflow per page, and so does the initial containing block's width

Previously, per-page horizontal reflow (a `@page` rule that overrides left/right margins or `size` for
some pages but not others) only re-wrapped auto-width block content whose containing-block chain was
root/`<html>`/`<body>` by tag name. A perfectly plain, unconstrained wrapper `<div>` nested below `<body>`
kept a single measure across every page its content spanned, instead of adopting each page's own content
width the way a direct child of `<body>` already did. Separately, a block with a percentage `width` (or a
`position: fixed` box's percentage width) resolved against a single static value — its containing block's
`Size.Width`, or the document's base `PageSize.Width` for a fixed box — rather than the page-aware measure
an auto-width sibling already used, so its resolved width stayed pinned to whichever page it happened to
be measured against on an earlier layout pass. And the initial containing block's own *width* (unlike its
height, which already did this) resolved against the document's base configured page area rather than the
first page's own (possibly `@page :first`-overridden) area, so a percentage width — or an
absolutely-positioned box's `left`/`right`-filled auto width — that bottomed out at the true ICB ignored a
first-page-only margin or `size` override that a percentage *height* in the same position would already
have honored.

All three now reflow: a plain auto-width wrapper nested anywhere below the main column participates in
per-page reflow exactly as a main-column box does (as long as nothing in its own containing-block chain up
to the page area carries an explicit/percentage width or a `max-width` clamp, which still correctly opts a
chain out); a percentage width tracks its containing block's own page-aware measure; and the initial
containing block's width is pinned to page 1's own resolved band, matching its height. An explicit
fixed-length `width` still never varies by page, and a `max-width`-bearing wrapper still disqualifies
itself (and its descendants) from the page-aware chain — both correctly unaffected by this change, since a
fixed length or a capped box can't be assumed to genuinely span the page area.

See [Per-page margin variation](../../docs/html-css-support.md#page-rule) in `docs/html-css-support.md`.
