# Overflow containers retain page breaks

Until this change, every box with `overflow` other than `visible` was treated as unbreakable in print.
A tall auto-height `overflow: hidden`, `auto` or `scroll` container was carried whole to the next page
where it would straddle a page edge, or, when it was taller than a page, was sliced at the edge so a
line on each cut was drawn on neither page.

Now an auto-height `overflow: hidden`, `auto` or `scroll` container in ordinary block flow breaks between
lines and blocks like any other block. Page counts and line positions can change for a document that wraps
content in such a container (the common clearfix wrapper is the usual case). A `min-height` alone does not
keep the box together; use `break-inside: avoid` to ask that a short panel stay on one page.

Unchanged, and still unbreakable:

- a scroll container with a definite logical height or a definite maximum logical height, whatever its
  `overflow` value (a percentage against an indefinite containing block counts as automatic, and in a
  vertical writing mode the logical height is the physical width);
- a scroll container that is a flex or grid item, or that sits inside a multi-column container, even
  when it has no size of its own;
- a scroll container that holds an absolutely positioned box (a `display: none` one does not count),
  whatever its own `position`. Wrappers without one, the usual clearfix case, do break.

An `overflow: hidden` box with a `max-height` therefore stays whole, as before, including when the
maximum is larger than the `height`.

Checked against the last release (v0.9.20), where `MonolithicContent.IsMonolithic` returned true for every
scroll container.
