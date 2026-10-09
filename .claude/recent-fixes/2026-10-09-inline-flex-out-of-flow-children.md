# Out-of-flow children of an inline-flex container are laid out (#1665)

An absolute/fixed child of `display:inline-flex` stayed 0x0 at the origin. A block-level flex container
reaches the flex engine through `CssBox.LayoutEngineContent`, which ends by laying the box's out-of-flow
children out (the flex engine never does — css-flexbox-1 §4.1); an inline-flex box goes through
`CssLayoutEngine.FlowInlineFlexChild`, which called `CssLayoutEngineFlex.PerformLayout` directly and
skipped that step. `inline-grid` was unaffected (it goes through `PerformLayout`).

The "detach the fragmentainer, run `LayoutOutOfFlowChildren`, restore" tail of `LayoutEngineContent` is now
`CssBox.LayoutOutOfFlowChildrenDetached`, called from both. Keep the detach: `LayoutOutOfFlowChildren`
discards any resumption record, so it must run with breaking suppressed.

It was mostly unnoticed because `CorrectReplacedElementBoxes` used to wrap an absolute `<img>`/`<svg>` in an
in-flow block that laid out through the ordinary flow; the blockify change skips that wrapper for direct
flex/grid children, which exposed it for replaced children too.

A second trap, found in review: `OffsetBoxWithinLine` (vertical-align) only translated a box's subtree when
`LastOwnLineBaselineOf` found a baseline, so a *text-less* inline-flex had its line rectangle moved while its
`Location` — which the abs children were positioned against — stayed put. An `inline-flex` box now always
translates as a unit, and `MoveStaticallyPlacedDescendants` (the table-cell mover's helper) carries along an
absolute descendant with auto offsets whose containing block lies outside the box and so was skipped by the
translation. `inline-grid`/`inline-table` have the same text-less hole for in-flow items; not touched, see [the gap](../accepted-gaps/vertical-align-does-not-move-a-textless-inline-grid-or-inline-table.md).

Evidence: `InlineFlexOutOfFlowChildrenIntegrationTests` (div, img, svg, fixed div, each compared with the
`flex` container laid out from the same markup; plus vertical-align and in-flow-item-moves-once cases) — the
four compare-with-`flex` cases and the exact-offset fact fail without the change (the other cases cover the
vertical-align follow-up); full net8.0 suite passes. `InlineFlexOutOfFlowChildrenVariantsIntegrationTests` adds
wrapped, right/bottom, percentage, unpositioned, nested, display:none, re-measured (inline-block / table cell /
flex item / float), repeated-layout, multicol and page-foot cases; the page-foot cases compare the child's
fragmentainer slot with the block-level `flex` container's. Side findings, both pre-existing: a static-position child's X is the containing block's left edge rather than the
container's, for a block-level `flex` too — the already-recorded [static position gap](../accepted-gaps/absolutely-positioned-box-ignores-its-static-position.md), so the static-position test pins only Y; and
`text-align` ignores an atomic `inline-flex`/`-grid`/`-table` box's width ([gap](../accepted-gaps/text-align-ignores-the-width-of-an-inline-flex-grid-or-table-box.md)), which is why the text-align test pins only
the child's offset from its container.
