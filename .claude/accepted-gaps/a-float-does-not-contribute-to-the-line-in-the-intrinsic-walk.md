# A float does not contribute to the line it sits beside in the intrinsic-width walk

Tracked as **#1033**. A regression from issue #1017 in the `white-space: nowrap` case, and a
pre-existing defect otherwise.

A float is taken out of flow but is still placed *beside* the inline content of the block it is in
([CSS 2.1 §9.5](https://www.w3.org/TR/CSS21/visuren.html#floats)), so for max-content sizing its width
adds to that line. `CssBox.GetMinMaxSumWords` instead sees a float as a block-level box — correctly, as
far as `StartsNewLine` goes, since CSS 2.1 §9.7 blockifies it — and therefore has it *open a line of its
own*, competing with the text beside it instead of adding to it.

Float widths at `font: 16px monospace`:

```html
<div style="float:left"><!-- or with white-space:nowrap -->XY <span style="float:left">ZZZZ</span></div>
```

| outer box | before #1017 | after #1017 | expected |
|---|---|---|---|
| plain | 26.3906 | 26.3906 | 46.1836 |
| `white-space: nowrap` | 46.1836 | **26.3906** | 46.1836 |

26.3906 is `max("XY " = 19.7930, "ZZZZ" = 26.3906)`; 46.1836 is their sum, and is what this engine
itself produced for the `nowrap` row before #1017. A browser was not available to the session that
recorded this — the review pass that found it reported Chromium giving 46.1836, but that figure was not
independently re-measured, so confirm it before treating it as the reference.

The consequence is the usual one for an undercount out of this walk: a shrink-to-fit box sized narrower
than the content it holds, with the float overprinting its neighbour.

## The `nowrap` row is a regression from issue #1017, and was right by accident

Before #1017, `StartsNewLine` returned false for any `white-space: nowrap` box — so a float inside one
did not open a line, and was summed onto it, giving the right answer for a reason that had nothing to do
with floats. #1017 removed `white-space` from that predicate (it says whether content wraps *within* a
line, not whether a box opens one), and the `nowrap` case joined the plain case's pre-existing defect.
Restoring it by putting `white-space` back is not an option: that predicate was wrong for every other
shape, measurably so.

## Why fixing it properly was out of scope for #1017

"A float adds to the line" is not true unconditionally, so this is not a predicate tweak. A float
between two block-level siblings does not add to either one's line, and the flat walk has no notion of
which formatting context a given child is participating in — it carries one running line total and no
record of whether the current position is inside an IFC. The shape of the fix is the one
[.claude/accepted-gaps/atomic-inline-with-block-content-is-not-measured-in-isolation.md](atomic-inline-with-block-content-is-not-measured-in-isolation.md)
(issue #1032) describes for atomic inlines: measure the box in isolation via its own top-level `GetMinMaxWidth` and add
the result, which is what `IsFlexRow` already does per flex item — but gated on the float actually
sitting beside inline content, which is the part that needs the walk to know more than it does today.
