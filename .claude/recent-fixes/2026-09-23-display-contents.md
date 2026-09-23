# `display: contents`

CSS Display 3 §2.5: the element generates no box and is treated as if replaced by its contents — issue #1282.

**Load-bearing idea.** Splice, but keep the element. `CascadeApplyStyles` (the one walk that visits every box)
records each `display: contents` element in `HtmlContainerInt.DisplayContentsShells`;
`DomParser.FlattenDisplayContents` then lifts each one's children into its parent — **innermost first**, by
iterating that list in reverse — after `AssignBidiLevels` and before `CorrectTextBoxes`. Splicing keeps flex/grid
item generation, table fixups and anonymous boxes correct with no per-engine change, and the kept shell is what
lets everything that is about the *element* rather than its box keep working. See
[the invariant](../invariants/dom-a-display-contents-element-is-in-no-boxes-list-so-a-walk-cannot-find-it.md).

**Why that position.** Selector matching, the table-attribute, list-marker and `::first-letter` passes need the
as-authored tree; bidi runs before the splice too, which is why `CssBidiParagraphResolver` treats `contents` like
`inline` (transparent to the paragraph) and applies no `unicode-bidi` push for it. Counters resolve in
`CorrectTextBoxes`, after the splice, so the memoized values are never stale.

**Found by running it.**
- Two `<tr style="display:contents">` share **one** anonymous row (the cells are just cells); an early draft of
  the plan asserted two rows.
- `break-before` on a `display: contents` element has no box to break before — the showcase's second page needed
  it on the `<h1>` inside.
- A shell must keep its `_parentBox` (not `DomParentBox`: a null-`ParentBox`, non-null-`DomParentBox` box is what
  `IsInDetachedRepeatingGroup` treats as a detached `<thead>`).
- `string-set` cannot hook layout (a shell is never laid out); it is assigned in
  `RegisterDisplayContentsNamedStrings`, once geometry is final, and inserted into `_namedStrings` **by position**
  because `string(first|last|start)` select by list order.
- A contents `<body>` shell has to have its background image loaded by hand, and its border/padding/radius
  dropped (`DropBoxModel`) or the canvas background is measured from edges that do not exist.
- `target-counter(#shell, name)` is resolved during DOM construction, before any layout, so the counters where a
  shell's content begins cannot be found through geometry (the first version returned the parent's value, `0`).
  `DomUtils.ResolveCounterAnchor` is structural: the box just before the first lifted child (previous sibling,
  else parent) — not the child itself, whose own `counter-increment` comes after the element's position.
  `target-counter(…, page)` and the `#id` rectangle *do* use geometry (`ResolveGeometryBox`); they are re-resolved
  after layout.
- The declarative `CssPropertyFactory` is shared by every page: `AddDeclarativePage` clears its shell list first, or
  page 2's container inherits page 1's (disposed) shells - stale ids, duplicated bookmarks and links.
- The cascade recurses into an inline `<svg>`/`<math>`; recording a `<g>` there as a shell lifted it out from under
  the SVG builder. No shell list is passed below a `CssBoxSvg`/`CssBoxMath`.
- Same-Y ties: a wrapper and the heading it wraps begin at the same Y, so shell entries insert before equal-Y
  entries (with a floor that keeps two shells in document order), not after.
- The `IContainer.Html(...)` declarative path has no container at splice time; its shells travel on
  `CssPropertyFactory.DisplayContentsShells` to `SetDeclarativeRoot`.

**Not done.** A counter leaking out of a contents list ([gap](../accepted-gaps/counter-scope-leaks-out-of-a-display-contents-list.md), #1297). SVG-internal `display: contents`
([gap](../accepted-gaps/display-contents-on-svg-internal-elements-has-no-effect.md), #1295). The html-vs-body
canvas precedence inversion found on the way is unrelated and left alone (#1296).

**Evidence.** `DisplayContentsIntegrationTests` (layout geometry on every affected box, tagged structure, ids/links/
bookmarks/`target-*`, `string-set`, canvas background, bidi, declarative API), full net8.0 suite green, and the
`display_contents` showcase rasterized through PDFium and MuPDF.
