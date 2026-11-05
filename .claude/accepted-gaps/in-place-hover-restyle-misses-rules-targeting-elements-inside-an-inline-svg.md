# The in-place hover restyle does not reach a rule whose target is an element inside an inline SVG

**What.** With `a:hover .fill { fill: red }` and `<a><svg><rect class="fill"/></svg></a>`, hovering the link leaves the rectangle's old fill on screen when the
restyle is applied in place (the fast path). `a:hover svg { fill: red }` works (the `svg` is a box). With `PEACHPDF_FULL_RESTYLE=1`, or a document that is
reloaded, both work.

**Why.** The elements inside an inline SVG are not boxes: the SVG builder reads and matches them itself at paint-preparation time. The in-place restyle finds the
boxes whose matched rules differ under the new state, so a rule whose subject is not a box has nothing to restyle, and the box-level caches (the SVG's matched
declarations are kept per element, see `CssBoxSvgSourceNode`) are not told. `InteractionRestyler` now bumps `HtmlContainerInt.StyleGeneration` after every in-place
restyle, so the cache cannot serve a stale match for a box that did restyle; it does not make the SVG internals notice the change.

**Tried and rejected.** Declining the fast path whenever the changed state is on or inside an element holding an SVG: the hovered chain always includes `html`, `body`
and the page wrappers, so every page with any inline SVG (Ars Technica has 129) would lose the fast path. A precise test needs to know whether any stateful rule's subject
can match an element inside an SVG, which needs selector analysis the matcher does not expose.

**What would close it.** At index time, record whether a stateful rule's rightmost compound can match an element of an inline SVG (a tag from the SVG vocabulary, or a
class/id/attribute compound that appears inside one), and decline the fast path only then.
