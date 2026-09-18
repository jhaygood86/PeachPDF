# `@page` now has its own border and padding, genuinely reserving layout space

Per [css-page-3 §3](https://www.w3.org/TR/css-page-3/#page-model), the page box has the same box model
as any other: margin, then optionally border and padding, around the content area. `border-*`/
`padding-*` declared directly on `@page` were previously read by nothing at all — a document declaring
them saw no effect whatsoever, whether laid out or painted.

Two things change:

- **`border-*`/`padding-*` on `@page` now genuinely narrow the content area.** The resolved border width
  plus padding shrinks the page's own content band on every side, exactly as it does for an ordinary
  element — main content, multi-column layout, footnote areas, and every other page-band consumer
  reflow into the smaller area. A document that happened to already declare these properties on `@page`
  (previously silently inert) will see its content genuinely shift and re-wrap once this ships. A
  document with no `@page` border/padding at all is unaffected.
- **`background-origin`/`background-clip` on an `@page` background now genuinely distinguish border-box/
  padding-box/content-box**, mirroring how a page-margin box's own background already does. This also
  fixes a related, previously-inert case: an `@page` background's positioning/clip area was always the
  *full physical sheet* regardless of the resolved value — including a document with `margin` set but no
  border/padding at all, where the correct default (padding-box) should already have excluded the page's
  own margin area (background never extends into any box's margin, page box included). A document that
  sets an `@page` background *and* a non-zero page margin will see that background's default (padding-
  box) extent pull in from the sheet edge to the margin edge; declaring `background-clip: border-box`
  restores the previous (margin-including) extent when a margin is the only inset in play, since
  border-box sits directly outside any border/padding.

Since `@page`/page-margin-box background support (the feature these two behaviors are part of) landed
after v0.9.18 and has not yet shipped in a release, there is no prior released behavior to diff against
here — this note exists so the eventual release notes describe the feature's actual, corrected shipping
behavior rather than the inert-property/full-sheet-only state it briefly had mid-development.

Closes the accepted gap tracked as issue #1147; see
[docs/html-css-support.md](../../docs/html-css-support.md)'s `@page` rule section (the new "Page-box
border and padding" subsection, and the updated page-box-background paragraph) for the current,
reader-facing description.
