# A bare `*` (and `:not()`/`:lang()`) still matches generated pseudo-element boxes

Tracked in #1361.

`CssData.CanBeMatched` admits a `CssBox { IsPseudoElement: true }` in addition to element boxes, so
`* { margin: 0 }` (or `:not(.x) { ... }`) still applies to a generated `::before`/`::after`/`::marker`/
`::first-letter`/`::placeholder`/footnote box. Selectors 4 §5.2 says `*` represents elements only, and a
pseudo-element is reached only through its own selector on the originating element.

**Why it stays:** #1360 removed the damaging case — bare text/anonymous boxes matching `*` and so beating
more specific rules on inherited properties. The pseudo-element case only affects non-inherited
properties on generated boxes and would need its own showcase review (Charts.css and Acid2 pair blanket
`*` rules with `*::before`). Not measured or attempted yet; the fix is to require a real element in the
three subject matchers and rely on the `referenceBox` logic already in
`DoesSelectorMatch(CompoundSelector, ...)`.
