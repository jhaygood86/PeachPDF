# A sibling combinator must compare its reference node by value, never by reference

_A trap the SVG DOM sets for anyone optimizing the selector matcher._

`DoesSelectorMatch`'s `+` and `~` arms find the reference node's position by scanning
`parent.Children`. An HTML `CssBox` is one object with a stable identity, so reference
identity happens to work there — but an SVG node is not. `SvgXmlDomNode` and
`SvgCssBoxDomNode` are **wrapper objects created fresh on every access**: `Parent` and
`Children` are expression-bodied properties that allocate new wrappers over the same
underlying `XElement`/`CssBox` each time they are read. Both override `Equals`/`GetHashCode`
to key off the *wrapped* object precisely so the matcher can compare them. Two wrappers for
the same element are therefore `Equals` but never `ReferenceEquals`.

So `sibling.Equals(node)` in `PrecedingElementSibling`/`ChildIndexOf` (`CssData.cs`) is
load-bearing. `ReferenceEquals` there — or a `HashSet`/dictionary keyed on reference
identity, or `object.ReferenceEquals` smuggled in as `(object)a == (object)b` — makes the
scan never find the node, so every `+`/`~` rule inside inline SVG silently stops matching.
Silently: nothing throws, the declaration is simply absent, and the HTML side is unaffected
because its nodes *are* reference-stable, so most of the suite stays green.

Pinned by `SvgCssDomNodeTests.MatchedDeclarations_AdjacentSiblingCombinator_*` and
`…_GeneralSiblingCombinator_*` — swapping in `ReferenceEquals` fails them.
