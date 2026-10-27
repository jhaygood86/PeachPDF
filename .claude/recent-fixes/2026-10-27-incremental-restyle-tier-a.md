# In-place restyle for paint-only properties (`SetStyle`, tier A)

Phase 2, tier A of the selection/editor roadmap: `HtmlDocument.SetStyle(element, property, value)` (opt-in `EnableIncrementalUpdates`) writes
`color`, `background-color`, `text-decoration-line/-style/-color` or `outline-color` on a laid-out document and returns `DirtyRegions`; nothing
is laid out again. Also `GetElement(id)`, `GetElementAt(page, x, y)` and `PaintPageRegionAsync`.

**Load-bearing idea.** The fragment tree holds geometry only and `FragmentPainter` reads style live off `CssBox`, with nothing cached across
`PaintPageAsync` calls, so writing a paint-only property through the generated `CssPropertyRegistry.TrySet` and painting again is a restyle.
Properties that can move anything are *refused*, not approximated (`UpdateResult.NotApplied`), which is what keeps tier B (relayout, height
changes) a separate, later problem.

**Traps.**
- There is no live inheritance: a descendant holds its own copy of the colour it inherited. `PropagateColor` rewrites descendants whose colour
  still equals the old one and stops at one with its own. It cannot tell a declared `color` equal to the old value from an inherited one, so such
  a descendant is rewritten too.
- The border-side colour caches are cleared only by their own longhand (`.claude/invariants/dom-a-border-sides-cached-...`); a colour write
  must clear them by hand or a `currentcolor` border keeps the old colour. The invariant file now names this writer.
- `background-color: currentColor` and `outline-color: currentColor` are substituted with the colour *at cascade time* (`CssUtils.ApplyCurrentColor`),
  so a later `color` change does not reach them. `SetStyle` calls `ApplyCurrentColor` for the element it writes, not for the descendants it
  rewrites' pre-existing values.
- The `html`/`body` background is the canvas's, resolved once; refused rather than half-applied.
- Repainting a region over the old pixels leaves antialiased edges of the old glyphs, so the caller clears the region first (documented; the
  test does it, and then compares the whole bitmap with a fresh full paint).

**Not done.** `opacity`, `box-shadow`, border colours and anything layout-affecting (tier B). Dirty regions are the element's line and word
rectangles, inflated by 4pt and merged, not a tight ink box.

**Evidence.** `IncrementalUpdateTests` (7), including region repaint equal pixel-for-pixel to a full repaint, and the engine suite unchanged.
