# A float that follows inline content is placed on the next line, not beside it

Tracked as **#1038**. Pre-existing; found while fixing #1033, and unaffected by it either way.

A float is taken out of the flow but is still placed *beside* the inline content of the block it is
in ([CSS 2.1 §9.5](https://www.w3.org/TR/CSS21/visuren.html#floats), and §9.5.1 rule 6 puts its outer
top no higher than the top of the current line box). PeachPDF places a float whose source position
*follows* inline content at the block's left/right edge instead, on top of that content, and starts
the content after it on a new line.

```html
<div style="width: 300pt; font: 16px monospace">XY <span style="float:left">ZZZZ</span> more words</div>
```

| box | x | y |
|---|---|---|
| `XY` | 20 | 20 |
| the float | 20 | 20 |
| `more words` | 20 | 34.25 |

A float whose source position is *before* the inline content is placed correctly, and the text does
wrap around it — so this is specifically a float that follows content already on the line.

## Why it happens, and why it is not a layout-engine tweak

`DomParser.CorrectInlineBoxesParent` wraps the inline run in a CSS 2.1 §9.2.1.1 anonymous block: §9.7
blockifies the float, so `ContainsVariantBoxes` sees a block-level child among inline ones. That
wrapper *ends the line at the float*, and the float is then placed as an ordinary block-level sibling
after it. Browsers generate no such wrapper — an out-of-flow box does not make a block container's
children non-inline — but removing it here is a box-tree change, not a placement one:
`DomUtils.ContainsInlinesOnly` (which `CssBox.LayoutContents` dispatches inline-vs-block layout on)
and `CssLayoutEngine.FlowBox` would both have to handle a floated child sitting among a box's own
inline content, a shape the engine has never been given.

## What #1033 did and did not change

#1033 was the same fact in the **intrinsic-width walk**: the float did not add its width to the line
it sits beside, so a shrink-to-fit box was sized to the wider of the two rather than their sum. That
is fixed, and the box now reserves room for both — see
[.claude/recent-fixes/2026-09-13-a-float-contributes-to-the-line-it-sits-beside.md](../recent-fixes/2026-09-13-a-float-contributes-to-the-line-it-sits-beside.md).
What remains is that layout does not then *put* the float in that room. Two floats with no inline
content between them are already placed side by side correctly, so the measurement fix is fully
visible there and only the "float after inline content" shape still draws wrong.
