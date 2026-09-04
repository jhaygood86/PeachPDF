## Added `tab-size` (CSS Text 4 §3.6)

Closes GitHub issues #704 and #885.

**Load-bearing idea:** a literal tab character (U+0009) only ever survives past whitespace collapsing
into a `CssRectWord`'s `Text` under `white-space: pre`/`pre-wrap` (`AppendWordsFromText`'s
`preserveSpaces` branch groups any run of preserved spaces/tabs into one whitespace-only word) - so
`tab-size` only has to intervene at the point that word's width is actually measured
(`CssBox.MeasureWordsSize`, plus its two `::first-line` siblings `ApplyFirstLineStyleOverride`/
`RemeasureWordsTail`), not in word-splitting or painting. `ExpandTabs` (`CssBox.cs`) walks a
tab-containing whitespace word's characters against a running `lineX` offset (accumulated across the
box's own `Words` in document order, reset at each explicit `\n` word) and rewrites every tab into the
literal space characters needed to reach the next stop. Because a bare-number `tab-size` defines the
stop as an exact integer multiple of the measured space-glyph width, that substitution reproduces the
correct width *exactly* for the common case (round-trips through the same `g.MeasureString`/`Tc` glyph
path unchanged) - the only approximation is rounding to the nearest whole space character when the
preceding content on the line wasn't itself a whole number of space-widths (proportional fonts, or a
`<length>`-valued `tab-size`). This also means **zero changes to painting**: `FragmentPainter` still just
draws `CssRect.Text`, which by paint time never contains a literal tab.

**What running it showed, not just reading it:** the CSS-OM (Layer A) is not optional plumbing for a
"plain string" property. `css-properties.json`'s `customValidator` alone (Layer B) round-tripped fine
through `CssUtils.SetPropertyValue` directly, so the first integration-test pass looked like tab-size
never took effect at all through an actual `style="tab-size:4"` attribute - it silently stayed at its
initial value `8`. Root cause: nothing in `PropertyFactory.cs` recognized the `tab-size` name at all, so
the real stylesheet/inline-style parser (which every earlier research pass in this session incorrectly
assumed opacity/flex-grow/line-height didn't need) dropped the declaration before it ever reached Layer
B. Fixed by adding `TabSizeProperty`/`Converters.TabSizeConverter` (`LengthConverter.Or(NumberConverter)`,
mirroring `LineHeightConverter`'s shape minus its `normal` keyword) and registering it in
`PropertyFactory.AddLonghand`, matching every other property including `flex-grow`/`orphans`.

**Issue #885 - the real rendered line, not a per-box approximation.** The first pass computed tab stops
from the most recent explicit `\n` in the tab word's *own* `CssBox.Words` - exact for `pre`'s common
leading-tab-indent case, but wrong for a `pre-wrap` soft wrap (no explicit `\n`, so the box has no idea
where the real line broke) or a tab preceded by a *sibling* inline box's content on the same line (each
box's `Words` list is independent, with no shared position). The fix moves the *authoritative* expansion
to `CssLayoutEngine.FlowBox`'s own per-word placement loop, right before `word.Left = coordinates.CurrentX`
- the one place in layout where `coordinates.CurrentX - coordinates.Line.ContentLeft` is a real, correct
"distance from this rendered line's own start," continuously valid across sibling boxes and already reset
at every real wrap (explicit or soft) by the existing wrap-handling code. It re-derives the expansion from
`CssRect.OriginalText`, which a preserved tab's raw text always survives in even after
`CssBox.MeasureWordsSize`'s own (now explicitly provisional) expansion has already overwritten `Text` -
and overwrites whichever of `Text`/`FirstLineText` is authoritative immediately before that word is
placed. This is architecturally the same pattern `TryHyphenateWord` and `RemeasureWordsTail` already use
elsewhere in the same loop: a word's `Width` can be freely recomputed at any point up to immediately
before that word's own placement, since nothing downstream has read it yet. `MeasureWordsSize`'s own
expansion stays exactly as it was - not because it's still needed for correctness once `FlowBox` runs, but
because it's the only width a measurement that never reaches `FlowBox` (an intrinsic/shrink-to-fit width
sum) will ever see, and it keeps a raw, unexpanded tab character from ever reaching `FragmentPainter` on
such a path. This also let `ApplyFirstLineStyleOverride`'s own tab-handling be deleted outright: since it
always runs from inside the very `FlowBox` call whose placement loop then supersedes it, its result was
provably dead the moment it was computed. `white-space: break-spaces` (which issue #704's own spec summary
mentions) is a separate, pre-existing gap - this repo's `Whitespace` enum has no such member at all,
unrelated to this change.

**Post-change review pass caught real bugs, not just style.** `PeachPDF.CSS.Length.TryParse` (used by the
`customValidator`) accepts percentages, so `tab-size: 50%` validated and then silently resolved to a
**zero**-width tab stop (`DerivedStyle.ResolvedTabSize` resolves it against a `hundredPercent` basis of
`0`, since tab-size has no percentage basis) instead of being rejected - fixed by excluding
`Length.Unit.Percent` in the validator. `ExpandTabs` also measured each surviving non-tab character one
glyph at a time instead of the whole run, defeating font shaping (kerning/ligatures) and drifting `lineX`
from what actually gets painted - rewritten to flush each maximal non-tab substring as one
`MeasureString` call. And it had no upper bound: since PeachPDF renders arbitrary, often untrusted HTML,
an unbounded declaration (`tab-size: 1e9`) could balloon a single tab into a multi-megabyte string, and a
`calc()`-derived `NaN`/`Infinity` tab-stop width bypassed the `<= 0` guard entirely (those comparisons are
always `false`) - fixed with a `double.IsFinite` check and a 1000-space expansion cap
(`MaxTabExpansionSpaces`). Also switched space-glyph measurement to the existing, per-`RFont`-memoized
`RFont.GetWhitespaceWidth` (the same idiom `CssUtils.WhiteSpace`/`MeasureWordSpacing` already use) instead
of a fresh unmemoized `MeasureString(" ", ...)` call per tab-containing word.

**Evidence:** `PeachPDF.Tests/Integration/TabSizeLayoutIntegrationTests.cs` (18 tests: default/custom
numeric and length stops, inheritance, mid-line advancement, a zero-tab-size edge case, mixed
space-and-tab runs in both orders, two `::first-line` interaction cases, a huge-tab-size clamp
regression, a post-layout `Left`-shift assertion per this repo's layout-testing convention, an
RTL+preserved-tab smoke test covering `CssRectWord.ReplaceText`'s two independent callers, a
FlowBox-level letter-spacing check, and - for issue #885 specifically - a sibling-inline-box test
(`<pre>ab<span>cd</span>\tX</pre>` expands identically to `<pre>abcd\tX</pre>`) and a `pre-wrap` soft-wrap
test (a tab on a wrapped line expands identically to the same trailing content laid out alone, with no
earlier line's content wrongly bleeding in) - both confirmed to fail before the `FlowBox` fix (temporarily
disabling the new correction reproduced `4` vs `8` and `6` vs `3` character-count mismatches respectively)
and pass after it. Plus `CssUtilsTests.cs` round-trip/rejection cases (including the percentage
rejection above). Full suite: 9434 passed, 0 failed, 9 skipped (unrelated platform-specific MIME tests).
`dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings. Diff coverage against `main`: 100% (78/78 changed
lines). Rasterized the `tab_size` showcase (`PeachPDF.TestHarness`) through both PDFium and MuPDF at each
stage of this change (the review-driven `ExpandTabs` rewrite, then the `FlowBox` line-building fix) -
byte-for-byte identical visual output throughout; the four tab-size values (default 8, 4, 2, and an
explicit 40px) produce visibly distinct, correctly-nested indentation in both renderers. Also spot-checked
three unrelated, layout-heavy existing showcases (`acid2`, `first_line`, `text_indent`) after the `FlowBox`
change, since it touches the single shared inline-placement loop every layout in the suite runs through -
all rendered identically to their known-correct output.
