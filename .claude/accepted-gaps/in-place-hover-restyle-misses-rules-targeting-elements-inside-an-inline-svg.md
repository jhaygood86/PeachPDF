# The in-place hover restyle still misses an SVG-internal rule reached through a sibling combinator

Tracked in #2205. Selectors Level 4 (`:hover` through the sibling combinators) is the rule it deviates from for the case below.

**What is handled.** `a:hover .fill { fill: red }` over `<a><svg><rect class="fill"/></svg></a>`: the elements inside an inline SVG are not boxes (the scene graph is built once and the
boxes dropped, `CssBoxSvg.EnsureDocument`), so the in-place restyle cannot restyle them. `CssData` records on the SVG box when a rule that reads the interaction state was a
candidate for one of its elements (`HasSvgInnerStatefulCandidates`), and `InteractionRestyler` declines the fast path (a full load follows) when the state changed on or inside an
element above such an SVG, `html` and `body` excepted.

**What is not.** A rule that reaches the SVG's elements from state on an element that is *not above* the SVG: `a:hover + svg .x`, `a:hover ~ .icons .x`. The changed element is a
sibling, so the check does not see it and the SVG keeps its old look until the document is next loaded. Also, a page whose SVGs carry such candidate rules loses the fast path
for hovers inside the elements that hold them (the cost of the full load, as before the fast path existed).

**What would close it.** Record the stateful rule alongside the candidate and evaluate its non-subject compounds against the changed element, so only a rule that can change
is a reason to decline; that needs the matcher to expose a "could this rule's state compound be the changed element" test.
