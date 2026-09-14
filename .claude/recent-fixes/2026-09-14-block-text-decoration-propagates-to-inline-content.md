# A block's text decoration is drawn over its inline content, not its own box

## What was wrong

`FragmentPainter` painted text decoration over each of a fragment's `Lines`. For an inline box those
are its per-line rectangles — the right area. For a block-level box there is exactly one, its border
box (`FragmentEmitter`'s `UsesOwnBounds` branch), which is right for background and border and wrong
for a decoration line: the underline ran the block's full content width. The `showcase/bookmarks`
table-of-contents entry (`a.toc-entry { display: block }`) underlined all the way to the right page
margin, which is how this surfaced.

css-text-decor-3 §2.4 does not decorate the block itself: the decoration propagates to an anonymous
inline box wrapping the block's in-flow inline-level content, and through in-flow block-level
descendants to theirs. So the geometry needed is the inline content's, per line box.

## The fix

`FragmentPainter.PaintPropagatedDecoration` (in `.Decorations.cs`) handles the one-rectangle case —
recognized at paint time by `lines is [{ Line: null }]`, since only the own-bounds branch emits a
`LineFragment` with no `CssLineBox`. It walks the fragment's children collecting one union rectangle
per `CssLineBox` and draws a line over each.

Load-bearing details:

- **The spans come from the fragment tree, not the box tree.** A block broken across pages then
  decorates exactly the lines that landed on the page being painted, with no coordinate mapping —
  fragment rectangles are already fragmentainer-local.
- **A descendant hosted on a line box contributes its rectangles and is not descended into.** Its
  rectangle already covers its content (an inline box's includes its padding and border; an atomic
  inline's is its whole border box, which §2.4 draws the line across without propagating into its
  contents). Anything else is a block whose own rectangle is its border box again — the very thing
  this method exists to avoid reading — so it is descended into.
- **Out-of-flow descendants are skipped** (`CssBox.IsOutOfFlow`), per the same section.
- **`PaintDecoration` gained `ownDecorationArea`.** A propagated span is inline content geometry,
  already inside the box's padding; the padding/border insets (`hasLeftEdge`/`hasRightEdge`) and the
  `bottomInset` compensation exist for a rectangle measured from the box's *border* edges and would
  displace a span. This is what makes a padded underlined block correct rather than merely different.
- **The subtree walk is gated on the box declaring a decoration at all** (its own or its
  `::first-line`'s), so the overwhelming majority of blocks never start it.

## Found by running it, not by reading it

- An opaque atomic inline (an `<img>`, an inline-block with a background) **covers** the propagated
  line, because the decoration is painted with the block and the atomic inline paints later. With the
  background removed the same line is continuous underneath, so the geometry is right and only the
  existing stacking order shows through. Not changed here — that is CSS 2.1 Appendix E's order, not
  this fix's business.
- Union-of-tops for the line's y-position was the worry (a tall inline-block or a baseline-aligned
  image would drag the union's top upward and lift the line). Rasterizing both cases showed the line
  still at text level, so no per-span y heuristic was added. If this ever does bite, the fix is a y
  basis taken from the line box rather than from the union rectangle — not a special case per
  descendant kind.

## Deliberately not done

- No change to *where* in the paint order the propagated line is drawn (see the atomic-inline note
  above).
- Decoration still uses the decorating box's own font for the underline offset, as the inline path
  already did; a per-span font would be a separate change.

## Evidence

- 8 new tests in `TextDecorationPaintIntegrationTests.cs` covering the span extent vs. the block's
  width, one span per line box, a multi-child line unioning into one span, padding, an in-flow block
  descendant, a skipped float, an empty block, and a block with no decoration at all. All 19 in the
  file pass.
- Full suite on net8.0: 11546 passed, 2 failed — both `LineClampIntegrationTests`, failing identically
  on `main` with the change stashed.
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings.
- Diff coverage: every changed line is hit.
- Rasterized through PDFium and MuPDF: the bookmarks fixture, plus wrapped/centered/right-aligned/
  padded/nested/floated/atomic-inline/image/empty blocks in one page.
