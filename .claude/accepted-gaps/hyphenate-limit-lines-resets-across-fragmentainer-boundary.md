# `hyphenate-limit-lines`'s consecutive-hyphenated-line count resets across a page/column break

`hyphenate-limit-lines` (CSS Text 4 §6.3.5) caps how many consecutive lines may end in a hyphen,
tracked via `CssLineBoxCoordinates.ConsecutiveHyphenatedLines` — incremented when a line closes
having just hyphenated, reset to 0 when a line closes without one. That state lives only on the
per-pass `CssLineBoxCoordinates` record, and `CssLayoutEngine.CreateLineBoxes` constructs a fresh
one at the top of every fragmentainer pass (the same object a resumed page/column starts from). A
paragraph that resumes on the next page therefore begins that page's run count at 0, regardless of
how many consecutive hyphenated lines it had already produced right before the break.

Nothing in the spec text carves out a fragmentation exception for this property, so this is a real
gap, not a spec-sanctioned choice — and this codebase already has the mechanism the correct fix
needs: `InlineBreakToken.FollowsForcedBreak` is threaded from the break token that ends one
fragmentainer pass into the seed line the next pass starts from (`CssLayoutEngine.cs`'s
`new CssLineBox(blockBox) { FollowsForcedBreak = ... }`), specifically so `text-indent: each-line`
keeps recognizing a resumed line correctly across the boundary. `ConsecutiveHyphenatedLines` needs
the identical treatment — a field on `InlineBreakToken` carrying the count as of the break, seeded
into the resumed pass's `CssLineBoxCoordinates` the same way `FollowsForcedBreak` already is — but
that wiring doesn't exist yet.

Concretely: `hyphenate-limit-lines: 2` on a paragraph whose page-1 fragment happens to end on 2
consecutive hyphenated lines lets page 2 hyphenate its own first 2 lines again, producing 4
consecutive hyphenated lines across the boundary where the author asked for at most 2. Multi-page
paragraphs are an ordinary case for this engine, not a rare edge case.

Tracked as [issue #724](https://github.com/jhaygood86/PeachPDF/issues/724). Disclosed to readers
in [Text Layout](docs/html-css-support.md#text-layout)'s `hyphenate-limit-lines` row.
