# A multi-column container holding only a float is not an empty box (margins do not collapse through it)

`<div style="columns:2"><div style="float:left;width:120pt;height:86pt"></div></div>text` placed the text at the container's
top, beside the float. The container's height was right (86pt, via `ApplyHeight`'s CSS 2.1 §10.6.7 containment, checked by
logging it), but the *next sibling's* top was computed as if the container were an empty box: `IsMarginCollapseThrough`
treated it as having no in-flow content and no height, so the container's margins collapsed through it and what followed
sat at its top. The same with an in-flow wrapper around the float (a `position: relative` div holding only the float).

**Fix.** `CssBox.IsMarginCollapseThrough` returns false for a multi-column container: it establishes a formatting context
(css-multicol-1 §2), and CSS 2.1 §8.3.1 lets only a box that does *not* establish one collapse through. Its summary already
said so; the code only tested `overflow`. `display: flow-root` and other formatting context roots still fall through the
same hole (the same text lands beside the float) and are left alone here, because the change touches how every empty box's
margins collapse and the corpus showed this one needed it.

Found while reducing a corpus regression (seed 194) that only the page-foot fix exposed: the "text beside a float" was
text after a container whose float it did not clear. Main hid it by accident (the float moved to the next page).

Evidence: `TextAfterAContainerHoldingOnlyAFloat_GoesBelowTheFloat` (both shapes fail without the change); full net8.0 suite
passes; 300-document corpus against main: 2 documents lose fewer words, 1 loses more (seed 162, the page-foot case in the
next change).
