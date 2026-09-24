# An empty positioned inline at a bidi run boundary anchors at the far end of the run (#1309)

**Gap:** an empty positioned inline's zero-width containing block
(`CssLayoutEngine.EmptyInlineContainingBlockFor`) follows one adjacent word through
`ApplyBidiReordering` (`PlaceOnFinalLine`). Inside a run that is right, in `ltr` and `rtl` alike. At the
boundary between two runs of opposite direction, the place goes with its anchor word's run and ends up
at that run's far end. Measured in review against Chrome: `<div dir=rtl>aaa bbb[b]` gives X=300 against
256.5, and an `ltr` paragraph `[b]אבג דהו` gives 112.7 against 75. UAX #9 resolves the empty inline's
own level from its neighbours and reorders it with them, and CSS 2.1 §10.1 / CSS Positioned Layout 3
§2.1 form the containing block wherever that puts it.

**Where:** `PlaceOnFinalLine` reads the anchor's level (`ReorderedLevelOf`) and translates or reflects
within that word. The empty inline has no level of its own and takes no part in the reorder.

**Why out of scope:** found in review of the #1299 follow-up. The badge-in-one-direction shapes are
correct without it, and fixing it means giving the empty inline a resolved level and a slot in
`ApplyBidiReordering`, not a better neighbour. Related:
[the empty inline's `vertical-align`](empty-positioned-inline-ignores-its-own-vertical-align.md) is the
same missing piece: an empty inline takes no part in the line passes that only see words. (Its
`font-size` now does grow the line, #1310, through `CssLineBoxCoordinates.PendingEmptyInlineExtent`, which
still gives it no bidi level or word slot.)
