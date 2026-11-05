# In-place hover restyle: declining for an inline SVG's state rules is now exact

Issue 2205. `InteractionRestyler` used to decline the fast path only when the changed element was *above* an inline SVG that had a state-reading rule as a candidate, so `a:hover + svg .x` / `a:hover ~ .icons .x` (changed element a sibling) left the old fill on screen.

**What changed.** The SVG box now keeps the *selectors* of those rules (`CssBox.SvgInnerStatefulSelectors`, noted from `CssData`), and `CssData.StateCompoundCouldMatch(selector, changed)` answers "could this rule's state compound be the changed element": each compound that reads `:hover/:active/:focus*` is tested on its tag/class/id/attributes against the changed element. Over-approximates (never under): a state read inside `:has()/:is()/:not()` answers yes; the rest of the selector is not tested. The restyler declines only when some rule can match a changed element (html/body excepted, as before), so hovers on unrelated elements keep the fast path.

**Trap found by running it.** The SVG root as a sibling-combinator target never matched at all (static `a + svg .x` included): `SvgCssBoxDomNode.Parent` handed the matcher a wrapper for the `<svg>` element, and the sibling lookups compare against the raw `CssBox` children of its parent, so the wrapper was never found. Elements directly under the `<svg>` now get the raw box as their parent. Separately, SVG styling resolves after layout, on the tree that already has anonymous block boxes, so a sibling combinator to an `<svg>` that sits in an anonymous wrapper still does not find the previous sibling (tests use a flex container to avoid it).

**Evidence.** InteractionStateTests (+4 cases), StateCompoundCouldMatchTests (13).
