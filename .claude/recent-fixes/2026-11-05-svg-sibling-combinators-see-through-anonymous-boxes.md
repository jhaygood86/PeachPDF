# Sibling combinators to an inline SVG see through anonymous boxes (#2215)

Layout wraps inline content that sits beside blocks (an inline `<svg>` after a block `<a>`) in an anonymous block, which is not in the DOM. An inline `<svg>`'s rules are matched after layout, so `a + svg .x` / `a ~ svg .x` looked for `<a>` among the anonymous box's children and never found it.

Fix: `CssBox`'s selector view (`ICssDomNode.Parent`, `SelectorChildren`) now skips anonymous wrapper boxes (`IsAnonymousWrapper`: no element, not a pseudo-element, not a text run, holds boxes) - the parent is the nearest element and the children list each wrapper's contents in its place. A box with no wrapper child keeps returning its `Boxes` list, so `SiblingIndex` indexing is untouched. The svg root is still handed out as the raw `CssBox` (see the hover-restyle entry), which is what lets it be found among its siblings; a first attempt that wrapped the root in a new parent node broke those hover tests.

Evidence: `InlineSvgSiblingCombinatorTests` failed before the fix; full `PeachBrackets.Engine.Tests` and `PeachBrackets.Tests` pass.
