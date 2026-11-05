# Sibling combinators to an inline SVG see through anonymous boxes (#2215)

An inline `<svg>`'s styling is resolved after layout, on the box tree. `SvgCssBoxDomNode.Parent` for the svg root returned the raw `ParentBox`, so `a + svg .x` / `a ~ svg .x` failed two ways: layout may have put the svg in an anonymous block (whose children lack the `<a>`), and the raw parent's children held the raw `CssBox` rather than the `SvgCssBoxDomNode` the matcher was holding, so `Equals` never found it.

Fix: the root's parent is now `SvgHostParentNode` - the nearest element ancestor (anonymous boxes skipped), delegating to its `CssBox` view except `Children`, which are listed through anonymous boxes and name the svg root by the same wrapper being matched. The HTML cascade and `SiblingIndex` are untouched (this runs post-layout only; the SVG wrapper path never used the index).

Evidence: `InlineSvgSiblingCombinatorTests` failed before (2 of 3) and pass after; full `PeachBrackets.Engine.Tests` and the `*Svg*` PeachPDF tests pass.
