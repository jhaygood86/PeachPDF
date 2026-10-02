# A line of an inline float that would cross the page foot moves to the next page (#1568)

`w1 <div style="float:left;width:53pt;height:60pt">six words</div>` starting 20pt above a page's foot drew the float's first
line, lost its second and continued with the third at the top of the next page.

**Cause.** A float among inline content is laid out as one unbroken run (`LayoutContentUnbroken`, #1201: nothing up the
inline flow resumes a record the float leaves behind), so its geometry runs on past the foot and each page shows the slice
that falls in it. The emitter claims a line for the page its top is on, so a line that straddles the foot is drawn there,
clipped at the foot, and not on the next page: in neither slice, drawn on no page. Seeds 162, 174 and 200 of the corpus
reduce to this.

**Fix.** `HtmlContainerInt.PushUnbrokenLinesPastThePageFoot`, set only for a float's content, makes the line-opening word
ask `PushLineBelowThePageFoot`: a line whose bottom is past the page's foot and which would fit a page is placed at the
next page's top (the same cursor moves the empty-line shift uses), and everything after it follows. The float's height
follows from its content. Not applied to fixed boxes, nor when there is no real page grid, nor to other unbroken content
(an atomic inline-block still slices).

**Found by running it, not by reading.** The first hook ran only for the first word of the flow: the lines after a wrap
open through another path, which `wordOpensTheLine` covers at the point the word lands, after the wrap decision. A debug
print showed one call where five were expected.

**Not done.** The float's box is still one unbroken box, so the text beside it does not continue beside it on the next
page (the open float fragmentation work); only whole lines are kept.

Evidence: `FloatLineAtPageFootTests` (the three spacer heights fail without the change); full net8.0 suite passes;
300-document corpus against main: 4 documents lose fewer words, none lose more or double more.
