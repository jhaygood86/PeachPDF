# Opt-in snapping of box decorations to whole CSS pixels (`SnapBoxDecorationsToCssPixels`)

**Symptom:** a 1px border read heavier than Chrome's at 100% zoom. Both stroked 0.75pt, but the box edge
was at 94.49px, so the stroke covered two half-covered device columns; Chrome's edge at 94px covers one.
Chrome's PDF coordinates for the same page were all multiples of 0.75pt.

**Change:** `PdfGenerateConfig.SnapBoxDecorationsToCssPixels` (default `false`, CLI
`--snap-box-decorations-to-css-pixels`). When on, `FragmentPainter` snaps the `BoxDecorationGeometry` once,
right after `BoxDecorationGeometry.For`, via `DecorationPixelSnapping.Snap`: each edge of the decoration
rect goes to the nearest whole CSS pixel (`Length.PointsPerPx * PixelsPerPoint` layout units). Everything
resolved against that geometry (background, border, border-image, outline, shadows, backdrop filter)
therefore shares the same edges. Skipped under a non-identity transform; an axis that would collapse keeps
its original edges. The flag reaches the painter through
`HtmlContainerInt.SnapBoxDecorationsToCssPixels`, set in `PdfGenerator.SetContent` **and** in the
declarative document-builder path (`PdfGenerator.AddPages`), which builds its own container and never
passes through `SetContent` - the first version only set it in the former, so the option silently did
nothing for a `CreateDocument` caller.

**Why opt-in:** the maintainer checked PrinceXML, whose output matches PeachPDF's exact geometry, and did
not want a default that moves every box by up to 0.375pt. With the option off the output is unchanged,
so there is no migration note.

**Traps:**
- The first attempt snapped only the border rect. A snapped border moves up to 0.375pt while an unsnapped
  background does not, so the background showed past the border as a fringe (about 35% background colour
  one pixel out in MuPDF; a full pixel in PDFium, which does not anti-alias axis-aligned rect fills). The
  outline drifted off the border the same way. Snapping the geometry once, rather than each painter on its
  own, is what fixes both. (Snapping at the same point in an always-on version broke ~48 tests, which is
  one more reason it stays opt-in.)
- **In PDFium the page size decides the result, not the option.** PDFium rounds A4 (793.7px at 96 dpi) up to 794px
  and fills plain rects without anti-aliasing, so a rect landing ~0.05px past a boundary paints the whole next
  pixel. Thirteen 1px `border-top` rules at fractional offsets, rasterized at 96 dpi: A4 + PDFium is 13/13 two-row
  rules with the option off **and on**; A4 + MuPDF goes 13/13 two-row -> 13/13 one-row; a 794x1123px page is 13/13
  one-row in PDFium even with the option off. So for the issue's own scenario (a single `border-top: 1px` on A4)
  the option does nothing in a PDFium-based viewer (Chrome, Edge). It is documented in usage-examples.md and the
  XML doc, and the PR says `Refs`, not `Fixes`, #1530.
- Text and replaced-element content are not snapped, so text can sit up to 0.375pt off its snapped box.
- Measured (one element per 200x100px page at a 10.3px offset, off vs on, MuPDF): text `<input>`, `<select>`,
  checkbox, radio, `<object>` with no usable data, `<video>` with no source and plain/inline-block boxes **are**
  snapped; `<button>` and `<textarea>` are snapped once the author sets a border or background but not in their
  default appearance; `<img>`, inline `<svg>`, `<iframe>`, `<math>` and an `<object>` showing an image are not.
  `<progress>` and `<meter>` paint nothing at all here, so they say nothing either way. "Form-field chrome" in
  earlier versions of this note meant the internal glyphs of a control, not controls in general.
- A cut is not a box edge: only edges the box owns (`HasLeft/Top/Right/BottomEdge`) are snapped, so a block
  cut across a page keeps its page-top/page-bottom edge where it is. The table page-break Y that replaces
  the border rect's bottom is a cut across a page, not an edge of the table, so it stays at the Y layout gave.
- The `overflow` clip is snapped with the box (`FragmentPainter.OverflowClipOf`, also the recorded step the
  deferred outlines replay), or a child's background covers part of the snapped border. The rectangle and the
  rounded curve's own rect are snapped together so they keep agreeing, and the clips re-pushed for a hoisted
  participant (`RenderUtils.TryPushOverflowClip`) are snapped too, on the edges that ancestor fragment owns.
  `BoxFragment.OverflowClip` is the clipping *ancestor's* whole padding box in this fragmentainer's space, not
  a cut of it, so its edges are never page breaks. The exception is a displaced table-row slice, whose clip is
  intersected with the fragmentainer band: that band edge can be moved by up to 0.375pt.
- Collapsed table borders (`PaintCollapsedTableBorders`) are snapped with the cell backgrounds; left alone they
  stayed on their fractional line beside a snapped background.
- Rounding is half-up with a 1e-6 bias (`HalfPixelBias`). Two edges that are the same line computed two ways
  (`left + width` vs a running sum) differ by ~1e-13, and at exactly x.5px that split them into a 1px gap.
- A sliced box (`NeedsClip`) snaps too: its unbroken strip (every edge is the box's own) and the owned edges
  of the slice that clips it, which are the same lines and so land on the same place. Cut edges stay put.
  Skipping sliced boxes left the border/background unsnapped under a snapped child clip.
- A collapsed segment's stroke width follows its snapped rect (`DrawCollapsedSegment` centres a dashed or
  banded stroke at `rect.Top + width / 2`), and the replaced-element painter takes its overflow clip from the
  same `FragmentPainter.OverflowClipOf` as every other box - it was a third call site that kept the
  unsnapped one. The hoisted-ancestor clip takes its owned edges from `BoxDecorationGeometry.For`, so
  `box-decoration-break: clone` agrees with the decorations.
- Reviewed and declined: text decorations keep their unsnapped rect (they follow the text, which is not
  snapped); `CurrentTransform.IsIdentity` is exact on purpose, since `GraphicsAdapter.Pop` restores the saved
  matrix rather than recomputing it, so no float residue; a box thinner than a pixel keeps its fractional edges
  rather than growing to a pixel, because growing it would let it overlap its neighbours, which the monotonic
  rounding otherwise rules out. (Culling used to be listed here; it later got half a pixel of slack, see
  `CullingBounds` below.)
- The config-flow test blanks the PDF's comment lines, subset tags and trailer ID before comparing; without
  that, a plain `NotEqual` passes on the creation-time comment alone.
- **Offscreen canvases are not skipped.** The first version returned early on `Canvas.IsOffscreenTile`, which
  meant a box with `opacity`, `mix-blend-mode` or a filter, or anything repainted into a flatten/raster
  region, was left unsnapped beside its snapped neighbours (measured: the PDF was byte-identical with the
  option on and off for all of them). But the offscreen canvases PeachPDF paints boxes into are in the page's
  own coordinates: `Canvas.BeginLayer` with no region is a page-sized form with no translation, a layer with
  a region applies a translate (so it fails the identity check and is still skipped), and a `RasterCanvas`
  region is seeded with its requester's transform and mirrors its `PixelsPerPoint`. `CreateTile` tiles with
  their own origin (pattern/border-image/stroke-opacity tiles) never carry a box's decorations, so the flag
  was guarding a case that does not occur. Rasterized at 96 dpi, an `opacity:.6` box now has the same crisp
  single border column as a plain one.
- The overflow clip is `Snap(border box)` inset by the border's own widths, **not** `Snap(padding box)`: the
  border is drawn at its true width inside the snapped outer edge, so with `border: 2pt` (2.67px) a separately
  snapped padding edge sat up to 0.5px off the border's inner edge. `BoxFragment.OverflowClipBasis` carries the
  ancestor's unconfined border and padding boxes plus the fragmentainer band, because `OverflowClip` itself is
  the intersection with that band for a displaced fragment, and the band is a page/column cut that must not move.
  A clip with no basis (band only, no clipping ancestor) is left as layout gave it. The hoisted path does the
  same from `ancestor.Rect` and feeds the snapped border rect to `ComputeInnerRadii`. Both go through
  `DecorationPixelSnapping.SnapPaddingBox`, which also falls back to the unsnapped padding box when the snapped
  border box is narrower than its own borders (the derived one would be inverted).
- `text-overflow: ellipsis` measures its end boundary against the snapped padding edge (the clip that cuts the
  text); the start boundary stays on the raw edge, where the unsnapped text begins. Measured on a width
  sweep: the ellipsis ended 0.3pt past the snapped clip before the change.
- The table page-break check compares against the *unsnapped* bottom (`unsnappedBottom`), since whether a table
  is cut at a break is a fact about layout, and the replacement Y is **not** snapped either: it is a page cut,
  not an edge of the table, and snapping it would let the page clip cut part of the closed bottom border off
  (an earlier version snapped it; a review caught that it contradicted the cut-edges-stay-put rule).
- Visibility culling gets half a CSS pixel of slack when snapping is on (`CullingBounds`): a hairline that only
  just misses the clip can have its snapped edge inside it.
- Reviewed and declined: the grid is anchored at the page's top-left corner (fragmentainer-local coordinates
  include the page margins) and is 0.75pt on the page whatever `PixelsPerInch` is, since the grid pitch is
  `PointsPerPx * PixelsPerPoint` layout units; a `DecorationRect`/`ClipRect` pair collapsing independently needs a
  sub-pixel sliced fragment and cannot reach a visible mismatch; the ellipsis start boundary stays on the raw
  edge on purpose (see above). A collapsed segment thinner than a pixel keeps its fractional rect, and a 1.5px one can
  be 1px or 2px depending on where it falls (a browser floors the width at layout; that is a separate
  decision); an inline box's underline is positioned against the raw rect, up to 0.375pt from the snapped
  border edge (it follows the text, which is not snapped); a bordered `<img>` keeps its own unsnapped rect
  (replaced elements are documented as not snapped); `ComputeInnerRadii` for a hoisted rounded clip still takes
  the unsnapped border rect (the radii differ by far less than a pixel).
- A sub-pixel box keeps its fractional edges (Chromium would make it 1px wide), so it can overlap a neighbour by
  under 0.2px: a small exception to "never overlaps more than layout already had".
- Per-fragment snapping of the overflow clip is cheap (four floors) and only runs when the option is on, so
  it is recomputed for every descendant rather than memoised.
- The tests that compare border output with outline output needed no change only because snapping is off
  by default.

**Evidence:** sample from upstream #1530 at 96 dpi, left border edge, MuPDF and PDFium agree: exact render
two half-covered columns, snapped render one column (grey 182, Chrome's value). Showcases
`border_pixel_snapping_exact` / `border_pixel_snapping_snapped` render the same HTML both ways.
