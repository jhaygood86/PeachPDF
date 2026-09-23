# A positioned float now paints with the positioned boxes

**Before:** a float that was also positioned (`float: left; position: relative`, no `z-index`) was
drawn in the same step as ordinary floats. That step comes before every positioned box in the same
stacking context. So a positioned ancestor or earlier sibling with an opaque background covered it.
A typical sidebar (`#sidebar { float: left; position: relative }`) inside a positioned, white
`#page` wrapper rendered blank, even though its text was in the PDF.

**Now:** such a float is drawn together with the other positioned boxes, in tree order, as in
browsers ([CSS 2.1 Appendix E](https://www.w3.org/TR/CSS21/zindex.html), where step 5 is
*non-positioned* floats only).

**What an author may need to change:** nothing. Plain (non-positioned) floats are unaffected.
