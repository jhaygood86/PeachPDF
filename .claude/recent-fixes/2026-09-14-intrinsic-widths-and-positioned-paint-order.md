# Intrinsic widths keep content and decoration on the same line

Acid2's eye row exposed two independent defects that had survived the earlier layout fixes: its
shrink-to-fit width combined content from one child line with padding and borders from another, and
its positioned eyes painted in groups based on positioning scheme rather than in document-tree order.
The resulting face was wider than Chrome's and one eye painted over the other in the wrong order.

## The load-bearing ideas

An intrinsic-width candidate is one complete line. Content width and that line's decoration must stay
paired while sibling lines compete; independently taking the largest content width and a separate
decoration total can invent an outer width that no line has. Decoration from nested boxes still
accumulates through the containment chain because every ancestor contributes to the same candidate.
An explicit child width is folded into the recursive candidate before that candidate competes with
sibling lines for the same reason.

The walk now updates min-content and max-content widths together with the decoration belonging to
each winning candidate. The decoration-less `GetMinMaxWidth` overload was removed so a new consumer
cannot accidentally discard half of that result.

CSS 2.1 Appendix E puts every positioned descendant at stack level zero into one tree-order phase.
`position: relative`, `absolute`, `fixed`, and `sticky` are therefore not separate paint buckets.
All positioned boxes now take the stacking-hoist path, the ordinary block and inline passes exclude
them, and one positioned pass paints them in flattened tree order.

The Acid2 showcase also uses zero page margins. That makes its viewport coordinate system comparable
to a browser viewport instead of shifting normal-flow content while fixed-position content remains
page-relative.

## Deliberately not done

Acid2's 2×2-pixel transparent checker tiles can still look stippled. The emitted grids have the
correct one-pixel phase, but their appearance varies by rasterizer and output resolution: MuPDF
renders the band largely solid while PDFium can retain a visible pattern. Replacing the grids with
native PDF tiling patterns was tested in both engines and produced worse stripes or checks at 96 and
192 DPI, so the ordinary image-repeat path remains in place.

## Evidence

- Acid2's `.eyes` box now measures 108pt (144 CSS px), matching Chrome, rather than combining a
  120px content candidate with 35px of decoration from a different line.
- New intrinsic-width tests pin sibling-line decoration pairing and nested-decoration accumulation.
- A paint-order integration test places relative, absolute, fixed, and sticky descendants in one
  source-order sequence and asserts the adapter call order.
- The 82 focused intrinsic-width, Acid2, and stacking-order tests pass on `net8.0`.
- The full `net8.0` suite passes: 11,404 passed, 0 failed, and 9 platform skips. Diff coverage
  against `origin/main`, including working-tree changes, is 99%.
- `dotnet build PeachPDF.slnx -t:Rebuild` completes with 0 warnings and 0 errors.
- All 121 showcases were regenerated. PDFium and MuPDF rasterizations at 96 and 192 DPI agree on
  the face geometry and eye ordering; only the documented checker-tile filtering differs.
