# A link or widget over a transformed element is placed where the element was drawn (#1708)

`<div style="transform: rotate(30deg)"><a href="...">link</a></div>` painted the link rotated and wrote its `/Link`
annotation at the untransformed rectangle. Links, form widgets, links in an SVG, `display: contents` links and links in a
running element now all follow the transform.

- **Load-bearing idea: the painter says where it drew a box; the annotation pass does not derive it.** The painter tracks
  the cumulative `Homography` of the transforms it has entered, in the fragmentainer-local space fragment rectangles are
  in (`FragmentPainter.Placements.cs`: `EnterMap`/`LeaveMap`, an element's own map composed *in front of* the enclosing
  one), and `RecordPlacement` stores it per box and pagination slot in `HtmlContainerInt.Placements` (`BoxPlacements`)
  for the boxes an annotation can sit over. `PdfGenerator.HandleLinks`/`HandleFormFields`/`HandleRunningElementLinks` carry
  the layout rectangle through that map (`AnnotationPlacement.TryMap`). The alternative - re-deriving each ancestor's
  matrix and `transform-origin` from the box tree in the collectors - was not taken for the reason
  [the collapsed-transform fix](2026-10-11-collapsed-transform-takes-links-and-3d-context-with-it.md) already found for
  culling: perspective, a 3D context's accumulated plane and the clip/visibility decisions are all the painter's, and a
  second derivation would disagree in some corner.
- **Four places enter a map, and each needs its own test.** The 2D matrix (`PaintFragment`), an affine result of a parent's
  perspective and the no-raster fallback (`PaintUnderMap`), the projective warp (`PaintProjective`) and each plane of a 3D
  context (`ComposeContext`). The first tests put the link *inside* the transformed element, and disabling the plane's
  own `RecordPlacement` in the warp and in the context left them green: the link's own fragment is painted as a
  descendant and records the same map on its own. A test where the link **is** the transformed plane is what fails with
  each site disabled; each was disabled in turn to check. The no-raster fallback is not reachable from a PDF render and
  is untested.
- **Recording is only for the painter that owns the page walk.** `_recordsPlacements` is set by `Paint` and
  `PaintDetached`, not by the mirror painters (backdrop, flatten, the text-only overlay), which start from the identity
  wherever they start and would record a wrong map. The text-only pass is also skipped by name.
- **The rectangle is mapped through the map, then converted.** `PageFrame` is the one local-px to PDF-points conversion
  (`ContentLeftPt + (x - MarginLeft)/ppp`, y flipped against the page height), the same arithmetic `HandleLinks` always did
  for an untransformed rectangle; the layout rectangle goes to local space with `FragmentainerFragment.LocalOriginY`
  (X is unchanged). `/Rect` is the bounding box of `Homography.ProjectRectangle`'s polygon (clipped at the eye, so a box
  straddling it does not smear); `/QuadPoints` is written when four corners do not fill that box, ordered top-left,
  top-right, bottom-left, bottom-right as readers expect rather than by winding, and only for a PDF 1.6+ file.
- **Widgets: `/Matrix`, with no translation.** The appearance is still built upright at the control's own size and the
  map's linear part is its `/Matrix` (ISO 32000-1 section 12.5.5: the reader fits the matrix-transformed `/BBox` to
  `/Rect`, so a translation would only be cancelled, and leaving it out makes a plain move the identity, which is
  omitted). The first version put the bottom-left corner's image in the translation, and every translated widget then
  carried a pointless `/Matrix`. Under a non-affine map there is no matrix: see
  [the gap](../accepted-gaps/widgets-under-perspective-are-the-upright-bounding-box.md).
- **Traps found by running it.** A form field's widget rectangle is *not* its CSS box (an `<input>` of `width:100pt` was
  105.5pt wide, and the painter's `transform-origin` pivot is `WholeBoxRect`'s centre, not the widget rectangle's), so a
  test that expects a widget to rotate about its own rectangle's centre fails by several points; the widget tests assert
  size and axis directions, which do not depend on the pivot, and an exact move only for a translation. An empty
  `height:1000pt` block does not push later content onto a second page (it needs a background or content), which cost a
  confusing "page index out of range" in the later-page test.
- **Not done:** an annotation over a fragment that fails `PaintFragment`'s `visible` test is not recorded and keeps the
  layout rectangle (the same edge as for a culled element); only the first line rectangle of an inline link is annotated,
  as before; a box fragmented across pages is mapped with the whole-box pivot the painter uses.
- **Evidence:** `TransformedAnnotationTests` (translate, scale about the origin and the centre, quarter turn, rotation
  with its quadrilateral, skew, nested ancestors, the link on the transformed element itself, an anchor link, a later page,
  off the page, `display: contents`, SVG, perspective trapezoid against an independent projection, a plane brought
  nearer, a 3D context against the same plane without one, a 1.4 header, a running element, and the widget cases); the
  full `PeachPDF.Tests` run on net8.0 (16,574 passed, 0 failed).
- **A projected plane wholly outside the clip is hidden per slot, not per box.** Returning early there recorded nothing, so
  the annotation pass read "not recorded" as "not moved" and left a ghost link at the layout rectangle; recording only the
  plane itself still ghosted the links *inside* it, and `NoteNotRendered` fixed both but is per box and so per document (a
  box split over pages would lose its links on the page where it is drawn). `RecordHiddenSubtree` writes a `Hidden`
  placement for the plane and every annotation target under it, keyed by slot like the maps are.
- **Rasterized with both renderers** (MuPDF and PDFium, a rotated text field, a rotated and scaled checkbox and a rotated link): the
  widget appearances line up with the page's own painted chrome in both, so the `/Matrix` carries the upright appearance onto the
  tilted box; the "Transformed (rotate, scale)" section of the `interactive_pdf_forms` showcase is the same markup.
- **The painter's clip is in untransformed space**, so `visible` compares a transformed box's layout rectangle to the clip: content
  translated into view from outside its `overflow` box is not drawn at all (a carousel track). Not part of this change, but the
  annotation pass now agrees with it (a hidden placement), so fixing that test fixes the links with it.
