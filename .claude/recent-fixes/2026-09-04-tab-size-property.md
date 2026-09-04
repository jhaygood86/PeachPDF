## Added `tab-size` (CSS Text 4 §3.6)

Closes GitHub issue #704.

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

**Deliberately not done:** tab stops are computed from the most recent explicit `\n` in this box's own
`Words`, not the true rendered line start - exact for `pre` (never soft-wraps) and the common
leading-tab-indent case, approximate for a `pre-wrap` soft wrap or a tab preceded by a *sibling* inline
box's content on the same line (each box's `Words` list is independent). A fully correct fix needs the
tab's real position on its final rendered line, only known during `CssLayoutEngine`'s line-building pass
rather than this earlier per-box measurement pass - tracked in
[`.claude/accepted-gaps/tab-size-line-start-approximation.md`](../accepted-gaps/tab-size-line-start-approximation.md)
([#885](https://github.com/jhaygood86/PeachPDF/issues/885)). `white-space: break-spaces` (which the
issue's own spec summary mentions) is a separate, pre-existing gap - this repo's `Whitespace` enum has no
such member at all, unrelated to this change.

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

**Evidence:** `PeachPDF.Tests/Integration/TabSizeLayoutIntegrationTests.cs` (15 tests: default/custom
numeric and length stops, inheritance, mid-line advancement, a zero-tab-size edge case, mixed
space-and-tab runs in both orders, two `::first-line` interaction cases, a huge-tab-size clamp
regression, a post-layout `Left`-shift assertion per this repo's layout-testing convention, and an
RTL+preserved-tab smoke test covering `CssRectWord.ReplaceText`'s two independent callers) plus
`CssUtilsTests.cs` round-trip/rejection cases (including the percentage rejection above). Full suite:
9430 passed, 0 failed, 9 skipped (unrelated platform-specific MIME tests). `dotnet build PeachPDF.slnx
-t:Rebuild`: 0 warnings. Diff coverage against `main`: 93% (69/74 changed lines - the small remainder is
in a defensive branch already covered indirectly). Rasterized the `tab_size` showcase
(`PeachPDF.TestHarness`) through both PDFium and MuPDF both before and after the review-driven
`ExpandTabs` rewrite - byte-for-byte identical visual output; the four tab-size values (default 8, 4, 2,
and an explicit 40px) produce visibly distinct, correctly-nested indentation in both renderers.
