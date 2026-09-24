# A line grown without placing a word must ask the straddle question itself

_Layout invariant._

`FlowBox` decides whether a line fits its fragmentainer one word at a time. Each word is asked
`WouldStraddleFragmentainer` as it is placed, against the line's extent at that moment. A taller word placed
later is asked itself, and its own rectangle carries the growth. So anything that makes a line taller
*without* placing a word (an empty inline's strut, #1310) gets no check at all. The line can then grow past
the page end, and nothing moves it.

Measured: `<div style="height: 770pt"></div><p>text<span style="font-size: 40pt"></span></p>` ended its line
at 849.2 on an 822pt page. The same markup with a word after the span was correct, because that word was
asked after the growth.

So such a path has to re-ask the words already on the line, where the grown line will put them. Text moves
with the baseline. A replaced element is asked where `ApplyVerticalAlignment` will put it, since at flow
time it still sits at the line's top. Take the break at `LineStartOrdinal`, as a word does
(`CssLayoutEngine.GrowLineForEmptyInlines`).

And it has to exempt a line too deep for *any* fragmentainer, measured from the line's top rather than by
the word's own height: `WouldStraddleFragmentainer`'s own exemption looks only at the word. Without that,
every pass breaks before the line. The resume slot advances each time, so the no-progress backstop never
fires, and the run truncates at the pass cap. The same holds for growth folded into the line *before* a
word is placed and then asked through that word (`<span style="font-size: 1000pt"></span>text`), so
`FlowBox`'s word straddle check asks `CssRect.LineFitsNoFragmentainer` too.

A line that overflows that way must also step the pass's fragmentainer cursor to where it ends
(`StepOverADeepLine`), asked of the line and not of a word on it: the words sit at the baseline, can lie
wholly in a later band, and then straddle nothing. Without the step, the lines after it started at the next
page's top, overlapping the deep line, and were emitted into the wrong fragmentainer.
