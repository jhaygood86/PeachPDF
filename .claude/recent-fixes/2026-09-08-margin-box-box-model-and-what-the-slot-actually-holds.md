# A page margin box's box model, and what its slot actually holds

`MarginBoxRenderer` resolved a margin box's `width`/`height` but never read `margin`/`padding`, so
`content` painted at the edge of the whole slot. The fix is one method, `ApplyBoxModel`, called from
both render paths — `MarginBoxRenderer.Render` for text/image content and
`HtmlContainerInt.LayoutMarginBoxes` for `content: element()`.

The load-bearing idea is that `GetMarginBoxRect`'s slot and the rect the caller paints into are two
different rectangles, and which one a declared `width` describes decides everything else.

## What running it turned up

- **Subtracting alone double-charges an explicitly sized box.** `GetMarginBoxRect` feeds a declared
  `width` straight into `ComputeThreeBoxSizes` as the slot extent, so subtracting padding from the
  result renders `width: 200pt; padding: 0 5pt` at 190pt. css-page-3 §5.3.2 is explicit that the
  declared dimension is the CONTENT box and the box model is additive, so the slot has to be the
  OUTER size. Both directions now go through one `BoxModelExtent` helper —
  `GetMarginBoxRect` adds it, `ApplyBoxModel` subtracts it — rather than two implementations that
  can drift apart. An auto-sized box is unaffected: its slot really was an outer allocation.
- **The percentage basis was the box's own post-distribution share, and that is circular.** Using
  `rect.Width` meant the same percentage on two boxes in one row was two different absolute lengths,
  and for an auto-width box the basis fed a distribution that was itself box-model-blind.
- **Fixing that against the containing block's width alone was still wrong, on the vertical axis.**
  CSS 2.1 §8.3/§8.4 resolve every edge against the containing block's *width*, and that is what the
  first fix did — but css-page-3 §6 explicitly overrides it in the page context: "For right and left
  values, percentages are relative to the width of the containing block; for top and bottom values,
  percentages are relative to the height of the containing block." So the two axes need two different
  bases, and the containing block itself (§5.3.1) differs per box: the content band's width by the
  page margin's thickness for a top/bottom row, the margin's thickness by the band's height for a
  left/right column, and the intersection of the two margins for a corner. `MarginAreaWidth` and
  `MarginAreaHeight` resolve those from the box name. Read the spec's own text before trusting a
  CSS 2.1 rule inside the page context — several of them are overridden there.
- **A `<param>` tag is all-or-nothing.** Documenting the new `containingBlockWidthPt` parameter alone
  produced four CS1573 warnings for the parameters that had none — the exact warning class
  CLAUDE.md names. Caught by `-t:Rebuild`, invisible to an incremental build.
- **An undeclared margin box is still `auto`, not absent.** The first version of the sizing fixtures
  assumed a lone `@top-center` took the whole 480pt band; `ComputeThreeBoxSizes` gives all three an
  equal third, so its slot is 160pt at x=220. Worth knowing before writing any margin-box geometry
  assertion.

## Evidence

`MarginBoxRendererBoxModelTests` (11 fixtures) plus
`MarginBoxElementFragmentIntegrationTests.RunningHeading_MarginBoxHonoursItsOwnPadding` for the
`element()` path, all mutation-checked: reverting only the outer-size allocation fails the two
explicit-dimension fixtures; reverting only the percentage basis fails the two percentage fixtures;
dropping the `element()` call site fails the integration test. Full suite green on net8.0, 0 build
warnings, diff coverage 100% on the changed lines.

## Deliberately not done

`border` — neither painted nor charged. Recorded in
[the accepted gap](../accepted-gaps/page-margin-box-border-is-not-painted-or-charged-space.md) and
tracked as #943; charging space for a decoration that never renders would be worse than the gap.
