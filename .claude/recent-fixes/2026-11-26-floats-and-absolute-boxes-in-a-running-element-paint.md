# Floats and absolutely positioned boxes in a running element paint (#2490)

A `float` or `position: absolute` descendant of a `position: running()` element placed with `content: element()` was
laid out on every page but never painted; in-flow and `position: relative` content was. A letterhead logo aligned with
`float: left` disappeared.

Cause: `MarginBoxContentFragmentBuilder.Build` filtered `IsOutOfFlow` children out of the fragment tree, so the painter never
saw them. The painter was already ready (`StackingOrder.Flatten` makes a detached root an ordering scope, see the footnote
stacking-context fix), so the change is only to the builder: floats and absolute boxes are children again; page floats
(`float: top/bottom`, nothing to float to here) and nested running boxes stay out. The same builder serves footnote bodies
and SVG `foreignObject`, which gain the same behavior.

Trap: the clip. A fragment carries its nearest overflow clipper's clip, but an absolute box is clipped only by a clipper on
its containing-block chain (`DomUtils.IsOnClippingChainOf`, css-overflow-3 section 3), not one between it and its containing
block, so the builder passes such a box the outer clip instead.

Tests: `RunningElementOutOfFlowTests` (fail without the change). Rendered the issue's HTML through the CLI: image, FLOATED-TEXT
and ABSOLUTE-TEXT are on both pages.
