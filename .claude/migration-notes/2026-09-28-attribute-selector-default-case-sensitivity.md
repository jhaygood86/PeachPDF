# Attribute selector default case-sensitivity is now spec-correct (HTML, SVG, MathML)

**Before:** an attribute selector's value (`[attr=value]` and every other value operator) compared case-insensitively
by default for *every* attribute on an HTML element (`[data-x=abc]` matched `<p data-x="ABC">`), and, less
visibly, selector matching of any kind — type, class, id, attribute name and value — against an inline `<svg>`
descendant reached through the document's general cascade (inherited/custom-property matching, as opposed to
SVG's own presentation-attribute styling) was also case-insensitive, as was *all* selector matching against
MathML (`<math>`) content, since MathML had no case-sensitivity handling at all.

**Now:** per the HTML Standard's "Case-sensitivity of selectors" (§4.16.2) and Selectors 4 §6.3/§6:

- An HTML attribute selector's value is case-sensitive by default, except for a fixed list of ~46 legacy
  attributes (`accept`, `align`, `dir`, `type`, `method`, `target`, `rel`, `lang`, and others — see
  [docs/html-css-support.md](../../docs/html-css-support.md#attribute-selectors)), which stay ASCII
  case-insensitive as before. The explicit `[attr=value i]`/`[attr=value s]` modifier (already supported)
  overrides the default either way and is unaffected.
- SVG and MathML are foreign (XML) content: **all** selector matching against them — element type, class,
  id, and attribute name/value — is now consistently case-sensitive, everywhere it's matched (previously
  this was only true for SVG's presentation-attribute styling, not the general cascade, and was never true
  for MathML at all).

**Why:** tracked in issue #1384. A document that relied on the old lenient default for a non-legacy
attribute (e.g. `[data-x=ABC]` matching `data-x="abc"`), or on case-insensitive matching against SVG/MathML
content, needs an exact-case selector or the explicit `i` modifier now.
