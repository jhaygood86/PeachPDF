# `CssBox.ParentBox`'s setter *appends* to the new parent's `Boxes`

_CSS Fragmentation Level 3. Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320)._

An `Insert(index, box)` followed by re-parenting puts the box in the child list **twice** — which is how a restored `<thead>` was both `_headerBox` and a body row, adding exactly one header's height per layout (#353). Nothing warns: the compiler is quiet, the showcases are byte-identical, and the suite is green.
