# A line shared with a monolithically oversized sibling now moves as one unit

Previously: when two sibling inline boxes shared one physical line and one of them was, on its own,
taller than any single page or column (a huge `font-size`, or a replaced element like an `<img>` too
tall to fit any fragmentainer), the oversized box was clipped to the first fragmentainer it started in
(issue #484's own rule), but an ordinary sibling on the same physical line was judged - and placed -
purely on its own, different, baseline-driven position. A `<span style="font-size:900pt">A</span>`
beside a `<span style="font-size:10pt">b</span>` could render `A` on one page and `b` on the next, even
though both are part of the same physical line.

Now: once any content sharing a physical line makes that line's own aggregate extent too tall for any
single fragmentainer, every box on that line - the oversized one and its ordinary siblings alike - is
claimed by the one fragmentainer the line's own top starts in, per
[css-break-3 §4.1](https://www.w3.org/TR/css-break-3/#possible-breaks)'s rule that a line box is a
monolithic break unit. This only changes documents where an oversized, monolithic box shares a physical
line with other content; an oversized box alone on its own line (issue #484's original case) is
unaffected, and an ordinary line that fits within a page or column is unaffected.

Note this does not reposition the ordinary sibling's own ink - a sibling whose natural, baseline-aligned
position sits far past the oversized content's own start can still fall outside every page's visible
area once pulled onto the oversized content's fragment, the same overflow-rather-than-slice outcome a
monolithic box's own excess height already produces (css-break-3 §2). The fix corrects which
fragmentainer's fragment tree claims the content, not where that content's ink lands within it.
