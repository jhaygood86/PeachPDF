# A vertical-writing-mode multi-column container cuts its flow into strips after layout

A multi-column container under `vertical-rl`/`vertical-lr` now has real columns. Columns run along the
container's inline axis (physical Y), so they are horizontal strips stacked down the page, and the content of
each strip flows along the block axis (physical X) and continues in the next strip. Before this the container
took the single-column path of `CssLayoutEngineColumns`, which stacked its (vertical) children top to bottom with
the horizontal block flow, so even `column-count: 1` was wrong. The code is `CssBox.TryLayoutVerticalColumnsAsync`
(`CssBox.VerticalColumns.cs`) and `VerticalColumnFlow` (+ `.Snapshots`).

## The load-bearing idea

The column fill of a horizontal container is a resumable pass over a physical-Y band
(`FragmentainerContext`'s `(Top, Bottom)`), and a vertical box's fragmentation axis is X, the one axis that band
does not describe. Making that band axis-agnostic was the "highest blast radius" option the old accepted gap named.
It was not needed. A vertical box already lays out in one pass (`CreateVerticalLineBoxes`,
`LayoutVerticalBlockChildren`), so the container is laid out **once as one long strip** with the column's inline
size, and the flow is then **cut after layout** at the break points css-break-3 section 4.1 allows (between line
boxes, between sibling blocks; `break-before`/`break-after: column`, `break-inside: avoid`, `orphans`/`widows`
honoured; `column-span: all` splits the flow into runs). Each piece is then moved (words, line rectangles, atomic
inlines, whole subtrees through `OffsetLeft`/`OffsetTop`) back along X to the strip's block-start edge and along Y
to the strip's place. The page driver never sees any of it: the container is monolithic to the page
(`MonolithicContent.IsUnresumableOrthogonalFlow` no longer carves out a multicol exception).

## What was found by running it, not reading it

- `CssBox.ActualRight`/`ActualBottom` are **derived** (`Location` + `Size`). Setting `Location` after `ActualBottom`
  moves the far edge too; the first version doubled the shift (a box's bottom came out 120pt too low). Set the
  location first, the far edges after.
- A child of a vertical box wrapped against one full **page** depth, not against its parent. That is wrong inside a
  column (the column is the line length), so a box carries `StripInlineExtent` and treats it as its definite inline
  extent, passed to vertical descendants by `LayoutVerticalBlockChildren`. It is null everywhere else, which is what
  keeps every existing vertical layout byte-for-byte as it was (the whole engine suite passed unchanged apart from
  the one test that asserted the old fallback).
- `column-span: all` needs its own extent: `ColumnsFullInlineExtent`, because a spanning child stretches across the
  whole inline axis while its siblings stretch across one column.
- The flow's pending state (the `direction: rtl` child reflection, bottom floats, text-align finalize) has to be
  settled against the *strip*, before the cut; the epilogue would settle it against the container's final height.
- The long strip must not leak: `ActualSize` is restored afterwards, as the horizontal measurement pass does, and
  the auto-width shrink that `LayoutVerticalBlockChildren` does for the long strip is undone and redone from the
  strips actually used.

## Emission

A box cut across strips has one live position (its last piece). Each strip is recorded as a nested fragmentainer
(`RecordCapturedInstance`, `blockAxisIsHorizontal: true`) whose snapshot states the lines, words and rectangle that
box has *in that strip*. `FragmentRegion.BlockAxisIsHorizontal` makes the slice edges the left/right ones (which of
them begins the box follows `writing-mode`) and `UnbrokenBlockStripOf` concatenates the pieces along X. A container
holding only inline content records no strips (its words are emitted by geometry, which is right; only a decorated
inline spanning the cut is not sliced).

## Not done, and why

- Page level: a vertical box in a horizontal page is not fragmented at a page boundary. CSS Writing Modes 4 section
  7.3.4 allows breaks only between line boxes, and the lines of a vertical box all span the page's own block axis,
  so there is nothing to break between; browsers treat an orthogonal flow root as monolithic. That settled
  the "a vertical box's own content can never fragment across a page boundary" gap: it is the specified behaviour,
  and the fragmentation that *is* possible (along the block axis) is the strips above.
- Footnotes and page floats scoped to a column of a vertical container (`RecordColumnFragmentainer` is not called).
- `column-span: all` below a direct child of the container.

## Evidence

`MulticolWritingModeIntegrationTests` (strip positions for `vertical-rl`/`vertical-lr`, rtl, balance, gap, count and
width resolution, auto-width shrink, break-before/inside, orphans/widows, span, rules painted horizontally, every
word emitted exactly once, per-strip fragments and slice edges). Full `PeachBrackets.Engine.Tests` suite on net10.0.
