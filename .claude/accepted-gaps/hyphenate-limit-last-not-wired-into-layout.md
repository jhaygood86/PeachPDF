# `hyphenate-limit-last` parses but has no layout effect

`hyphenate-limit-last` (CSS Text 4 §6.3.5: `none | always | column | page | spread`) is parsed,
validated, cascaded, and inherited like any other property — `box.HyphenateLimitLast` resolves
correctly — but nothing in layout consults it. Every value past the initial `none` is currently a
no-op: a line that should be forbidden from ending in a hyphen (the element's last full line under
`always`, or the last line before a column/page/spread break under any of the four non-`none` values)
still hyphenates exactly as it would with `hyphenate-limit-last: none`.

The other four hyphenation control properties #713 added — `hyphenate-character`,
`hyphenate-limit-chars`, `hyphenate-limit-lines`, `hyphenate-limit-zone` — are all real forward-looking
decisions `CssLayoutEngine.FlowBox`/`TryHyphenateWord` can make while building a line, using only state
already in hand at that point (the resolved hyphen glyph, the word/before/after character minimums, how
many consecutive lines already ended in a hyphen, how much space a non-hyphenated break would leave
unfilled). `hyphenate-limit-last` isn't: it asks whether the line currently being built is *going to
turn out to be* the last line before some future break, which isn't knowable during a single forward
pass — it's the same "unknowable until the rest of the content has been flowed" shape as `widows`,
which PeachPDF already solves with a genuine rewind (`HtmlContainerInt.RequestWidowsRewind`/
`TryRewindForWidows`: lay a fragmentainer out, notice after the fact that too few lines followed a
break, and re-run the pass with a smaller line budget). Building that same caliber of rewind — detect a
forbidden hyphenated last line after the fact, undo the split via the existing
`CssLayoutEngine.UndoAbandonedHyphenationSplits` mechanics, and re-flow — plus, for the `column`/`page`/
`spread` values, distinguishing *which kind* of boundary a line sits before, was out of scope for a
change centered on adding the five properties themselves.

Tracked as [issue #723](https://github.com/jhaygood86/PeachPDF/issues/723). See
[Text Layout](docs/html-css-support.md#text-layout).
